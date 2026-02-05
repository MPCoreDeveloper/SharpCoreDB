// Quick verification that the SQL parsing works correctly
using System;
using System.Text.RegularExpressions;

// Test the regex patterns
var createTableSql = @"CREATE TABLE bench_records (
            id INTEGER PRIMARY KEY,
            name TEXT,
            email TEXT,
            age INTEGER,
            salary DECIMAL,
            created DATETIME
        )";

var regex = new Regex(
    @"CREATE\s+TABLE\s+(\w+)\s*\((.*)\)", 
    RegexOptions.IgnoreCase | RegexOptions.Singleline);

var match = regex.Match(createTableSql);
if (match.Success)
{
    Console.WriteLine($"✅ Table name: {match.Groups[1].Value.Trim()}");
    Console.WriteLine($"✅ Column defs: {match.Groups[2].Value.Substring(0, 50)}...");
}
else
{
    Console.WriteLine("❌ Regex failed");
}

// Test INSERT parsing
var insertSql = "INSERT INTO bench_records (id, name, email, age, salary, created) VALUES (1, 'User1', 'user1@test.com', 25, 50000, '2025-01-01')";
var insertRegex = new Regex(
    @"INSERT\s+INTO\s+(\w+)\s*\((.*?)\)\s*VALUES\s*\((.*?)\)",
    RegexOptions.IgnoreCase | RegexOptions.Singleline);

var insertMatch = insertRegex.Match(insertSql);
if (insertMatch.Success)
{
    Console.WriteLine($"✅ INSERT - Table: {insertMatch.Groups[1].Value.Trim()}");
    Console.WriteLine($"✅ INSERT - Columns: {insertMatch.Groups[2].Value.Trim()}");
    Console.WriteLine($"✅ INSERT - Values: {insertMatch.Groups[3].Value.Trim()}");
}
else
{
    Console.WriteLine("❌ INSERT Regex failed");
}

// Test SELECT parsing
var selectSql = "SELECT * FROM bench_records WHERE age > 30";
var selectRegex = new Regex(
    @"SELECT\s+(.*?)\s+FROM\s+(\w+)\s*(?:WHERE\s+(.*))?",
    RegexOptions.IgnoreCase | RegexOptions.Singleline);

var selectMatch = selectRegex.Match(selectSql);
if (selectMatch.Success)
{
    Console.WriteLine($"✅ SELECT - Columns: {selectMatch.Groups[1].Value.Trim()}");
    Console.WriteLine($"✅ SELECT - Table: {selectMatch.Groups[2].Value.Trim()}");
    Console.WriteLine($"✅ SELECT - WHERE: {selectMatch.Groups[3].Value.Trim()}");
}
else
{
    Console.WriteLine("❌ SELECT Regex failed");
}
