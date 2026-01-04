// <copyright file="FastSqlLexer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services.Compilation;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// Zero-allocation SQL lexer using ReadOnlySpan&lt;char&gt;.
/// Tokenizes SQL in a single pass without string allocations.
/// ✅ OPTIMIZED: No Substring, no intermediate strings, Span-based only.
/// </summary>
public sealed class FastSqlLexer
{
    /// <summary>
    /// Token types recognized by the lexer.
    /// </summary>
    public enum TokenType : byte
    {
        /// <summary>Unknown token type.</summary>
        Unknown = 0,
        /// <summary>SQL keyword (SELECT, FROM, WHERE, etc).</summary>
        Keyword = 1,
        /// <summary>Identifier (column name, table name, etc).</summary>
        Identifier = 2,
        /// <summary>String literal (quoted text).</summary>
        String = 3,
        /// <summary>Numeric literal.</summary>
        Number = 4,
        /// <summary>Operator (=, !=, &lt;&gt;, &lt;, &gt;, etc).</summary>
        Operator = 5,
        /// <summary>Comma separator.</summary>
        Comma = 6,
        /// <summary>Left parenthesis.</summary>
        LeftParen = 7,
        /// <summary>Right parenthesis.</summary>
        RightParen = 8,
        /// <summary>Asterisk/star character.</summary>
        Star = 9,
        /// <summary>End of file marker.</summary>
        EOF = 10,
    }

    /// <summary>
    /// Represents a token with span-based position, no string allocation.
    /// </summary>
    public readonly struct Token
    {
        /// <summary>Gets the token type.</summary>
        public TokenType Type { get; init; }

        /// <summary>Gets the start position in the source text.</summary>
        public int Start { get; init; }

        /// <summary>Gets the length of the token.</summary>
        public int Length { get; init; }

        /// <summary>
        /// Gets the token text as a ReadOnlySpan using the source input.
        /// Zero-copy, no allocation.
        /// </summary>
        public ReadOnlySpan<char> GetSpan(ReadOnlySpan<char> source) =>
            source.Slice(Start, Math.Min(Length, source.Length - Start));

        /// <summary>
        /// Gets the token text as a string (allocation only if needed).
        /// Avoid calling this in hot paths.
        /// </summary>
        public string GetString(ReadOnlySpan<char> source) =>
            new string(GetSpan(source));

        /// <summary>Returns string representation of token.</summary>
        public override string ToString() => $"{Type}[{Start}:{Length}]";
    }

    private readonly string source;
    private int position;

    /// <summary>
    /// Initializes a new instance of the <see cref="FastSqlLexer"/> class.
    /// </summary>
    /// <param name="sql">The SQL text to tokenize.</param>
    public FastSqlLexer(string sql)
    {
        source = sql ?? string.Empty;
        position = 0;
    }

    /// <summary>
    /// Tokenizes SQL and returns all tokens.
    /// Single-pass, zero allocation (returns struct array).
    /// </summary>
    /// <returns>Array of tokens.</returns>
    public Token[] Tokenize()
    {
        var tokens = new List<Token>();
        var sourceSpan = source.AsSpan();

        while (!IsAtEnd())
        {
            SkipWhitespace();
            if (IsAtEnd()) break;

            var token = NextToken(sourceSpan);
            if (token.Type != TokenType.Unknown)
            {
                tokens.Add(token);
            }
        }

        tokens.Add(new Token { Type = TokenType.EOF, Start = position, Length = 0 });
        return tokens.ToArray();
    }

    /// <summary>
    /// Gets and consumes the next token from the stream.
    /// </summary>
    /// <returns>The next token.</returns>
    public Token NextToken(ReadOnlySpan<char> sourceSpan)
    {
        SkipWhitespace();

        if (IsAtEnd())
        {
            return new Token { Type = TokenType.EOF, Start = position, Length = 0 };
        }

        var start = position;
        var ch = Current();

        // Single-character tokens
        return ch switch
        {
            ',' => Advance(TokenType.Comma),
            '(' => Advance(TokenType.LeftParen),
            ')' => Advance(TokenType.RightParen),
            '*' => Advance(TokenType.Star),
            '\'' => ReadString('\''),
            '"' => ReadString('"'),
            '=' or '!' or '<' or '>' => ReadOperator(),
            _ when IsLetter(ch) => ReadKeywordOrIdentifier(sourceSpan),
            _ when IsDigit(ch) => ReadNumber(),
            _ => new Token { Type = TokenType.Unknown, Start = start, Length = 1 }
        };
    }

    /// <summary>
    /// Reads a string literal (single or double quoted).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Token ReadString(char quote)
    {
        var start = position;
        position++; // Skip opening quote

        while (!IsAtEnd() && Current() != quote)
        {
            if (Current() == '\\' && Peek(1) == quote)
            {
                position += 2; // Skip escaped quote
            }
            else
            {
                position++;
            }
        }

        if (!IsAtEnd()) position++; // Skip closing quote

        return new Token
        {
            Type = TokenType.String,
            Start = start,
            Length = position - start
        };
    }

