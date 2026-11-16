// <copyright file="SqlScalarExpression.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Parses and evaluates a scalar SQL expression against a row: literals, column references, SQLite's scalar
/// functions (string, numeric and date/time), <c>CAST</c>, <c>||</c> and arithmetic — and the WHERE conditions
/// built from them. It is the one evaluator every row path shares: the SELECT list of the single-table path, the
/// directory-mode and single-file WHERE evaluators, the UPDATE/DELETE scan evaluator, the AST executor's function
/// fallback, and a SELECT without FROM.
/// <para>
/// Up to 2.1.4 a function applied to a column was not evaluated on any of those paths: the SELECT list returned the
/// row unchanged, and every WHERE evaluator read the left operand as a column name, so
/// <c>strftime('%Y', at) = '2026'</c> matched no row — the shape linq2db's SQLite dialect emits for every
/// <see cref="DateTime"/> comparison. A condition is only taken here when one of its operands is a computed
/// expression; plain <c>column op literal</c> conditions keep their existing evaluators, collations and fast paths.
/// </para>
/// </summary>
internal static class SqlScalarExpression
{
    private const int CacheCapacity = 4096;

    private static readonly ConcurrentDictionary<string, Node?> ExpressionCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Condition?> ConditionCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, object> EmptyRow = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex NumericPrefix = new(
        @"^\s*[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly HashSet<string> ScalarFunctions = new(StringComparer.Ordinal)
    {
        "UPPER", "LOWER", "LENGTH", "SUBSTR", "SUBSTRING", "TRIM", "LTRIM", "RTRIM", "REPLACE", "INSTR",
        "COALESCE", "IFNULL", "NULLIF", "IIF", "TYPEOF", "ABS", "ROUND", "SIGN", "MIN", "MAX", "QUOTE", "CONCAT",
        "DATE", "TIME", "DATETIME", "JULIANDAY", "UNIXEPOCH", "STRFTIME",
    };

    // ── Public surface ──────────────────────────────────────────────────────────────────────────

    /// <summary>Parses <paramref name="text"/> as one complete scalar expression (cached).</summary>
    internal static bool TryParse(string text, out Node node)
    {
        node = GetOrAdd(ExpressionCache, text, static t => new Parser(t).ParseWhole())!;
        return node is not null;
    }

    /// <summary>Returns <c>true</c> when the node is computed — a function, CAST, operator — not a literal or column.</summary>
    internal static bool IsComputed(Node node) => node is not (Literal or Column);

    /// <summary>Returns <c>true</c> when the node is a plain column reference.</summary>
    internal static bool IsColumn(Node node) => node is Column;

    /// <summary>Evaluates a parsed expression against a row (<c>null</c> is SQL NULL).</summary>
    internal static object? Evaluate(Node node, Dictionary<string, object>? row) => Eval(node, row ?? EmptyRow);

    /// <summary>
    /// Evaluates a WHERE condition against a row when it involves a computed expression. Returns <c>false</c> —
    /// "not mine" — when the condition has no computed operand or uses syntax this evaluator does not cover, so the
    /// caller's own evaluator stays in charge of it.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="condition">The condition text.</param>
    /// <param name="result">Whether the row matches (SQL NULL counts as no match).</param>
    internal static bool TryEvaluateCondition(Dictionary<string, object> row, string condition, out bool result)
    {
        result = false;
        if (string.IsNullOrWhiteSpace(condition) || condition.IndexOf('(') < 0)
        {
            return false;   // every computed operand this evaluator handles contains a parenthesis
        }

        var compiled = GetOrAdd(ConditionCache, condition, static c => ConditionCompiler.Compile(c));
        if (compiled is null)
        {
            return false;
        }

        result = compiled.Evaluate(row) == true;
        return true;
    }

    /// <summary>SQLite's text form of a value (what <c>upper</c>, <c>length</c>, <c>||</c> and CAST AS TEXT see).</summary>
    internal static string ToText(object? value) => value switch
    {
        null or DBNull => string.Empty,
        string s => s,
        bool b => b ? "1" : "0",
        double d => FormatReal(d),
        float f => FormatReal(f),
        DateTime dt => dt.ToString(SqlLiteralFormatter.DateTimeFormat, CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString(SqlLiteralFormatter.DateTimeOffsetFormat, CultureInfo.InvariantCulture),
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        Guid g => g.ToString("D"),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    // ── Expression nodes ────────────────────────────────────────────────────────────────────────

    /// <summary>A parsed scalar expression.</summary>
    internal abstract class Node;

    private sealed class Literal(object? value) : Node
    {
        public object? Value { get; } = value;
    }

    private sealed class Column(string? qualifier, string name, bool doubleQuoted) : Node
    {
        public string? Qualifier { get; } = qualifier;
        public string Name { get; } = name;
        public bool DoubleQuoted { get; } = doubleQuoted;
    }

    private sealed class Call(string name, Node[] args) : Node
    {
        public string Name { get; } = name;
        public Node[] Args { get; } = args;
    }

    private sealed class Cast(Node operand, string typeName) : Node
    {
        public Node Operand { get; } = operand;
        public string TypeName { get; } = typeName;
    }

    private sealed class Binary(string op, Node left, Node right) : Node
    {
        public string Op { get; } = op;
        public Node Left { get; } = left;
        public Node Right { get; } = right;
    }

    private sealed class Negate(Node operand) : Node
    {
        public Node Operand { get; } = operand;
    }

    // ── Parser ──────────────────────────────────────────────────────────────────────────────────

    private sealed class ParseFailure : Exception;

    private sealed class Parser(string text)
    {
        private int _pos;

        public Node? ParseWhole()
        {
            try
            {
                var node = ParseExpression(0);
                SkipWhitespace();
                return _pos == text.Length ? node : null;
            }
            catch (ParseFailure)
            {
                return null;
            }
        }

        private Node ParseExpression(int minPrecedence)
        {
            var left = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                var (op, precedence) = PeekBinaryOperator();
                if (op is null || precedence < minPrecedence)
                {
                    return left;
                }

                _pos += op.Length;
                var right = ParseExpression(precedence + 1);
                left = new Binary(op, left, right);
            }
        }

        private (string? Op, int Precedence) PeekBinaryOperator()
        {
            if (_pos >= text.Length)
            {
                return (null, 0);
            }

            // SQLite precedence, tightest first: ||, then * / %, then + -.
            if (text.AsSpan(_pos).StartsWith("||", StringComparison.Ordinal))
            {
                return ("||", 3);
            }

            return text[_pos] switch
            {
                '*' or '/' or '%' => (text[_pos].ToString(), 2),
                '+' or '-' => (text[_pos].ToString(), 1),
                _ => (null, 0),
            };
        }

        private Node ParseUnary()
        {
            SkipWhitespace();
            if (_pos < text.Length && text[_pos] == '-')
            {
                _pos++;
                var operand = ParseUnary();
                return operand is Literal { Value: long l } ? new Literal(-l)
                    : operand is Literal { Value: double d } ? new Literal(-d)
                    : new Negate(operand);
            }

            if (_pos < text.Length && text[_pos] == '+')
            {
                _pos++;
                return ParseUnary();
            }

            return ParsePrimary();
        }

        private Node ParsePrimary()
        {
            SkipWhitespace();
            if (_pos >= text.Length)
            {
                throw new ParseFailure();
            }

            char c = text[_pos];
            if (c == '(')
            {
                _pos++;
                var inner = ParseExpression(0);
                Expect(')');
                return inner;
            }

            if (c == '\'')
            {
                return new Literal(ReadStringLiteral());
            }

            if (char.IsAsciiDigit(c) || (c == '.' && _pos + 1 < text.Length && char.IsAsciiDigit(text[_pos + 1])))
            {
                return ReadNumber();
            }

            if (c is '[' or '"' or '`')
            {
                return ReadColumn();
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = _pos;
                var word = ReadWord();
                SkipWhitespace();
                if (_pos < text.Length && text[_pos] == '(')
                {
                    return ReadCall(word);
                }

                switch (word.ToUpperInvariant())
                {
                    case "NULL":
                        return new Literal(null);
                    case "TRUE":
                        return new Literal(1L);
                    case "FALSE":
                        return new Literal(0L);
                    case "CURRENT_TIMESTAMP":
                        return new Call("DATETIME", [new Literal("now")]);
                    case "CURRENT_DATE":
                        return new Call("DATE", [new Literal("now")]);
                    case "CURRENT_TIME":
                        return new Call("TIME", [new Literal("now")]);
                }

                _pos = start;
                return ReadColumn();
            }

            throw new ParseFailure();
        }

        private Node ReadCall(string word)
        {
            var name = word.ToUpperInvariant();
            Expect('(');

            if (name == "CAST")
            {
                var operand = ParseExpression(0);
                SkipWhitespace();
                if (!ReadKeyword("AS"))
                {
                    throw new ParseFailure();
                }

                int typeStart = _pos;
                int depth = 0;
                while (_pos < text.Length && (text[_pos] != ')' || depth > 0))
                {
                    depth += text[_pos] == '(' ? 1 : text[_pos] == ')' ? -1 : 0;
                    _pos++;
                }

                var typeName = text[typeStart.._pos].Trim();
                Expect(')');
                return typeName.Length == 0 ? throw new ParseFailure() : new Cast(operand, typeName);
            }

            if (!ScalarFunctions.Contains(name))
            {
                throw new ParseFailure();   // aggregates, unknown and engine-state functions stay on their own paths
            }

            var args = new List<Node>();
            SkipWhitespace();
            if (_pos < text.Length && text[_pos] == ')')
            {
                _pos++;
            }
            else
            {
                while (true)
                {
                    args.Add(ParseExpression(0));
                    SkipWhitespace();
                    if (_pos < text.Length && text[_pos] == ',')
                    {
                        _pos++;
                        continue;
                    }

                    Expect(')');
                    break;
                }
            }

            // One-argument MIN/MAX is the aggregate, not the scalar.
            return name is "MIN" or "MAX" && args.Count < 2 ? throw new ParseFailure() : new Call(name, [.. args]);
        }

        private Column ReadColumn()
        {
            var (first, firstQuoted) = ReadIdentifier();
            SkipWhitespace();
            if (_pos < text.Length && text[_pos] == '.')
            {
                _pos++;
                SkipWhitespace();
                var (second, secondQuoted) = ReadIdentifier();
                return new Column(first, second, secondQuoted);
            }

            return new Column(null, first, firstQuoted);
        }

        private (string Name, bool DoubleQuoted) ReadIdentifier()
        {
            if (_pos >= text.Length)
            {
                throw new ParseFailure();
            }

            char open = text[_pos];
            char close = open switch { '[' => ']', '"' => '"', '`' => '`', _ => '\0' };
            if (close != '\0')
            {
                int end = text.IndexOf(close, _pos + 1);
                if (end < 0)
                {
                    throw new ParseFailure();
                }

                var name = text[(_pos + 1)..end];
                _pos = end + 1;
                return (name, open == '"');
            }

            if (!(char.IsLetter(open) || open == '_'))
            {
                throw new ParseFailure();
            }

            return (ReadWord(), false);
        }

        private string ReadWord()
        {
            int start = _pos;
            while (_pos < text.Length && (char.IsLetterOrDigit(text[_pos]) || text[_pos] is '_' or '$'))
            {
                _pos++;
            }

            return text[start.._pos];
        }

        private bool ReadKeyword(string keyword)
        {
            if (_pos + keyword.Length <= text.Length &&
                text.AsSpan(_pos, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase) &&
                (_pos + keyword.Length == text.Length || !char.IsLetterOrDigit(text[_pos + keyword.Length])))
            {
                _pos += keyword.Length;
                SkipWhitespace();
                return true;
            }

            return false;
        }

        private string ReadStringLiteral()
        {
            var sb = new StringBuilder();
            _pos++;   // opening quote
            while (_pos < text.Length)
            {
                char c = text[_pos++];
                if (c != '\'')
                {
                    sb.Append(c);
                    continue;
                }

                if (_pos < text.Length && text[_pos] == '\'')
                {
                    sb.Append('\'');   // '' is one quote
                    _pos++;
                    continue;
                }

                return sb.ToString();
            }

            throw new ParseFailure();   // unterminated literal
        }

        private Literal ReadNumber()
        {
            int start = _pos;
            bool isReal = false;
            while (_pos < text.Length && (char.IsAsciiDigit(text[_pos]) || text[_pos] == '.'))
            {
                isReal |= text[_pos] == '.';
                _pos++;
            }

            if (_pos < text.Length && text[_pos] is 'e' or 'E')
            {
                isReal = true;
                _pos++;
                if (_pos < text.Length && text[_pos] is '+' or '-')
                {
                    _pos++;
                }

                while (_pos < text.Length && char.IsAsciiDigit(text[_pos]))
                {
                    _pos++;
                }
            }

            var span = text.AsSpan(start, _pos - start);
            if (!isReal && long.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            {
                return new Literal(l);
            }

            return double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? new Literal(d)
                : throw new ParseFailure();
        }

        private void Expect(char c)
        {
            SkipWhitespace();
            if (_pos >= text.Length || text[_pos] != c)
            {
                throw new ParseFailure();
            }

            _pos++;
        }

        private void SkipWhitespace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos]))
            {
                _pos++;
            }
        }
    }

    // ── Evaluation ──────────────────────────────────────────────────────────────────────────────

    private static object? Eval(Node node, Dictionary<string, object> row) => node switch
    {
        Literal literal => literal.Value,
        Column column => ResolveColumn(row, column),
        Call call => InvokeFunction(call, row),
        Cast cast => CastValue(Eval(cast.Operand, row), cast.TypeName),
        Binary binary => EvalBinary(binary.Op, Eval(binary.Left, row), Eval(binary.Right, row)),
        Negate negate => EvalBinary("-", 0L, Eval(negate.Operand, row)),
        _ => null,
    };

    private static object? ResolveColumn(Dictionary<string, object> row, Column column)
    {
        if (column.Qualifier is not null && row.TryGetValue($"{column.Qualifier}.{column.Name}", out var qualified))
        {
            return Normalize(qualified);
        }

        if (row.TryGetValue(column.Name, out var exact))
        {
            return Normalize(exact);
        }

        foreach (var (key, value) in row)
        {
            if (key.Equals(column.Name, StringComparison.OrdinalIgnoreCase) ||
                key.EndsWith("." + column.Name, StringComparison.OrdinalIgnoreCase))
            {
                return Normalize(value);
            }
        }

        // SQLite reads a double-quoted name that is not a column as a string literal.
        return column.DoubleQuoted && column.Qualifier is null ? column.Name : null;
    }

    private static object? Normalize(object? value) => value is DBNull ? null : value;

    private static object? InvokeFunction(Call call, Dictionary<string, object> row)
    {
        var args = new object?[call.Args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            args[i] = Eval(call.Args[i], row);
        }

        return InvokeScalarFunction(call.Name, args);
    }

    /// <summary>
    /// Evaluates one of SQLite's scalar functions over already-evaluated arguments. Returns <c>false</c> for a name
    /// this evaluator does not implement. Shared with the AST executor so both paths agree.
    /// </summary>
    /// <param name="upperName">The upper-case function name.</param>
    /// <param name="args">The evaluated arguments.</param>
    /// <param name="result">The function result (<c>null</c> is SQL NULL).</param>
    internal static bool TryInvokeScalarFunction(string upperName, object?[] args, out object? result)
    {
        result = null;
        if (!ScalarFunctions.Contains(upperName) || (upperName is "MIN" or "MAX" && args.Length < 2))
        {
            return false;
        }

        for (int i = 0; i < args.Length; i++)
        {
            args[i] = Normalize(args[i]);
        }

        result = InvokeScalarFunction(upperName, args);
        return true;
    }

    private static object? InvokeScalarFunction(string name, object?[] a)
    {
        if (SqlDateTimeFunctions.IsDateTimeFunction(name))
        {
            return SqlDateTimeFunctions.Evaluate(name, a);
        }

        object? Arg(int i) => i < a.Length ? a[i] : null;

        switch (name)
        {
            case "UPPER":
                return Arg(0) is null ? null : ToText(Arg(0)).ToUpperInvariant();
            case "LOWER":
                return Arg(0) is null ? null : ToText(Arg(0)).ToLowerInvariant();
            case "LENGTH":
                return Arg(0) switch
                {
                    null => null,
                    byte[] bytes => (long)bytes.Length,
                    var v => (long)ToText(v).Length,
                };
            case "SUBSTR" or "SUBSTRING":
                return a.Length is < 2 or > 3 || Arg(0) is null || Arg(1) is null || (a.Length == 3 && Arg(2) is null)
                    ? null
                    : Substr(ToText(Arg(0)), ToInteger(Arg(1)), a.Length == 3 ? ToInteger(Arg(2)) : null);
            case "TRIM" or "LTRIM" or "RTRIM":
            {
                if (Arg(0) is null || (a.Length > 1 && Arg(1) is null))
                {
                    return null;
                }

                var chars = a.Length > 1 ? ToText(Arg(1)).ToCharArray() : [' '];
                var s = ToText(Arg(0));
                return name switch
                {
                    "LTRIM" => s.TrimStart(chars),
                    "RTRIM" => s.TrimEnd(chars),
                    _ => s.Trim(chars),
                };
            }

            case "REPLACE":
                if (a.Length != 3 || Arg(0) is null || Arg(1) is null || Arg(2) is null)
                {
                    return null;
                }

                var find = ToText(Arg(1));
                return find.Length == 0 ? ToText(Arg(0)) : ToText(Arg(0)).Replace(find, ToText(Arg(2)), StringComparison.Ordinal);
            case "INSTR":
                return Arg(0) is null || Arg(1) is null
                    ? null
                    : (long)(ToText(Arg(0)).IndexOf(ToText(Arg(1)), StringComparison.Ordinal) + 1);
            case "COALESCE":
                foreach (var v in a)
                {
                    if (v is not null)
                    {
                        return v;
                    }
                }

                return null;
            case "IFNULL":
                return a.Length == 2 ? Arg(0) ?? Arg(1) : null;
            case "NULLIF":
                return a.Length == 2 && TryCompare(Arg(0), Arg(1), out var cmp) && cmp == 0 ? null : Arg(0);
            case "IIF":
                return a.Length == 3 ? (IsTrue(Arg(0)) == true ? Arg(1) : Arg(2)) : null;
            case "TYPEOF":
                return Arg(0) switch
                {
                    null => "null",
                    sbyte or byte or short or ushort or int or uint or long or ulong or bool => "integer",
                    float or double or decimal => "real",
                    byte[] => "blob",
                    _ => "text",
                };
            case "ABS":
                return ToNumber(Arg(0)) switch
                {
                    null => null,
                    long l => Math.Abs(l),
                    double d => (object)Math.Abs(d),
                    _ => null,
                };
            case "ROUND":
            {
                if (Arg(0) is null || (a.Length > 1 && Arg(1) is null))
                {
                    return null;
                }

                int digits = a.Length > 1 ? (int)Math.Clamp(ToInteger(Arg(1)), 0, 15) : 0;
                return Math.Round(ToReal(Arg(0)), digits, MidpointRounding.AwayFromZero);
            }

            case "SIGN":
                return ToNumber(Arg(0)) switch
                {
                    long l => (long)Math.Sign(l),
                    double d => (long)Math.Sign(d),
                    _ => null,
                };
            case "MIN" or "MAX":
            {
                object? best = null;
                foreach (var v in a)
                {
                    if (v is null)
                    {
                        return null;
                    }

                    if (best is null || (TryCompare(v, best, out var c) && (name == "MIN" ? c < 0 : c > 0)))
                    {
                        best = v;
                    }
                }

                return best;
            }

            case "QUOTE":
                return SqlFunctions.Quote(Arg(0));
            case "CONCAT":
            {
                var sb = new StringBuilder();
                foreach (var v in a)
                {
                    sb.Append(ToText(v));
                }

                return sb.ToString();
            }

            default:
                return null;
        }
    }

    /// <summary>SQLite's substr(): 1-based start, a negative start counts from the end, a negative length looks back.</summary>
    private static string Substr(string s, long start, long? length)
    {
        long len = s.Length;
        long p1 = start;
        long p2 = length ?? len;
        bool negativeLength = p2 < 0;
        if (negativeLength)
        {
            p2 = -p2;
        }

        if (p1 < 0)
        {
            p1 += len;
            if (p1 < 0)
            {
                p2 += p1;
                if (p2 < 0)
                {
                    p2 = 0;
                }

                p1 = 0;
            }
        }
        else if (p1 > 0)
        {
            p1--;
        }
        else if (p2 > 0)
        {
            p2--;
        }

        if (negativeLength)
        {
            p1 -= p2;
            if (p1 < 0)
            {
                p2 += p1;
                p1 = 0;
            }
        }

        if (p1 > len)
        {
            return string.Empty;
        }

        if (p1 + p2 > len)
        {
            p2 = len - p1;
        }

        return p2 <= 0 ? string.Empty : s.Substring((int)p1, (int)p2);
    }

    private static object? CastValue(object? value, string typeName)
    {
        if (value is null)
        {
            return null;
        }

        // SQLite's affinity rules, by substring of the declared type name.
        var type = typeName.ToUpperInvariant();
        if (type.Contains("INT", StringComparison.Ordinal))
        {
            return ToInteger(value);
        }

        if (type.Contains("CHAR", StringComparison.Ordinal) || type.Contains("CLOB", StringComparison.Ordinal) ||
            type.Contains("TEXT", StringComparison.Ordinal))
        {
            return ToText(value);
        }

        if (type.Contains("BLOB", StringComparison.Ordinal))
        {
            return value is byte[] ? value : Encoding.UTF8.GetBytes(ToText(value));
        }

        if (type.Contains("REAL", StringComparison.Ordinal) || type.Contains("FLOA", StringComparison.Ordinal) ||
            type.Contains("DOUB", StringComparison.Ordinal))
        {
            return ToReal(value);
        }

        // NUMERIC: an integer when the value is integral, otherwise a real.
        return ToNumber(value) switch
        {
            double d when d == Math.Floor(d) && Math.Abs(d) < 9.2e18 => (long)d,
            var n => n,
        };
    }

    private static object? EvalBinary(string op, object? left, object? right)
    {
        if (left is null || right is null)
        {
            return null;
        }

        if (op == "||")
        {
            return ToText(left) + ToText(right);
        }

        var l = ToNumber(left);
        var r = ToNumber(right);
        if (l is long li && r is long ri)
        {
            return op switch
            {
                "+" => li + ri,
                "-" => li - ri,
                "*" => li * ri,
                "/" => ri == 0 ? null : li / ri,
                "%" => ri == 0 ? null : li % ri,
                _ => null,
            };
        }

        double ld = ToReal(l);
        double rd = ToReal(r);
        return op switch
        {
            "+" => ld + rd,
            "-" => ld - rd,
            "*" => ld * rd,
            "/" => rd == 0 ? null : ld / rd,
            "%" => rd == 0 ? null : (double)((long)ld % (long)rd),
            _ => null,
        };
    }

    // ── Values: numbers, truth, comparison ──────────────────────────────────────────────────────

    /// <summary>The numeric value SQLite gives a value: a <see cref="long"/> or a <see cref="double"/>; NULL stays null.</summary>
    private static object? ToNumber(object? value)
    {
        switch (value)
        {
            case null or DBNull:
                return null;
            case bool b:
                return b ? 1L : 0L;
            case sbyte or byte or short or ushort or int or uint or long:
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            case ulong u:
                return u <= long.MaxValue ? (long)u : (double)u;
            case float or double or decimal:
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        // Text converts by its longest numeric prefix ('12abc' is 12, 'abc' is 0), as in SQLite.
        var m = NumericPrefix.Match(ToText(value));
        if (!m.Success)
        {
            return 0L;
        }

        var text = m.Value.Trim();
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l))
        {
            return l;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0L;
    }

    private static long ToInteger(object? value) => ToNumber(value) switch
    {
        long l => l,
        double d when !double.IsNaN(d) => d >= 9.2e18 ? long.MaxValue : d <= -9.2e18 ? long.MinValue : (long)Math.Truncate(d),
        _ => 0L,
    };

    private static double ToReal(object? value) => ToNumber(value) switch
    {
        long l => l,
        double d => d,
        _ => 0d,
    };

    /// <summary>SQLite's truth of a value: NULL is unknown, otherwise its numeric value is non-zero.</summary>
    private static bool? IsTrue(object? value) => ToNumber(value) switch
    {
        null => null,
        long l => l != 0,
        double d => d != 0,
        _ => false,
    };

    private static bool IsNumeric(object value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal or bool;

    /// <summary>
    /// Compares two non-NULL values as SQLite does: numbers numerically (a numeric-looking text operand is read as a
    /// number, the leniency the engine's other evaluators already apply), text by its bytes (BINARY collation), and
    /// across classes numbers before text before blobs. Returns <c>false</c> when either side is NULL.
    /// </summary>
    internal static bool TryCompare(object? left, object? right, out int comparison)
    {
        comparison = 0;
        left = Normalize(left);
        right = Normalize(right);
        if (left is null || right is null)
        {
            return false;
        }

        if (left is DateTime or DateTimeOffset or Guid)
        {
            left = ToText(left);
        }

        if (right is DateTime or DateTimeOffset or Guid)
        {
            right = ToText(right);
        }

        bool leftNumeric = IsNumeric(left) || (left is string ls && IsWholeNumber(ls));
        bool rightNumeric = IsNumeric(right) || (right is string rs && IsWholeNumber(rs));
        if (leftNumeric && rightNumeric)
        {
            var l = ToNumber(left);
            var r = ToNumber(right);
            comparison = l is long li && r is long ri ? li.CompareTo(ri) : ToReal(l).CompareTo(ToReal(r));
            return true;
        }

        int leftClass = ClassOf(left, leftNumeric);
        int rightClass = ClassOf(right, rightNumeric);
        if (leftClass != rightClass)
        {
            comparison = leftClass.CompareTo(rightClass);
            return true;
        }

        if (left is byte[] lb && right is byte[] rb)
        {
            comparison = lb.AsSpan().SequenceCompareTo(rb);
            return true;
        }

        comparison = Math.Sign(string.CompareOrdinal(ToText(left), ToText(right)));
        return true;
    }

    private static int ClassOf(object value, bool numeric) => numeric ? 0 : value is byte[] ? 2 : 1;

    private static bool IsWholeNumber(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length > 0 &&
               double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _) &&
               NumericPrefix.Match(trimmed) is { Success: true } m && m.Length == trimmed.Length;
    }

    private static string FormatReal(double value)
    {
        // SQLite prints a real with a decimal point: 5.0, not 5.
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return double.IsFinite(value) && !text.Contains('.') && !text.Contains('E') ? text + ".0" : text;
    }

    private static TValue? GetOrAdd<TValue>(ConcurrentDictionary<string, TValue?> cache, string key, Func<string, TValue?> factory)
        where TValue : class
    {
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (cache.Count >= CacheCapacity)
        {
            cache.Clear();   // the statements a process uses are few; a full clear keeps the bound trivially
        }

        var value = factory(key);
        cache[key] = value;
        return value;
    }

    // ── Conditions ──────────────────────────────────────────────────────────────────────────────

    private abstract class Condition
    {
        /// <summary>SQL three-valued result: <c>null</c> is unknown.</summary>
        public abstract bool? Evaluate(Dictionary<string, object> row);
    }

    private sealed class AndCondition(Condition[] parts) : Condition
    {
        public override bool? Evaluate(Dictionary<string, object> row)
        {
            bool unknown = false;
            foreach (var part in parts)
            {
                var value = part.Evaluate(row);
                if (value == false)
                {
                    return false;
                }

                unknown |= value is null;
            }

            return unknown ? null : true;
        }
    }

    private sealed class OrCondition(Condition[] parts) : Condition
    {
        public override bool? Evaluate(Dictionary<string, object> row)
        {
            bool unknown = false;
            foreach (var part in parts)
            {
                var value = part.Evaluate(row);
                if (value == true)
                {
                    return true;
                }

                unknown |= value is null;
            }

            return unknown ? null : false;
        }
    }

    private sealed class NotCondition(Condition inner) : Condition
    {
        public override bool? Evaluate(Dictionary<string, object> row) => !inner.Evaluate(row);
    }

    private sealed class CompareCondition(string op, Node left, Node right) : Condition
    {
        public override bool? Evaluate(Dictionary<string, object> row)
        {
            if (!TryCompare(Eval(left, row), Eval(right, row), out var c))
            {
                return null;
            }

            return op switch
            {
                "=" or "==" => c == 0,
                "!=" or "<>" => c != 0,
                "<" => c < 0,
                "<=" => c <= 0,
                ">" => c > 0,
                ">=" => c >= 0,
                _ => null,
            };
        }
    }

    private sealed class IsCondition(Node left, Node right, bool negated) : Condition
    {
        public override bool? Evaluate(Dictionary<string, object> row)
        {
            // IS / IS NOT never yield NULL: NULL IS NULL is true.
            var l = Eval(left, row);
            var r = Eval(right, row);
            bool same = l is null || r is null ? l is null && r is null : TryCompare(l, r, out var c) && c == 0;
            return negated ? !same : same;
        }
    }

    private sealed class LikeCondition(Node value, Node pattern, Node? escape, bool negated) : Condition
    {
        public override bool? Evaluate(Dictionary<string, object> row)
        {
            var v = Eval(value, row);
            var p = Eval(pattern, row);
            if (v is null || p is null)
            {
                return null;
            }

            char? esc = null;
            if (escape is not null)
            {
                var e = ToText(Eval(escape, row));
                esc = e.Length == 1 ? e[0] : null;
            }

            bool matched = SqlLikePattern.IsMatch(ToText(v), ToText(p), esc);
            return negated ? !matched : matched;
        }
    }

    private sealed class TruthCondition(Node expression) : Condition
    {
        public override bool? Evaluate(Dictionary<string, object> row) => IsTrue(Eval(expression, row));
    }

    private sealed class ConditionCompiler
    {
        private static readonly string[] SymbolOperators = ["<=", ">=", "<>", "!=", "==", "=", "<", ">"];

        private bool _hasComputedOperand;

        public static Condition? Compile(string condition)
        {
            var compiler = new ConditionCompiler();
            var compiled = compiler.CompileCondition(condition);
            return compiled is not null && compiler._hasComputedOperand ? compiled : null;
        }

        private Condition? CompileCondition(string text)
        {
            text = SqlInPredicate.StripOuterParentheses(text.Trim());
            if (text.Length == 0 || FindTopLevelKeyword(text, "BETWEEN") >= 0)
            {
                return null;   // BETWEEN's own AND would be split as a conjunction; leave it to the caller
            }

            var orParts = SqlInPredicate.SplitTopLevelLogical(text, "OR");
            if (orParts.Count > 1)
            {
                return CompileAll(orParts, static p => new OrCondition(p));
            }

            var andParts = SqlInPredicate.SplitTopLevelLogical(text, "AND");
            if (andParts.Count > 1)
            {
                return CompileAll(andParts, static p => new AndCondition(p));
            }

            if (StartsWithKeyword(text, "NOT"))
            {
                var inner = CompileCondition(text[3..]);
                return inner is null ? null : new NotCondition(inner);
            }

            return CompileLeaf(text);
        }

        private Condition? CompileAll(List<string> parts, Func<Condition[], Condition> combine)
        {
            var compiled = new Condition[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                var part = CompileCondition(parts[i]);
                if (part is null)
                {
                    return null;
                }

                compiled[i] = part;
            }

            return combine(compiled);
        }

        private Condition? CompileLeaf(string text)
        {
            // Keyword operators first: "a NOT LIKE b" also contains no symbol operator, but "x IS NOT NULL" must not
            // be read as a comparison.
            foreach (var keyword in (string[])["IN", "GLOB", "REGEXP", "MATCH", "EXISTS"])
            {
                if (FindTopLevelKeyword(text, keyword) >= 0)
                {
                    return null;
                }
            }

            int likeAt = FindTopLevelKeyword(text, "LIKE");
            if (likeAt >= 0)
            {
                var left = text[..likeAt].TrimEnd();
                bool negated = EndsWithKeyword(left, "NOT");
                if (negated)
                {
                    left = left[..^3].TrimEnd();
                }

                var right = text[(likeAt + 4)..];
                string? escapeText = null;
                int escapeAt = FindTopLevelKeyword(right, "ESCAPE");
                if (escapeAt >= 0)
                {
                    escapeText = right[(escapeAt + 6)..];
                    right = right[..escapeAt];
                }

                var value = ParseOperand(left);
                var pattern = ParseOperand(right);
                var escape = escapeText is null ? null : ParseOperand(escapeText);
                return value is null || pattern is null || (escapeText is not null && escape is null)
                    ? null
                    : new LikeCondition(value, pattern, escape, negated);
            }

            int isAt = FindTopLevelKeyword(text, "IS");
            if (isAt >= 0)
            {
                var rest = text[(isAt + 2)..].TrimStart();
                bool negated = StartsWithKeyword(rest, "NOT");
                if (negated)
                {
                    rest = rest[3..];
                }

                var left = ParseOperand(text[..isAt]);
                var right = ParseOperand(rest);
                return left is null || right is null ? null : new IsCondition(left, right, negated);
            }

            var (op, at) = FindTopLevelSymbolOperator(text);
            if (op is null)
            {
                var expression = ParseOperand(text);
                return expression is null ? null : new TruthCondition(expression);
            }

            var lhs = ParseOperand(text[..at]);
            var rhs = ParseOperand(text[(at + op.Length)..]);
            return lhs is null || rhs is null ? null : new CompareCondition(op, lhs, rhs);
        }

        private Node? ParseOperand(string text)
        {
            if (!TryParse(text.Trim(), out var node))
            {
                return null;
            }

            _hasComputedOperand |= IsComputed(node);
            return node;
        }

        private static (string? Op, int At) FindTopLevelSymbolOperator(string text)
        {
            int depth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (TrySkipQuoted(text, ref i))
                {
                    continue;
                }

                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                }
                else if (depth == 0 && c is '<' or '>' or '=' or '!')
                {
                    foreach (var op in SymbolOperators)
                    {
                        if (text.AsSpan(i).StartsWith(op, StringComparison.Ordinal))
                        {
                            return (op, i);
                        }
                    }
                }
            }

            return (null, -1);
        }

        /// <summary>Finds a whole-word keyword outside quotes and parentheses; -1 when absent.</summary>
        private static int FindTopLevelKeyword(string text, string keyword)
        {
            int depth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (TrySkipQuoted(text, ref i))
                {
                    continue;
                }

                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                }
                else if (depth == 0 &&
                         (i == 0 || !IsWordChar(text[i - 1])) &&
                         i + keyword.Length <= text.Length &&
                         text.AsSpan(i, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase) &&
                         (i + keyword.Length == text.Length || !IsWordChar(text[i + keyword.Length])))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Advances past a quoted literal or identifier starting at <paramref name="i"/>.</summary>
        private static bool TrySkipQuoted(string text, ref int i)
        {
            char open = text[i];
            char close = open switch { '\'' => '\'', '"' => '"', '`' => '`', '[' => ']', _ => '\0' };
            if (close == '\0')
            {
                return false;
            }

            int j = i + 1;
            while (j < text.Length)
            {
                if (text[j] == close)
                {
                    if (open == '\'' && j + 1 < text.Length && text[j + 1] == '\'')
                    {
                        j += 2;   // '' inside a string literal
                        continue;
                    }

                    break;
                }

                j++;
            }

            i = Math.Min(j, text.Length - 1);
            return true;
        }

        private static bool StartsWithKeyword(string text, string keyword) =>
            text.Length > keyword.Length &&
            text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) &&
            !IsWordChar(text[keyword.Length]);

        private static bool EndsWithKeyword(string text, string keyword) =>
            text.Length > keyword.Length &&
            text.EndsWith(keyword, StringComparison.OrdinalIgnoreCase) &&
            !IsWordChar(text[^(keyword.Length + 1)]);

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';
    }
}
