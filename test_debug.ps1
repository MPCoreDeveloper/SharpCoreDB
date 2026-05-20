# Quick debug test for query execution
$code = @'
using SharpCoreDB;
using System;
using System.Linq;

var db = new Database();
db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, email TEXT)");
db.ExecuteSQL("INSERT INTO users VALUES (1, 'test@example.com')");

Console.WriteLine("Testing SELECT with @ in string literal:");
var result = db.ExecuteQuery("SELECT * FROM users WHERE email = 'test@example.com'");
Console.WriteLine($"Result count: {result.Count}");
if (result.Count > 0)
{
    Console.WriteLine($"Email: {result[0]["email"]}");
}
else
{
    Console.WriteLine("ERROR: No results returned!");
}

Console.WriteLine("\nTesting UNIXEPOCH function:");
var result2 = db.ExecuteQuery("SELECT UNIXEPOCH('2000-01-01T00:00:00') AS ts");
Console.WriteLine($"Result count: {result2.Count}");
if (result2.Count > 0)
{
    Console.WriteLine($"Timestamp: {result2[0]["ts"]}");
}
else
{
    Console.WriteLine("ERROR: No results returned!");
}
'@

dotnet-script eval $code
