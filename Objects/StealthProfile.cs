namespace NetworkMonitor.Objects;

public sealed record BrowserProfile(
    string UserAgent,
    string Platform,
    string AcceptLanguage,
    string Plugins,
    int Width,
    int Height);

/// <summary>Curated browser identities used by stealth browser sessions.</summary>
public static class StealthProfile
{
    private static readonly BrowserProfile[] Profiles =
    {
        new("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36", "Win32", "en-US,en;q=0.9", ChromePlugins, 1920, 1080),
        new("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36", "Win32", "en-US,en;q=0.9", ChromePlugins, 1536, 864),
        new("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36", "MacIntel", "en-US,en;q=0.9", ChromePlugins, 1440, 900),
        new("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36", "Linux x86_64", "en-US,en;q=0.9", ChromePlugins, 1920, 1080),
        new("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.2535.67", "Win32", "en-US,en;q=0.9", ChromePlugins, 1366, 768),
        new("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36", "MacIntel", "en-US,en;q=0.9", ChromePlugins, 1440, 900)
    };

    private const string ChromePlugins = "[{ name: 'Chrome PDF Viewer', filename: 'internal-pdf-viewer', description: 'Portable Document Format' }, { name: 'Widevine Content Decryption Module', filename: 'widevinecdm', description: '' }]";

    public static BrowserProfile GetRandom() => Profiles[Random.Shared.Next(Profiles.Length)];
}
