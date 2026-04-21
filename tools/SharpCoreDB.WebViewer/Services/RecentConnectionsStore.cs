using System.Text.Json;
using Microsoft.Extensions.Options;
using SharpCoreDB.WebViewer.Models;

namespace SharpCoreDB.WebViewer.Services;

/// <summary>
/// Stores recent connection profiles in a local JSON settings file without sensitive secrets.
/// </summary>
public sealed class RecentConnectionsStore(IOptions<WebViewerOptions> options) : IRecentConnectionsStore
{
    private const string SettingsFileName = "settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly WebViewerOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settingsFilePath = GetSettingsFilePath();
            if (!File.Exists(settingsFilePath))
            {
                return [];
            }

            await using var fileStream = File.OpenRead(settingsFilePath);
            var payload = await JsonSerializer.DeserializeAsync<RecentConnectionsPayload>(fileStream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return payload?.RecentConnections ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(IReadOnlyCollection<ConnectionProfile> profiles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var clampedProfiles = profiles
                .OrderByDescending(static profile => profile.LastUsedUtc)
                .Take(Math.Max(1, _options.MaxRecentConnections))
                .ToArray();

            var payload = new RecentConnectionsPayload
            {
                RecentConnections = clampedProfiles
            };

            var settingsDirectoryPath = GetSettingsDirectoryPath();
            Directory.CreateDirectory(settingsDirectoryPath);

            var settingsFilePath = Path.Combine(settingsDirectoryPath, SettingsFileName);
            await using var fileStream = File.Create(settingsFilePath);
            await JsonSerializer.SerializeAsync(fileStream, payload, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string GetSettingsDirectoryPath()
    {
        var localApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationDataPath, "SharpCoreDB.WebViewer");
    }

    private static string GetSettingsFilePath() => Path.Combine(GetSettingsDirectoryPath(), SettingsFileName);

    private sealed class RecentConnectionsPayload
    {
        public IReadOnlyList<ConnectionProfile> RecentConnections { get; init; } = [];
    }
}
