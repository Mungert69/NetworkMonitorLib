using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Connection
{
    /// <summary>
    /// Encapsulates Google search logic for reuse by multiple command processors.
    /// </summary>
    public class SearchWebHelper
    {
        private readonly ILogger _logger;
        private readonly NetConnectConfig _netConfig;
        private readonly int _microTimeout;
        private readonly int _macroTimeout;

        // Replace with a trusted layout fingerprint after testing
        private const string _knownTrustedFingerprint = "D3D8098599C4BF01B6C9EE4E0DA11688E9D32BFA6667D1C8D80C05822EC70C5E";

        public SearchWebHelper(ILogger logger, NetConnectConfig netConfig, int microTimeout = 10000, int macroTimeout = 120000)
        {
            _logger = logger;
            _netConfig = netConfig;
            _microTimeout = microTimeout;
            _macroTimeout = macroTimeout;
        }

        public async Task<TResultObj<List<string>>> FetchGoogleSearchUrlsAsync(
            IPage page,
            string searchTerm,
            CancellationToken cancellationToken)
        {
            var result = new TResultObj<List<string>>();
            _logger.LogInformation($"→ FetchGoogleSearchUrlsAsync: query “{searchTerm}”");

            await page.GoToAsync(
                $"https://www.google.com/search?hl=en&q={Uri.EscapeDataString(searchTerm)}",
                new NavigationOptions { Timeout = _microTimeout, WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } }
            );

            await TryDismissConsentForm(page);

            await Task.Delay(RandomInterval(3000, 7000));

            string html = await page.GetContentAsync();
            LogIfFingerprintChanged(html);

            if (html.Contains("unusual traffic", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("please verify you are not a robot", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("⚠ Google presented CAPTCHA or unusual traffic message—using API fallback");
                return await FetchUrlsFromGoogleApi(searchTerm);
            }

            try
            {
                await page.WaitForFunctionAsync(
                    @"() => document.querySelectorAll('a[href^=""/url?q=""]').length > 0",
                    new WaitForFunctionOptions { Timeout = _microTimeout }
                );
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("No search results detected—falling back to API");
                return await FetchUrlsFromGoogleApi(searchTerm);
            }

            await Task.Delay(RandomInterval(1000, 3000));

            string[] rawUrls;
            try
            {
                rawUrls = await page.EvaluateFunctionAsync<string[]>(@"() => 
                    Array.from(document.querySelectorAll('a[href^=""/url?q=""]'))
                         .map(a => {
                             try {
                                 return new URL(a.href, window.location.origin)
                                           .searchParams.get('q');
                             } catch {
                                 return null;
                             }
                         })
                         .filter(url => url &&
                                        (url.startsWith('http://') || url.startsWith('https://'))
                                        && !url.includes('webcache'))");
            }
            catch (Exception e)
            {
                _logger.LogWarning($"Error extracting URLs via script: {e.Message}");
                return await FetchUrlsFromGoogleApi(searchTerm);
            }

            var urls = (rawUrls ?? Array.Empty<string>())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct()
                .ToList();

            if (!urls.Any())
            {
                _logger.LogWarning("Extracted 0 URLs—falling back to API");
                return await FetchUrlsFromGoogleApi(searchTerm);
            }

            result.Success = true;
            result.Data = urls;
            return result;
        }

        private async Task TryDismissConsentForm(IPage page)
        {
            try
            {
                _logger.LogInformation("→ Checking for consent dialog…");

                var selectors = new[]
                {
                    "button[name='set_eom'][value='true']",
                    "input[type='submit'][value='Reject all']",
                    "form[action*='/save'] input[name='set_eom'][value='true']"
                };

                foreach (var sel in selectors)
                {
                    var element = await page.QuerySelectorAsync(sel);
                    if (element != null)
                    {
                        _logger.LogInformation($"✅ Consent dialog: found element using selector “{sel}” → clicking");
                        await element.ClickAsync();

                        await page.WaitForNavigationAsync(new NavigationOptions
                        {
                            Timeout = _microTimeout,
                            WaitUntil = new[] { WaitUntilNavigation.Networkidle2 }
                        });

                        _logger.LogInformation("✅ Navigated past consent dialog");
                        return;
                    }
                }

                _logger.LogInformation("No consent dialog appeared");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Consent dialog handler error: {ex.Message}");
                await DumpPageHtml(page, "consent-error");
            }
        }

        private void LogIfFingerprintChanged(string html)
        {
            string normalized = RegexStripNumbers.Replace(html, "").Trim();
            string fingerprint = ComputeSha256Hash(normalized);

            if (_knownTrustedFingerprint != fingerprint)
            {
                _logger.LogWarning("⚠ Google SERP fingerprint mismatch. Layout may have changed.");
                _logger.LogInformation($"Computed fingerprint: {fingerprint}");
            }
        }
        public async Task StealthAsync(IPage page)
        {
            await page.EvaluateFunctionOnNewDocumentAsync(@"
        () => {
            // Overwrite the `navigator.webdriver` property
            Object.defineProperty(navigator, 'webdriver', { get: () => undefined });

            // Mock plugins
            Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3] });

            // Mock languages
            Object.defineProperty(navigator, 'languages', { get: () => ['en-US', 'en'] });

            // Fix screen resolution
            Object.defineProperty(screen, 'width', { get: () => 1920 });
            Object.defineProperty(screen, 'height', { get: () => 1080 });

            // Chrome runtime check spoof
            window.chrome = {
                runtime: {},
                loadTimes: () => { return {}; },
                csi: () => { return {}; }
            };

            // Navigator.permissions spoof
            const originalQuery = window.navigator.permissions.query;
            window.navigator.permissions.query = (parameters) => (
                parameters.name === 'notifications' ?
                Promise.resolve({ state: Notification.permission }) :
                originalQuery(parameters)
            );
        }
    ");
        }

        private async Task DumpPageHtml(IPage page, string tag)
        {
            try
            {
                string ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string file = $"debug_{tag}_{ts}.html";
                string content = await page.GetContentAsync();
                await File.WriteAllTextAsync(file, content);
                _logger.LogInformation($"✅ Page HTML dump written to {file}");
            }
            catch (Exception dumpEx)
            {
                _logger.LogError($"Failed to dump HTML: {dumpEx.Message}");
            }
        }

        public async Task<TResultObj<List<string>>> FetchUrlsFromGoogleApi(string searchTerm)
        {
            var result = new TResultObj<List<string>>();

            if (_netConfig.GoogleSearchApiCxID == "Not Set" || _netConfig.GoogleSearchApiKey == "Not set")
            {
                result.Message = "Missing Google API key or Cx ID";
                result.Success = false;
                return result;
            }

            string url = $"https://www.googleapis.com/customsearch/v1?q={Uri.EscapeDataString(searchTerm)}&key={_netConfig.GoogleSearchApiKey}&cx={_netConfig.GoogleSearchApiCxID}";
            _logger.LogInformation($" Using url for Google Search Api : {url}");

            using var httpClient = new HttpClient();
            try
            {
                var response = await httpClient.GetStringAsync(url);
                var searchResponse = JsonSerializer.Deserialize<GoogleSearchResponse>(response);

                if (searchResponse?.Items != null && searchResponse.Items.Count > 0)
                {
                    result.Data = searchResponse.Items.Select(item => item.Link).ToList();
                    result.Success = true;
                    return result;
                }

                result.Message = "Failed to fetch any urls";
                result.Success = false;
            }
            catch (Exception ex)
            {
                result.Message = $"Error fetching results from Google API: {ex.Message}";
                result.Success = false;
                _logger.LogError(result.Message);
            }

            return result;
        }

        private static int RandomInterval(int minMs, int maxMs) =>
            new Random().Next(minMs, maxMs);

        private static string ComputeSha256Hash(string raw)
        {
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(bytes);
        }

        private static readonly Regex RegexStripNumbers =
            new Regex(@"[\-\d]+", RegexOptions.Compiled);
    }

    public class GoogleSearchResponse
    {
        [JsonPropertyName("items")]
        public List<SearchResultItem> Items { get; set; }
    }

    public class SearchResultItem
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("link")]
        public string Link { get; set; }

        [JsonPropertyName("snippet")]
        public string Snippet { get; set; }
    }
}
