using SharpCoreDB.GraphRAG.AIAssistant.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpCoreDB.GraphRAG.AIAssistant.Services;

/// <summary>
/// Service for interacting with AI models (DeepSeek, OpenAI, etc.).
/// Handles context injection, prompt building, and streaming responses.
/// </summary>
public sealed class AIService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AIService> _logger;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly AIProvider _provider;

    public AIService(
        HttpClient httpClient,
        ILogger<AIService> logger,
        string apiKey,
        AIProvider provider = AIProvider.DeepSeek,
        string? model = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = apiKey;
        _provider = provider;

        // Configure based on provider
        (_baseUrl, _model) = provider switch
        {
            AIProvider.DeepSeek => ("https://api.deepseek.com/v1", model ?? "deepseek-chat"),
            AIProvider.OpenAI => ("https://api.openai.com/v1", model ?? "gpt-4"),
            AIProvider.AzureOpenAI => throw new NotImplementedException("Azure OpenAI requires additional config"),
            _ => throw new ArgumentException($"Unknown provider: {provider}")
        };

        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    /// <summary>
    /// Generates an answer to the user's question using retrieved context from GraphRAG.
    /// </summary>
    /// <param name="question">User's question</param>
    /// <param name="context">Retrieved documents from GraphRAG</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>AI-generated answer with source citations</returns>
    public async Task<AIResponse> GenerateAnswerAsync(
        string question,
        List<GraphRAGResult> context,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        _logger.LogInformation("Generating AI answer using {Provider} ({Model}) with {Count} context documents",
            _provider, _model, context.Count);

        // Build prompt with context
        var prompt = BuildPrompt(question, context);

        // Call AI API
        var response = await CallAIAsync(prompt, ct);

        // Extract source citations
        var citations = ExtractCitations(context);

        return new AIResponse
        {
            Answer = response,
            Question = question,
            SourceDocuments = context,
            Citations = citations,
            Provider = _provider,
            Model = _model
        };
    }

    /// <summary>
    /// Streams AI response token-by-token for real-time display.
    /// </summary>
    public async IAsyncEnumerable<string> StreamAnswerAsync(
        string question,
        List<GraphRAGResult> context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var prompt = BuildPrompt(question, context);

        await foreach (var token in StreamAIAsync(prompt, ct))
        {
            yield return token;
        }
    }

    /// <summary>
    /// Builds a prompt with GraphRAG context and instructions.
    /// </summary>
    private static string BuildPrompt(string question, List<GraphRAGResult> context)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are a helpful technical documentation assistant.");
        sb.AppendLine("Answer the user's question using ONLY the provided context documents.");
        sb.AppendLine("If the context doesn't contain enough information, say so.");
        sb.AppendLine("Always cite sources using [1], [2], etc. format.");
        sb.AppendLine();
        sb.AppendLine("# Context Documents");
        sb.AppendLine();

        for (int i = 0; i < context.Count; i++)
        {
            var doc = context[i];
            sb.AppendLine($"## [{i + 1}] {doc.Title}");
            sb.AppendLine($"**Source:** {doc.SourceUrl}");
            sb.AppendLine($"**Relevance Score:** {doc.Score:F3}");

            if (doc.RelationshipPath is not null)
            {
                sb.AppendLine($"**Found via:** {doc.RetrievalMethod} " +
                             $"({doc.RelationshipPath.RelationType}, {doc.RelationshipPath.HopDistance} hop(s))");
            }
            else
            {
                sb.AppendLine($"**Found via:** {doc.RetrievalMethod}");
            }

            sb.AppendLine();
            sb.AppendLine(doc.Content);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        sb.AppendLine("# User Question");
        sb.AppendLine();
        sb.AppendLine(question);
        sb.AppendLine();
        sb.AppendLine("# Instructions");
        sb.AppendLine("- Provide a clear, structured answer");
        sb.AppendLine("- Cite sources using [number] format");
        sb.AppendLine("- Highlight prerequisites if mentioned in context");
        sb.AppendLine("- Suggest related topics if found via graph relationships");
        sb.AppendLine("- Be concise but complete");

        return sb.ToString();
    }

    /// <summary>
    /// Calls the AI API and returns the complete response.
    /// </summary>
    private async Task<string> CallAIAsync(string prompt, CancellationToken ct)
    {
        var request = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = "You are a helpful technical documentation assistant." },
                new { role = "user", content = prompt }
            },
            temperature = 0.7,
            max_tokens = 2000,
            stream = false
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}/chat/completions",
            request,
            ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(ct);

        return result?.Choices?.FirstOrDefault()?.Message?.Content 
            ?? throw new InvalidOperationException("No response from AI");
    }

    /// <summary>
    /// Streams AI response token-by-token.
    /// </summary>
    private async IAsyncEnumerable<string> StreamAIAsync(
        string prompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var request = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = "You are a helpful technical documentation assistant." },
                new { role = "user", content = prompt }
            },
            temperature = 0.7,
            max_tokens = 2000,
            stream = true
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(request)
        };

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);

            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                continue;

            var data = line["data: ".Length..];

            if (data == "[DONE]")
                break;

            StreamChunk? chunk = null;
            try
            {
                chunk = JsonSerializer.Deserialize<StreamChunk>(data);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse streaming chunk: {Data}", data);
                continue;
            }

            var content = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;

            if (!string.IsNullOrEmpty(content))
                yield return content;
        }
    }

    /// <summary>
    /// Extracts citation information from context documents.
    /// </summary>
    private static List<Citation> ExtractCitations(List<GraphRAGResult> context)
    {
        return context.Select((doc, index) => new Citation
        {
            Number = index + 1,
            Title = doc.Title,
            Url = doc.SourceUrl ?? "",
            Score = doc.Score,
            RetrievalMethod = doc.RetrievalMethod
        }).ToList();
    }

    #region Response Models

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public Message? Message { get; set; }
    }

    private sealed class Message
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private sealed class StreamChunk
    {
        [JsonPropertyName("choices")]
        public List<StreamChoice>? Choices { get; set; }
    }

    private sealed class StreamChoice
    {
        [JsonPropertyName("delta")]
        public Delta? Delta { get; set; }
    }

    private sealed class Delta
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    #endregion
}

/// <summary>
/// AI provider enumeration
/// </summary>
public enum AIProvider
{
    DeepSeek,
    OpenAI,
    AzureOpenAI
}

/// <summary>
/// Complete AI response with context and citations
/// </summary>
public sealed class AIResponse
{
    public required string Answer { get; init; }
    public required string Question { get; init; }
    public required List<GraphRAGResult> SourceDocuments { get; init; }
    public required List<Citation> Citations { get; init; }
    public required AIProvider Provider { get; init; }
    public required string Model { get; init; }
}

/// <summary>
/// Source citation information
/// </summary>
public sealed class Citation
{
    public required int Number { get; init; }
    public required string Title { get; init; }
    public required string Url { get; init; }
    public required double Score { get; init; }
    public required RetrievalMethod RetrievalMethod { get; init; }
}
