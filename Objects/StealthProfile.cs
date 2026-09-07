using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NetworkMonitor.Objects;

public sealed record BrowserProfile(string UserAgent, string Platform, string AcceptLanguage, string Plugins, int Width, int Height);

public static class StealthProfile
{
    private const string VersionsUrl = "https://googlechromelabs.github.io/chrome-for-testing/known-good-versions.json";
    private const string ChromePlugins = "[{ name: 'Chrome PDF Viewer', filename: 'internal-pdf-viewer', description: 'Portable Document Format' }, { name: 'Widevine Content Decryption Module', filename: 'widevinecdm', description: '' }]";
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);
    private static BrowserProfile[] _profiles = FallbackProfiles();
    private static bool _initialized;

    public static BrowserProfile GetRandom() => _profiles[Random.Shared.Next(_profiles.Length)];

    public static async Task RefreshAsync(string? cachePath = null, ILogger? logger = null)
    {
        if (_initialized) return;
        await RefreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            if (!string.IsNullOrWhiteSpace(cachePath)) TryLoadCache(cachePath, logger);
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var response = await client.GetAsync(VersionsUrl).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var document = await JsonSerializer.DeserializeAsync<ChromeVersionsDocument>(
                    stream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }).ConfigureAwait(false);
                var refreshed = BuildProfiles(document?.Versions?.Select(v => v.Version).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!) ?? Array.Empty<string>());
                if (refreshed.Length > 0)
                {
                    _profiles = refreshed;
                    if (!string.IsNullOrWhiteSpace(cachePath)) TrySaveCache(cachePath, refreshed, logger);
                    logger?.LogInformation("Loaded {ProfileCount} current stealth browser profiles.", refreshed.Length);
                }
            }
            catch (Exception ex) { logger?.LogWarning(ex, "Unable to refresh stealth browser profiles; using cached/default profiles."); }
            _initialized = true;
        }
        finally { RefreshGate.Release(); }
    }

    private static BrowserProfile[] BuildProfiles(IEnumerable<string> versions)
    {
        var majors = versions.Select(v => v.Split('.')[0]).Where(v => int.TryParse(v, out _)).Distinct().OrderByDescending(v => int.Parse(v)).Take(3).ToArray();
        return majors.SelectMany(major => new[]
        {
            new BrowserProfile($"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{major}.0.0.0 Safari/537.36", "Win32", "en-US,en;q=0.9", ChromePlugins, 1920, 1080),
            new BrowserProfile($"Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{major}.0.0.0 Safari/537.36", "MacIntel", "en-US,en;q=0.9", ChromePlugins, 1440, 900),
            new BrowserProfile($"Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{major}.0.0.0 Safari/537.36", "Linux x86_64", "en-US,en;q=0.9", ChromePlugins, 1920, 1080)
        }).ToArray();
    }

    private static BrowserProfile[] FallbackProfiles() => new[]
    {
        new BrowserProfile("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36", "Win32", "en-US,en;q=0.9", ChromePlugins, 1920, 1080),
        new BrowserProfile("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36", "MacIntel", "en-US,en;q=0.9", ChromePlugins, 1440, 900),
        new BrowserProfile("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36", "Linux x86_64", "en-US,en;q=0.9", ChromePlugins, 1920, 1080)
    };

    private static void TryLoadCache(string path, ILogger? logger) { try { var cached = JsonSerializer.Deserialize<BrowserProfile[]>(File.ReadAllText(path)); if (cached?.Length > 0) _profiles = cached; } catch (Exception ex) { logger?.LogDebug(ex, "No usable stealth profile cache at {Path}.", path); } }
    private static void TrySaveCache(string path, BrowserProfile[] profiles, ILogger? logger) { try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(profiles)); } catch (Exception ex) { logger?.LogDebug(ex, "Unable to save stealth profile cache at {Path}.", path); } }
    private sealed class ChromeVersionsDocument { public List<ChromeVersion>? Versions { get; set; } }
    private sealed class ChromeVersion { public string? Version { get; set; } }
}
