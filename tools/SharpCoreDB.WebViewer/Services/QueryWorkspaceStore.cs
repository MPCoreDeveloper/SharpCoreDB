using System.Text.Json;
using Microsoft.Extensions.Options;
using SharpCoreDB.WebViewer.Models;

namespace SharpCoreDB.WebViewer.Services;

/// <summary>
/// Stores saved queries and execution history in a local JSON file.
/// </summary>
public sealed class QueryWorkspaceStore(IOptions<WebViewerOptions> options) : IQueryWorkspaceStore
{
    private const string WorkspaceFileName = "query-workspace.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly WebViewerOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async Task<QueryWorkspaceState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var filePath = GetWorkspaceFilePath();
            if (!File.Exists(filePath))
            {
                return new QueryWorkspaceState();
            }

            await using var stream = File.OpenRead(filePath);
            var state = await JsonSerializer.DeserializeAsync<QueryWorkspaceState>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return Normalize(state ?? new QueryWorkspaceState());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(QueryWorkspaceState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalized = Normalize(state);
            Directory.CreateDirectory(GetSettingsDirectoryPath());

            await using var stream = File.Create(GetWorkspaceFilePath());
            await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(state, JsonOptions);
    }

    /// <inheritdoc />
    public async Task ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        QueryWorkspaceState? imported;
        try
        {
            imported = JsonSerializer.Deserialize<QueryWorkspaceState>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid query workspace JSON: {ex.Message}", ex);
        }

        if (imported is null)
        {
            throw new InvalidOperationException("The query workspace JSON payload is empty.");
        }

        await SaveAsync(imported, cancellationToken).ConfigureAwait(false);
    }

    private QueryWorkspaceState Normalize(QueryWorkspaceState state)
    {
        var saved = state.SavedQueries
            .Where(static item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Sql))
            .GroupBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.OrderByDescending(item => item.LastUsedUtc).First())
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, _options.MaxSavedQueries))
            .ToArray();

        var history = state.History
            .Where(static item => !string.IsNullOrWhiteSpace(item.SqlPreview))
            .OrderByDescending(static item => item.ExecutedAtUtc)
            .Take(Math.Max(1, _options.MaxQueryHistoryItems))
            .ToArray();

        return new QueryWorkspaceState
        {
            SavedQueries = saved,
            History = history
        };
    }

    private static string GetSettingsDirectoryPath()
    {
        var localApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationDataPath, "SharpCoreDB.WebViewer");
    }

    private static string GetWorkspaceFilePath() => Path.Combine(GetSettingsDirectoryPath(), WorkspaceFileName);
}
