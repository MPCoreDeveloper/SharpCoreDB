// <copyright file="AIServiceIntegration.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Net.Http.Json;
using System.Text.Json;

namespace SharpCoreDB.Analytics.AI;

/// <summary>
/// Integration with external AI services for enhanced analytics.
/// Supports OpenAI, Azure OpenAI, and other AI providers.
/// C# 14: Primary constructors, async patterns, collection expressions.
/// </summary>
public sealed class AIServiceIntegration : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AIServiceOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIServiceIntegration"/> class.
    /// </summary>
    /// <param name="options">The AI service options.</param>
    public AIServiceIntegration(AIServiceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.ApiKey}");
        if (!string.IsNullOrEmpty(_options.OrganizationId))
        {
            _httpClient.DefaultRequestHeaders.Add("OpenAI-Organization", _options.OrganizationId);
        }
    }

    /// <summary>
    /// Analyzes data patterns using AI.
    /// </summary>
    /// <param name="data">The data to analyze.</param>
    /// <param name="analysisType">The type of analysis to perform.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The AI analysis result.</returns>
    public async Task<AIAnalysisResult> AnalyzeDataAsync(
        IReadOnlyList<Dictionary<string, object>> data,
        string analysisType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisType);

        var prompt = BuildAnalysisPrompt(data, analysisType);
        var response = await CallAIAsync(prompt, cancellationToken);

        return new AIAnalysisResult
        {
            AnalysisType = analysisType,
            Insights = response,
            Confidence = CalculateConfidence(response),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Generates predictions using AI models.
    /// </summary>
    /// <param name="historicalData">The historical data for training.</param>
    /// <param name="predictionTarget">The target variable to predict.</param>
    /// <param name="futurePeriods">Number of future periods to predict.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The prediction results.</returns>
    public async Task<AIPredictionResult> GeneratePredictionsAsync(
        IReadOnlyList<Dictionary<string, object>> historicalData,
        string predictionTarget,
        int futurePeriods,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(historicalData);
        ArgumentException.ThrowIfNullOrWhiteSpace(predictionTarget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(futurePeriods);

        var prompt = BuildPredictionPrompt(historicalData, predictionTarget, futurePeriods);
        var response = await CallAIAsync(prompt, cancellationToken);

        return new AIPredictionResult
        {
            TargetVariable = predictionTarget,
            Predictions = ParsePredictions(response),
            Confidence = CalculateConfidence(response),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Generates SQL queries from natural language.
    /// </summary>
    /// <param name="naturalLanguageQuery">The natural language query.</param>
    /// <param name="schemaInfo">Information about the database schema.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated SQL query.</returns>
    public async Task<AISQLGenerationResult> GenerateSQLAsync(
        string naturalLanguageQuery,
        string schemaInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(naturalLanguageQuery);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaInfo);

        var prompt = BuildSQLGenerationPrompt(naturalLanguageQuery, schemaInfo);
        var response = await CallAIAsync(prompt, cancellationToken);

        return new AISQLGenerationResult
        {
            NaturalLanguageQuery = naturalLanguageQuery,
            GeneratedSQL = ExtractSQLFromResponse(response),
            Explanation = ExtractExplanationFromResponse(response),
            Confidence = CalculateConfidence(response),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Calls the AI service with a prompt.
    /// </summary>
    /// <param name="prompt">The prompt to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The AI response.</returns>
    private async Task<string> CallAIAsync(string prompt, CancellationToken cancellationToken)
    {
        var request = new
        {
            model = _options.Model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            max_tokens = _options.MaxTokens,
            temperature = _options.Temperature
        };

        var response = await _httpClient.PostAsJsonAsync(_options.Endpoint, request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return result.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    /// <summary>
    /// Builds an analysis prompt.
    /// </summary>
    /// <param name="data">The data to analyze.</param>
    /// <param name="analysisType">The analysis type.</param>
    /// <returns>The analysis prompt.</returns>
    private static string BuildAnalysisPrompt(IReadOnlyList<Dictionary<string, object>> data, string analysisType)
    {
        var dataSample = string.Join("\n", data.Take(10).Select(row =>
            string.Join(", ", row.Select(kvp => $"{kvp.Key}: {kvp.Value}"))));

        return $@"
Analyze the following data for {analysisType}:

Data Sample:
{dataSample}

Please provide insights about:
1. Key patterns and trends
2. Anomalies or outliers
3. Correlations between variables
4. Recommendations for further analysis

Be specific and data-driven in your analysis.
";
    }

    /// <summary>
    /// Builds a prediction prompt.
    /// </summary>
    /// <param name="historicalData">The historical data.</param>
    /// <param name="predictionTarget">The prediction target.</param>
    /// <param name="futurePeriods">The number of future periods.</param>
    /// <returns>The prediction prompt.</returns>
    private static string BuildPredictionPrompt(
        IReadOnlyList<Dictionary<string, object>> historicalData,
        string predictionTarget,
        int futurePeriods)
    {
        var dataSample = string.Join("\n", historicalData.Take(20).Select(row =>
            string.Join(", ", row.Select(kvp => $"{kvp.Key}: {kvp.Value}"))));

        return $@"
Based on the following historical data, predict the next {futurePeriods} values for {predictionTarget}:

Historical Data:
{dataSample}

Please provide:
1. Predicted values for the next {futurePeriods} periods
2. Confidence intervals if possible
3. Factors that might influence the predictions
4. Any assumptions made

Format predictions as a numbered list.
";
    }

    /// <summary>
    /// Builds an SQL generation prompt.
    /// </summary>
    /// <param name="naturalLanguageQuery">The natural language query.</param>
    /// <param name="schemaInfo">The schema information.</param>
    /// <returns>The SQL generation prompt.</returns>
    private static string BuildSQLGenerationPrompt(string naturalLanguageQuery, string schemaInfo)
    {
        return $@"
Convert the following natural language query to SQL:

Query: {naturalLanguageQuery}

Database Schema:
{schemaInfo}

Please provide:
1. The SQL query
2. A brief explanation of the query
3. Any assumptions made

Ensure the SQL is valid and follows best practices.
";
    }

    /// <summary>
    /// Calculates confidence from AI response.
    /// </summary>
    /// <param name="response">The AI response.</param>
    /// <returns>The confidence score (0.0 to 1.0).</returns>
    private static double CalculateConfidence(string response)
    {
        // Simple heuristic: longer, more detailed responses are more confident
        var wordCount = response.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Min(wordCount / 100.0, 1.0);
    }

    /// <summary>
    /// Parses predictions from AI response.
    /// </summary>
    /// <param name="response">The AI response.</param>
    /// <returns>The parsed predictions.</returns>
    private static IReadOnlyList<double> ParsePredictions(string response)
    {
        // Simple parsing - in practice, use more sophisticated NLP
        var lines = response.Split('\n');
        var predictions = new List<double>();

        foreach (var line in lines)
        {
            if (double.TryParse(line.Trim(), out var value))
            {
                predictions.Add(value);
            }
        }

        return predictions;
    }

    /// <summary>
    /// Extracts SQL from AI response.
    /// </summary>
    /// <param name="response">The AI response.</param>
    /// <returns>The extracted SQL.</returns>
    private static string ExtractSQLFromResponse(string response)
    {
        // Look for SQL code blocks
        var startIndex = response.IndexOf("```sql", StringComparison.OrdinalIgnoreCase);
        if (startIndex == -1)
        {
            startIndex = response.IndexOf("```");
        }

        if (startIndex != -1)
        {
            var endIndex = response.IndexOf("```", startIndex + 3);
            if (endIndex != -1)
            {
                return response.Substring(startIndex + 3, endIndex - startIndex - 3).Trim();
            }
        }

        // Fallback: return the whole response
        return response;
    }

    /// <summary>
    /// Extracts explanation from AI response.
    /// </summary>
    /// <param name="response">The AI response.</param>
    /// <returns>The extracted explanation.</returns>
    private static string ExtractExplanationFromResponse(string response)
    {
        // Remove SQL code blocks and return the rest
        var sqlStart = response.IndexOf("```");
        if (sqlStart != -1)
        {
            var sqlEnd = response.IndexOf("```", sqlStart + 3);
            if (sqlEnd != -1)
            {
                return response.Remove(sqlStart, sqlEnd - sqlStart + 3).Trim();
            }
        }

        return response;
    }

    /// <summary>
    /// Disposes the AI service integration.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        await Task.CompletedTask;
    }
}

/// <summary>
/// Options for AI service integration.
/// </summary>
public class AIServiceOptions
{
    /// <summary>Gets or sets the API endpoint.</summary>
    public required string Endpoint { get; init; }

    /// <summary>Gets or sets the API key.</summary>
    public required string ApiKey { get; init; }

    /// <summary>Gets or sets the organization ID (for OpenAI).</summary>
    public string? OrganizationId { get; init; }

    /// <summary>Gets or sets the model name.</summary>
    public string Model { get; init; } = "gpt-4";

    /// <summary>Gets or sets the maximum tokens.</summary>
    public int MaxTokens { get; init; } = 1000;

    /// <summary>Gets or sets the temperature.</summary>
    public double Temperature { get; init; } = 0.7;
}

/// <summary>
/// Result of AI analysis.
/// </summary>
public class AIAnalysisResult
{
    /// <summary>Gets the analysis type.</summary>
    public required string AnalysisType { get; init; }

    /// <summary>Gets the AI insights.</summary>
    public required string Insights { get; init; }

    /// <summary>Gets the confidence score.</summary>
    public required double Confidence { get; init; }

    /// <summary>Gets the generation timestamp.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }
}

/// <summary>
/// Result of AI prediction.
/// </summary>
public class AIPredictionResult
{
    /// <summary>Gets the target variable.</summary>
    public required string TargetVariable { get; init; }

    /// <summary>Gets the predicted values.</summary>
    public required IReadOnlyList<double> Predictions { get; init; }

    /// <summary>Gets the confidence score.</summary>
    public required double Confidence { get; init; }

    /// <summary>Gets the generation timestamp.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }
}

/// <summary>
/// Result of AI SQL generation.
/// </summary>
public class AISQLGenerationResult
{
    /// <summary>Gets the natural language query.</summary>
    public required string NaturalLanguageQuery { get; init; }

    /// <summary>Gets the generated SQL.</summary>
    public required string GeneratedSQL { get; init; }

    /// <summary>Gets the explanation.</summary>
    public required string Explanation { get; init; }

    /// <summary>Gets the confidence score.</summary>
    public required double Confidence { get; init; }

    /// <summary>Gets the generation timestamp.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }
}
