using System;
using System.Collections.Generic;
namespace NetworkMonitor.Objects;
public class UserAgents
{
    private static readonly List<string> Agents = new List<string>
    {
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/114.0.5735.198 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.0.0 Safari/537.36 Edg/118.0.2088.69",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/119.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7; rv:115.0) Gecko/20100101 Firefox/115.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.6 Safari/605.1.1",
        "Mozilla/5.0 (iPhone; CPU iPhone OS 15_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/15.5 Mobile/15E148 Safari/604.1",
        "Mozilla/5.0 (Linux; Android 11; SM-G991B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/114.0.5735.198 Mobile Safari/537.36"
    };

    // Method to get all user agents
    public static List<string> GetUserAgents() => new List<string>(Agents);

    // Method to get a random user agent
    public static string GetRandomUserAgent()
    {
        var random = new Random();
        int index = random.Next(Agents.Count);
        return Agents[index];
    }

    // Helper method to extract platform
    public static string GetPlatformFromUserAgent(string userAgent)
    {
        if (userAgent.Contains("Win64") || userAgent.Contains("Windows NT"))
            return "Win32";
        if (userAgent.Contains("Macintosh") || userAgent.Contains("Mac OS X"))
            return "MacIntel";
        if (userAgent.Contains("Linux") && !userAgent.Contains("Android"))
            return "Linux x86_64";
        if (userAgent.Contains("Android"))
            return "Linux armv7l";
        if (userAgent.Contains("iPhone"))
            return "iPhone";

        // Fallback if platform cannot be determined
        return "Unknown";
    }
     public static string GetPluginsForUserAgent(string userAgent)
        {
            // Customize plugins based on the userAgent
            if (userAgent.Contains("Chrome"))
            {
                return @"
                    [
                        { name: 'Chrome PDF Viewer', filename: 'internal-pdf-viewer', description: 'Portable Document Format' },
                        { name: 'Widevine Content Decryption Module', filename: 'widevinecdm', description: '' }
                    ]
                ";
            }
            else if (userAgent.Contains("Firefox"))
            {
                return @"
                    [
                        { name: 'PDF.js', filename: 'pdfjs-extension', description: 'PDF Viewer' },
                        { name: 'Widevine Content Decryption Module', filename: 'widevinecdm', description: '' }
                    ]
                ";
            }
            else if (userAgent.Contains("Safari"))
            {
                return @"
                    [
                        { name: 'QuickTime Plug-in', filename: 'QuickTime.plugin', description: 'QuickTime Plug-in' }
                    ]
                ";
            }
            else
            {
                return "[]"; // Empty plugins list for unsupported user agents
            }
        }
}
