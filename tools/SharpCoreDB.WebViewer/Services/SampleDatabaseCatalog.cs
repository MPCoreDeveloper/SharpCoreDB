using System.Globalization;
using Microsoft.Extensions.Options;
using SharpCoreDB.Data.Provider;
using SharpCoreDB.WebViewer.Models;

namespace SharpCoreDB.WebViewer.Services;

/// <summary>
/// Manages built-in local databases: the default "scdb" database and
/// Contoso / AdventureWorks-style sample databases.
/// </summary>
public sealed class SampleDatabaseCatalog(IOptions<WebViewerOptions> options) : ISampleDatabaseCatalog
{
    public const string ContosoSampleName = "contoso";
    public const string AdventureWorksSampleName = "adventureworks";

    /// <summary>
    /// Marker file written into a database directory once seeding has completed.
    /// The engine's own files (meta.dat, *.dat) flush asynchronously, so they cannot
    /// be used as a reliable "already seeded" marker at startup.
    /// </summary>
    private const string SeededMarkerFileName = ".seeded";

    private readonly WebViewerOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string GetDefaultDatabasePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.DefaultDatabasePath))
        {
            return Path.GetFullPath(_options.DefaultDatabasePath);
        }

        return Path.Combine(GetDataRootDirectory(), _options.DefaultDatabaseName);
    }

    public string GetSampleDatabasePath(string sampleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sampleName);
        return Path.Combine(GetDataRootDirectory(), sampleName);
    }

    public string GetDataRootDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_options.SampleDatabasesDirectory))
        {
            return Path.GetFullPath(_options.SampleDatabasesDirectory);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SharpCoreDB.WebViewer",
            "Data");
    }

    public IReadOnlyList<SampleDatabaseInfo> ListSamples()
    {
        return
        [
            new SampleDatabaseInfo
            {
                Name = ContosoSampleName,
                DisplayName = "Contoso",
                Description = "Retail analytics sample: customers, products, orders, sales and inventory.",
                StorageMode = DatabaseStorageMode.Directory
            },
            new SampleDatabaseInfo
            {
                Name = AdventureWorksSampleName,
                DisplayName = "AdventureWorks",
                Description = "Cycles manufacturer sample: products, customers, sales orders and territories.",
                StorageMode = DatabaseStorageMode.Directory
            }
        ];
    }

    public async Task EnsureDefaultDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var defaultPath = GetDefaultDatabasePath();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(defaultPath);
            if (File.Exists(Path.Combine(defaultPath, SeededMarkerFileName)))
            {
                return;
            }

            await EnsureDatabaseAsync(defaultPath, _options.DefaultDatabasePassword, BuildScript(ScdbWelcomeScript), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EnsureSampleAsync(string sampleName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sampleName);

        var normalized = sampleName.Trim().ToLowerInvariant();
        if (!ListSamples().Any(sample => string.Equals(sample.Name, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Unknown sample database '{sampleName}'.");
        }

        var path = GetSampleDatabasePath(normalized);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(path);
            if (File.Exists(Path.Combine(path, SeededMarkerFileName)))
            {
                return;
            }

            var script = normalized == ContosoSampleName ? ContosoScript : AdventureWorksScript;
            await EnsureDatabaseAsync(path, _options.DefaultDatabasePassword, BuildScript(script), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task EnsureDatabaseAsync(string path, string password, string[] statements, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(path);

        var connectionString = new SharpCoreDBConnectionStringBuilder
        {
            Path = path,
            Password = password,
            ReadOnly = false,
            Cache = "Private"
        }.ConnectionString;

        try
        {
            await using var connection = new SharpCoreDBConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Execute through the ADO.NET provider path — the same path the viewer
            // query service uses successfully for both DDL and DML statements.
            foreach (var statement in statements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var command = new SharpCoreDBCommand(statement, connection);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // Mark the database as fully seeded so later launches skip the seed script.
            // Without this marker the INSERT statements would run again and fail on
            // primary-key violations, and the retry would delete the whole database.
            File.WriteAllText(Path.Combine(path, SeededMarkerFileName), DateTimeOffset.UtcNow.ToString("O"));
        }
        catch
        {
            // Remove a partially-created database so the next launch can retry from a clean state.
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }

            throw;
        }
    }

// ── Seed scripts (pipe-delimited token stream) ───────────────────────────
private const string ScdbWelcomeScript =
    "CREATE TABLE IF NOT EXISTS welcome (id INTEGER PRIMARY KEY AUTOINCREMENT, message TEXT NOT NULL, created_at TEXT NOT NULL DEFAULT (datetime('now')));|" +
    "welcome|id,message,created_at|1|Welcome to SharpCoreDB!|2026-01-01 00:00:00|2|This is your default scdb database.|2026-01-01 00:00:00|3|Use the Tools menu to create Contoso or AdventureWorks sample databases.|2026-01-01 00:00:00";

private const string ContosoScript =
    "CREATE TABLE IF NOT EXISTS customers (customer_id INTEGER PRIMARY KEY AUTOINCREMENT, first_name TEXT NOT NULL, last_name TEXT NOT NULL, email TEXT UNIQUE NOT NULL, city TEXT NOT NULL, country TEXT NOT NULL, created_at TEXT NOT NULL DEFAULT (datetime('now')));|" +
    "CREATE TABLE IF NOT EXISTS products (product_id INTEGER PRIMARY KEY AUTOINCREMENT, product_name TEXT NOT NULL, category TEXT NOT NULL, unit_price REAL NOT NULL, units_in_stock INTEGER NOT NULL DEFAULT 0);|" +
    "CREATE TABLE IF NOT EXISTS orders (order_id INTEGER PRIMARY KEY AUTOINCREMENT, customer_id INTEGER NOT NULL REFERENCES customers(customer_id), order_date TEXT NOT NULL DEFAULT (datetime('now')), status TEXT NOT NULL DEFAULT 'Pending');|" +
    "CREATE TABLE IF NOT EXISTS order_items (order_item_id INTEGER PRIMARY KEY AUTOINCREMENT, order_id INTEGER NOT NULL REFERENCES orders(order_id), product_id INTEGER NOT NULL REFERENCES products(product_id), quantity INTEGER NOT NULL, unit_price REAL NOT NULL);|" +
    "CREATE TABLE IF NOT EXISTS inventory (inventory_id INTEGER PRIMARY KEY AUTOINCREMENT, product_id INTEGER NOT NULL REFERENCES products(product_id), warehouse TEXT NOT NULL, quantity INTEGER NOT NULL DEFAULT 0);|" +
    "customers|customer_id,first_name,last_name,email,city,country,created_at|1|Alice|Johnson|alice.johnson@contoso.com|Amsterdam|Netherlands|2026-01-01 00:00:00|2|Bob|Smith|bob.smith@contoso.com|London|United Kingdom|2026-01-01 00:00:00|3|Carol|Garcia|carol.garcia@contoso.com|Madrid|Spain|2026-01-01 00:00:00|4|David|Muller|david.muller@contoso.com|Berlin|Germany|2026-01-01 00:00:00|5|Eva|Chen|eva.chen@contoso.com|Paris|France|2026-01-01 00:00:00|" +
    "products|product_id,product_name,category,unit_price,units_in_stock|1|Contoso Laptop Pro 14|Electronics|1299|42|2|Contoso Wireless Mouse|Electronics|29.99|250|3|Contoso Mechanical Keyboard|Electronics|89.99|120|4|Contoso Office Chair|Furniture|349|18|5|Contoso Standing Desk|Furniture|649|9|6|Contoso Espresso Machine|Appliances|499|25|" +
    "orders|order_id,customer_id,order_date,status|1|1|2026-01-15 10:30:00|Shipped|2|2|2026-01-17 14:00:00|Delivered|3|3|2026-02-01 09:15:00|Processing|4|4|2026-02-10 16:45:00|Pending|5|5|2026-02-14 11:20:00|Shipped|" +
    "order_items|order_item_id,order_id,product_id,quantity,unit_price|1|1|1|1|1299|2|1|2|2|29.99|3|2|3|1|89.99|4|3|4|2|349|5|3|5|1|649|6|4|6|1|499|7|5|1|2|1299|" +
    "inventory|inventory_id,product_id,warehouse,quantity|1|1|Main|42|2|2|Main|250|3|3|Secondary|120|4|4|Main|18|5|5|Main|9|6|6|Secondary|25";

private const string AdventureWorksScript =
    "CREATE TABLE IF NOT EXISTS product_categories (category_id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL);|" +
    "CREATE TABLE IF NOT EXISTS products (product_id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, product_number TEXT UNIQUE NOT NULL, color TEXT NULL, list_price REAL NOT NULL, category_id INTEGER NULL REFERENCES product_categories(category_id));|" +
    "CREATE TABLE IF NOT EXISTS customers (customer_id INTEGER PRIMARY KEY AUTOINCREMENT, first_name TEXT NOT NULL, last_name TEXT NOT NULL, email_address TEXT NULL, phone TEXT NULL);|" +
    "CREATE TABLE IF NOT EXISTS sales_territories (territory_id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, country_region TEXT NOT NULL, sales_ytd REAL NOT NULL DEFAULT 0);|" +
    "CREATE TABLE IF NOT EXISTS sales_orders (sales_order_id INTEGER PRIMARY KEY AUTOINCREMENT, order_date TEXT NOT NULL, customer_id INTEGER NOT NULL REFERENCES customers(customer_id), territory_id INTEGER NULL REFERENCES sales_territories(territory_id), status TEXT NOT NULL DEFAULT 'Shipped');|" +
    "CREATE TABLE IF NOT EXISTS sales_order_details (sales_order_detail_id INTEGER PRIMARY KEY AUTOINCREMENT, sales_order_id INTEGER NOT NULL REFERENCES sales_orders(sales_order_id), product_id INTEGER NOT NULL REFERENCES products(product_id), order_qty INTEGER NOT NULL, unit_price REAL NOT NULL, line_total REAL NOT NULL);|" +
    "product_categories|category_id,name|1|Bikes|2|Components|3|Clothing|4|Accessories|" +
    "products|product_id,name,product_number,color,list_price,category_id|1|Mountain-100 Black, 38|BK-M82S-38|Black|3374.99|1|2|Mountain-100 Silver, 38|BK-M82S-38-S|Silver|3399.99|1|3|Road-150 Red, 44|BK-R93R-44|Red|3578.27|1|4|LL Bottom Bracket|BB-7421|Black|53.99|2|5|ML Crankset|CR-7833|Black|256.49|2|6|Racing Socks, L|SO-R809-L|White|8.99|3|7|Water Bottle - 30 oz|WB-H098|NULL|4.99|4|" +
    "customers|customer_id,first_name,last_name,email_address,phone|1|John|Doe|john.doe@example.com|555-0101|2|Jane|Smith|jane.smith@example.com|555-0102|3|Michael|Brown|michael.brown@example.com|555-0103|4|Sarah|Davis|sarah.davis@example.com|555-0104|5|James|Wilson|james.wilson@example.com|555-0105|" +
    "sales_territories|territory_id,name,country_region,sales_ytd|1|Northwest|United States|5015687.65|2|Northeast|United States|3124567.12|3|Europe|United Kingdom|1987654|4|Pacific|Australia|1456789.55|" +
    "sales_orders|sales_order_id,order_date,customer_id,territory_id,status|1|2026-01-05 09:00:00|1|1|Shipped|2|2026-01-22 11:30:00|2|3|Shipped|3|2026-02-03 14:15:00|3|2|Shipped|4|2026-02-11 10:45:00|4|4|In Progress|5|2026-02-18 16:00:00|5|1|Shipped|" +
    "sales_order_details|sales_order_detail_id,sales_order_id,product_id,order_qty,unit_price,line_total|1|1|1|1|3374.99|3374.99|2|1|7|2|4.99|9.98|3|2|3|1|3578.27|3578.27|4|3|5|3|256.49|769.47|5|4|6|10|8.99|89.9|6|5|2|1|3399.99|3399.99";

    private static string[] BuildScript(string compactScript)
    {
        const char quote = '\'';
        var tokens = compactScript.Split('|');
        var result = new List<string>();
        var index = 0;
        var knownTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "customers", "products", "orders", "order_items", "inventory",
            "product_categories", "sales_territories", "sales_orders", "sales_order_details", "welcome"
        };

        while (index < tokens.Length)
        {
            var token = tokens[index];
            if (string.IsNullOrWhiteSpace(token))
            {
                index++;
                continue;
            }

            if (token.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(token);
                index++;
                continue;
            }

            // Table data: tokens[index] = table, tokens[index+1] = columns, then row values.
            var table = token;
            if (index + 1 >= tokens.Length)
            {
                throw new InvalidDataException($"Seed script table '{table}' is missing its column list.");
            }

            var columns = tokens[index + 1].Split(',');
            index += 2;

            while (index < tokens.Length
                && !tokens[index].StartsWith("CREATE", StringComparison.OrdinalIgnoreCase)
                && !knownTables.Contains(tokens[index]))
            {
                var values = new string[columns.Length];
                for (var i = 0; i < columns.Length; i++)
                {
                    if (index >= tokens.Length
                        || tokens[index].StartsWith("CREATE", StringComparison.OrdinalIgnoreCase)
                        || knownTables.Contains(tokens[index]))
                    {
                        throw new InvalidDataException(
                            $"Seed script table '{table}' has a row with fewer than {columns.Length} values. Check the compact script alignment.");
                    }

                    values[i] = FormatSeedValue(tokens[index], quote);
                    index++;
                }

                result.Add("INSERT INTO " + table + " (" + string.Join(", ", columns) + ") VALUES (" + string.Join(", ", values) + ");");
            }
        }

        return [.. result];
    }

    private static string FormatSeedValue(string value, char quote)
    {
        if (string.Equals(value, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return "NULL";
        }

        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
        {
            return value;
        }

        return string.Concat(quote.ToString(), value.Replace(quote.ToString(), new string(quote, 2), StringComparison.Ordinal), quote.ToString());
    }
}