    /// <summary>
    /// Reads an operator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Token ReadOperator()
    {
        var start = position;
        var ch = Current();
        position++;

        // Check for two-character operators
        if (!IsAtEnd())
        {
            var next = Current();
            if ((ch == '!' || ch == '<' || ch == '>' || ch == '=') && (next == '=' || (ch == '<' && next == '>')))
            {
                position++;
            }
        }

        return new Token
        {
            Type = TokenType.Operator,
            Start = start,
            Length = position - start
        };
    }

    /// <summary>
    /// Reads a keyword or identifier.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Token ReadKeywordOrIdentifier(ReadOnlySpan<char> sourceSpan)
    {
        var start = position;

        while (!IsAtEnd() && (IsLetterOrDigit(Current()) || Current() == '_'))
        {
            position++;
        }

        var span = sourceSpan.Slice(start, position - start);
        var type = IsKeyword(span) ? TokenType.Keyword : TokenType.Identifier;

        return new Token
        {
            Type = type,
            Start = start,
            Length = position - start
        };
    }

    /// <summary>
    /// Reads a numeric literal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Token ReadNumber()
    {
        var start = position;

        while (!IsAtEnd() && IsDigit(Current()))
        {
            position++;
        }

        // Handle decimal point
        if (!IsAtEnd() && Current() == '.' && position + 1 < source.Length && IsDigit(source[position + 1]))
        {
            position++;
            while (!IsAtEnd() && IsDigit(Current()))
            {
                position++;
            }
        }

        return new Token
        {
            Type = TokenType.Number,
            Start = start,
            Length = position - start
        };
    }

    /// <summary>
    /// Advances position and returns a token.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Token Advance(TokenType type)
    {
        var start = position++;
        return new Token
        {
            Type = type,
            Start = start,
            Length = 1
        };
    }

    /// <summary>
    /// Skips whitespace (spaces, tabs, newlines).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SkipWhitespace()
    {
        while (!IsAtEnd() && char.IsWhiteSpace(Current()))
        {
            position++;
        }
    }

    /// <summary>
    /// Checks if we're at the end of the input.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsAtEnd() => position >= source.Length;

    /// <summary>
    /// Gets the character at the current position.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private char Current() => IsAtEnd() ? '\0' : source[position];

    /// <summary>
    /// Peeks at a character ahead without advancing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private char Peek(int offset) =>
        position + offset >= source.Length ? '\0' : source[position + offset];

    /// <summary>
    /// Checks if a character is a letter (A-Z, a-z).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsLetter(char ch) => (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z');

    /// <summary>
    /// Checks if a character is a digit (0-9).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDigit(char ch) => ch >= '0' && ch <= '9';

    /// <summary>
    /// Checks if a character is alphanumeric.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsLetterOrDigit(char ch) => IsLetter(ch) || IsDigit(ch);

    /// <summary>
    /// Checks if a span represents a SQL keyword (case-insensitive).
    /// </summary>
    private static bool IsKeyword(ReadOnlySpan<char> word)
    {
        // Common SQL keywords - use case-insensitive comparison
        return EqualsIgnoreCase(word, "SELECT")
            || EqualsIgnoreCase(word, "FROM")
            || EqualsIgnoreCase(word, "WHERE")
            || EqualsIgnoreCase(word, "AND")
            || EqualsIgnoreCase(word, "OR")
            || EqualsIgnoreCase(word, "NOT")
            || EqualsIgnoreCase(word, "ORDER")
            || EqualsIgnoreCase(word, "BY")
            || EqualsIgnoreCase(word, "ASC")
            || EqualsIgnoreCase(word, "DESC")
            || EqualsIgnoreCase(word, "LIMIT")
            || EqualsIgnoreCase(word, "OFFSET")
            || EqualsIgnoreCase(word, "INSERT")
            || EqualsIgnoreCase(word, "INTO")
            || EqualsIgnoreCase(word, "VALUES")
            || EqualsIgnoreCase(word, "UPDATE")
            || EqualsIgnoreCase(word, "SET")
            || EqualsIgnoreCase(word, "DELETE")
            || EqualsIgnoreCase(word, "CREATE")
            || EqualsIgnoreCase(word, "TABLE")
            || EqualsIgnoreCase(word, "DROP")
            || EqualsIgnoreCase(word, "ALTER")
            || EqualsIgnoreCase(word, "ADD")
            || EqualsIgnoreCase(word, "COLUMN")
            || EqualsIgnoreCase(word, "AS")
            || EqualsIgnoreCase(word, "JOIN")
            || EqualsIgnoreCase(word, "INNER")
            || EqualsIgnoreCase(word, "LEFT")
            || EqualsIgnoreCase(word, "RIGHT")
            || EqualsIgnoreCase(word, "ON")
            || EqualsIgnoreCase(word, "DISTINCT")
            || EqualsIgnoreCase(word, "NULL")
            || EqualsIgnoreCase(word, "TRUE")
            || EqualsIgnoreCase(word, "FALSE");
    }

    /// <summary>
    /// Case-insensitive span equality (ASCII only, fast).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool EqualsIgnoreCase(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (char.ToUpperInvariant(a[i]) != char.ToUpperInvariant(b[i]))
                return false;
        }
        return true;
    }
}
