using SharpCoreDB;
using SharpCoreDB.GraphRAG.AIAssistant.Data;
using SharpCoreDB.GraphRAG.AIAssistant.Services;
using SharpCoreDB.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace SharpCoreDB.GraphRAG.AIAssistant;

internal sealed class Program
{
    private static ILogger<Program>? _logger;
    private static DocumentationService? _docService;
    private static AIService? _aiService;

    static async Task<int> Main(string[] args)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        // Setup dependency injection
        var services = new ServiceCollection();
        ConfigureServices(services, configuration);
        var serviceProvider = services.BuildServiceProvider();

        _logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        _docService = serviceProvider.GetRequiredService<DocumentationService>();

        // Initialize AI service (if API key configured)
        var apiKey = configuration["AI:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var providerStr = configuration["AI:Provider"] ?? "DeepSeek";
            var provider = Enum.Parse<AIProvider>(providerStr, ignoreCase: true);
            var model = configuration["AI:Model"];

            _aiService = new AIService(
                serviceProvider.GetRequiredService<HttpClient>(),
                serviceProvider.GetRequiredService<ILogger<AIService>>(),
                apiKey,
                provider,
                model);
        }

        PrintBanner();

        try
        {
            // Initialize database
            var db = serviceProvider.GetRequiredService<IDatabase>();
            await _docService.InitializeDatabaseAsync();
            await SampleDocumentation.LoadAsync(db, _logger);

            Console.WriteLine();
            PrintSuccess($"✅ Database initialized with {SampleDocumentation.GetArticleCount()} articles");
            PrintSuccess($"✅ Graph contains {SampleDocumentation.GetRelationshipCount()} relationships");
            PrintSuccess("✅ Ready to answer questions!");
            Console.WriteLine();

            // Interactive query loop
            await RunInteractiveQueryLoopAsync();

            return 0;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Application error");
            PrintError($"❌ Error: {ex.Message}");
            return 1;
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // SharpCoreDB core services
        services.AddSharpCoreDB();

        // Database instance
        services.AddSingleton<IDatabase>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<Database>>();
            var dbPath = configuration["Database:Path"] ?? "./documentation.scdb";
            var password = configuration["Database:Password"] ?? "GraphRAG_Demo_2025!";

            logger.LogInformation("Opening database: {Path}", dbPath);
            return new Database(sp, dbPath, password);
        });

        // Services
        services.AddSingleton<DocumentationService>();
        services.AddHttpClient();
    }

    private static async Task RunInteractiveQueryLoopAsync()
    {
        while (true)
        {
            Console.WriteLine();
            PrintPrompt("? Your question (or 'exit' to quit): ");
            var question = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(question))
                continue;

            if (question.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                question.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                PrintInfo("👋 Goodbye!");
                break;
            }

            if (question.Equals("demo", StringComparison.OrdinalIgnoreCase))
            {
                await RunDemoQueryAsync();
                continue;
            }

            await ProcessQuestionAsync(question);
        }
    }

    private static async Task RunDemoQueryAsync()
    {
        var demoQuestion = "How do I implement JWT authentication?";
        PrintInfo($"📚 Running demo query: \"{demoQuestion}\"\n");
        await ProcessQuestionAsync(demoQuestion);
    }

    private static async Task ProcessQuestionAsync(string question)
    {
        if (_docService == null)
        {
            PrintError("Documentation service not initialized");
            return;
        }

        var sw = Stopwatch.StartNew();

        PrintHeader("SEARCHING WITH GRAPHRAG");

        try
        {
            // Execute GraphRAG search
            var graphRagResults = await _docService.SearchAsync(
                question: question,
                topK: 25,
                maxResults: 8,
                minScore: 0.0,
                maxDepth: 2);

            // Execute traditional RAG for comparison
            var vectorOnlyResults = await _docService.VectorOnlySearchAsync(
                question: question,
                maxResults: 5,
                minScore: 0.0);

            sw.Stop();

            // Display vector search results
            PrintSubHeader("SEMANTIC SEARCH (Vector Similarity)");
            Console.WriteLine($"Top {Math.Min(5, vectorOnlyResults.Count)} similar documents:\n");

            for (int i = 0; i < Math.Min(5, vectorOnlyResults.Count); i++)
            {
                var doc = vectorOnlyResults[i];
                PrintResult($"{i + 1}. {doc.Title}", doc.Score, isVectorMatch: true);
            }
            Console.WriteLine();

            // Display graph traversal results
            PrintSubHeader("GRAPH TRAVERSAL (Relationship Discovery)");
            var graphOnlyDocs = graphRagResults
                .Where(r => r.RetrievalMethod == Models.RetrievalMethod.GraphTraversal)
                .ToList();

            if (graphOnlyDocs.Count > 0)
            {
                Console.WriteLine("Related documents via relationships:\n");

                foreach (var doc in graphOnlyDocs.Take(5))
                {
                    var path = doc.RelationshipPath;
                    var relationship = path != null 
                        ? $"[{path.RelationType}, {path.HopDistance} hop(s)]" 
                        : "[unknown]";
                    PrintResult($"• {doc.Title}", doc.Score, relationship: relationship);
                }
            }
            else
            {
                PrintInfo("No additional documents found via graph traversal.");
            }
            Console.WriteLine();

            // Display hybrid ranked results
            PrintSubHeader("HYBRID RANKING (Combined Results)");
            Console.WriteLine($"Final context ({graphRagResults.Count} documents):\n");

            for (int i = 0; i < Math.Min(8, graphRagResults.Count); i++)
            {
                var doc = graphRagResults[i];
                var icon = doc.RetrievalMethod switch
                {
                    Models.RetrievalMethod.VectorSearch => "⭐",
                    Models.RetrievalMethod.GraphTraversal => "🔗",
                    Models.RetrievalMethod.Hybrid => "✨",
                    _ => "•"
                };
                PrintResult($"{i + 1}. {doc.Title}", doc.Score, icon: icon);
            }
            Console.WriteLine();

            // Generate AI answer if service is available
            if (_aiService != null && graphRagResults.Count > 0)
            {
                PrintSubHeader("AI ANSWER (DeepSeek/OpenAI)");
                PrintInfo("🤖 Generating answer...\n");

                try
                {
                    var topContext = graphRagResults.Take(5).ToList();
                    var aiResponse = await _aiService.GenerateAnswerAsync(question, topContext);

                    PrintAnswer(aiResponse.Answer);

                    Console.WriteLine();
                    PrintInfo($"Sources used: {string.Join(", ", aiResponse.Citations.Select(c => $"[{c.Number}] {c.Title}"))}");
                }
                catch (Exception ex)
                {
                    PrintWarning($"⚠️  AI service error: {ex.Message}");
                    PrintInfo("Tip: Check your API key in appsettings.json");
                }
            }
            else if (_aiService == null)
            {
                PrintWarning("⚠️  AI service not configured (missing API key)");
                PrintInfo("Add your API key to appsettings.json to enable AI answers");
            }

            // Performance metrics
            Console.WriteLine();
            PrintSubHeader("PERFORMANCE METRICS");
            Console.WriteLine($"⚡ Total time: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"📊 Documents retrieved: {graphRagResults.Count}");
            Console.WriteLine($"🎯 Average relevance score: {graphRagResults.Average(r => r.Score):F3}");

            // Comparison summary
            Console.WriteLine();
            PrintComparison(vectorOnlyResults, graphRagResults);
        }
        catch (Exception ex)
        {
            PrintError($"❌ Search error: {ex.Message}");
            _logger?.LogError(ex, "Error processing question");
        }
    }

    private static void PrintComparison(List<Models.GraphRAGResult> vectorOnly, List<Models.GraphRAGResult> graphRag)
    {
        PrintSubHeader("🆚 COMPARISON: Traditional RAG vs GraphRAG");

        Console.WriteLine("Without GraphRAG (vector search only):");
        var missedDocs = graphRag
            .Where(g => g.RetrievalMethod != Models.RetrievalMethod.VectorSearch)
            .Take(3)
            .ToList();

        if (missedDocs.Count > 0)
        {
            foreach (var doc in missedDocs)
            {
                PrintError($"  ❌ Missed: {doc.Title} ({doc.RelationshipPath?.RelationType})");
            }
        }
        else
        {
            PrintSuccess("  ✅ All relevant docs found via vector search");
        }

        Console.WriteLine();
        Console.WriteLine("With GraphRAG:");
        PrintSuccess($"  ✅ Found {graphRag.Count} documents total");
        PrintSuccess($"  ✅ Discovered {missedDocs.Count} via graph relationships");
        PrintSuccess("  ✅ Complete context with prerequisites and related topics");
    }

    #region Console Helpers

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
╔══════════════════════════════════════════════════════════════╗
║  SharpCoreDB GraphRAG AI Documentation Assistant             ║
║  Powered by: SharpCoreDB.Graph.Advanced + VectorSearch       ║
╚══════════════════════════════════════════════════════════════╝
");
        Console.ResetColor();
    }

    private static void PrintHeader(string text)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"┌─ {text} " + new string('─', Math.Max(0, 60 - text.Length - 3)) + "┐");
        Console.ResetColor();
    }

    private static void PrintSubHeader(string text)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"├─ {text} " + new string('─', Math.Max(0, 60 - text.Length - 3)) + "┤");
        Console.ResetColor();
    }

    private static void PrintResult(string text, double score, string? relationship = null, string icon = "•", bool isVectorMatch = false)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"{icon} {text}");

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($" (score: {score:F3})");

        if (relationship != null)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write($" {relationship}");
        }

        Console.WriteLine();
        Console.ResetColor();
    }

    private static void PrintAnswer(string answer)
    {
        Console.ForegroundColor = ConsoleColor.White;

        // Wrap text at 80 characters
        var words = answer.Split(' ');
        var currentLine = "";

        foreach (var word in words)
        {
            if (currentLine.Length + word.Length + 1 > 80)
            {
                Console.WriteLine(currentLine);
                currentLine = word;
            }
            else
            {
                currentLine += (currentLine.Length > 0 ? " " : "") + word;
            }
        }

        if (currentLine.Length > 0)
            Console.WriteLine(currentLine);

        Console.ResetColor();
    }

    private static void PrintSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static void PrintInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static void PrintWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static void PrintPrompt(string message)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.Write(message);
        Console.ResetColor();
    }

    #endregion
}
