using System.Globalization;

Console.WriteLine("Testing locale validation with CreateCulture...");

string[] testLocales = ["en_US", "de_DE", "tr_TR", "xx_YY", "zz_ZZ", "invalid"];

foreach (var locale in testLocales)
{
    var normalized = locale.Replace('_', '-');
    Console.Write($"Testing '{locale}' (normalized: '{normalized}'): ");
    
    try
    {
        var culture = CultureInfo.GetCultureInfo(normalized);
        Console.WriteLine($"  - DisplayName: {culture.DisplayName}");
        Console.WriteLine($"  - TwoLetterISOLanguageName: {culture.TwoLetterISOLanguageName}");
        Console.WriteLine($"  - DisplayName == normalized: {culture.DisplayName == normalized}");
        Console.WriteLine($"  - DisplayName.StartsWith(\"Unknown\"): {culture.DisplayName.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase)}");
        
        // Test SharpCoreDB validation
        try
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"locale_test_{Guid.NewGuid()}");
            var db = new SharpCoreDB.Database(null!, dbPath, "test", false, null, null);
            db.ExecuteSQL($"CREATE TABLE test (col TEXT COLLATE LOCALE(\"{locale}\"))");
            Console.WriteLine($"  ✅ SharpCoreDB accepted locale");
            db.Dispose();
            if (Directory.Exists(dbPath))
                Directory.Delete(dbPath, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ SharpCoreDB rejected: {ex.GetType().Name} - {ex.Message}");
        }
    }
    catch (CultureNotFoundException ex)
    {
        Console.WriteLine($"❌ Invalid - {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error - {ex.GetType().Name}: {ex.Message}");
    }
    Console.WriteLine();
}
