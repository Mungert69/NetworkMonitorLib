using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using System.Linq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Connection;
using NetworkMonitor.Utils;
using System.Xml.Linq;
using System.IO;
using System.Threading;
using PuppeteerSharp;
using System.Text.Json;
namespace NetworkMonitor.Connection;
public class CrawlHelper
{
    // Simulate real user interactions
    public static async Task<string> SimulateUserInteraction(IPage page, ILogger logger, int timeout)
    {
        string currentPageUrl = "(page URL unknown)";
        var output = new StringBuilder();

        try
        {
            // Get the current URL of the page
            currentPageUrl = page.Url;

            // Calculate the number of scroll steps (randomized between 5 and 10)
            int totalScrollSteps = new Random().Next(5, 11);
            logger.LogInformation($"Simulating interaction: scrolling page in {totalScrollSteps} random steps.");

            // Get the total height of the page
            double pageHeight = await page.EvaluateFunctionAsync<double>("() => document.body.scrollHeight");

            // Calculate the maximum time allowed per scroll step (in milliseconds)
            int maxTimePerStep = timeout / totalScrollSteps;

            // Track the cumulative scroll position
            double currentScrollPosition = 0;

            // Scroll in random increments
            for (int step = 1; step <= totalScrollSteps; step++)
            {
                // Determine a random scroll increment (10–30% of the remaining scrollable height)
                double remainingHeight = pageHeight - currentScrollPosition;
                if (remainingHeight <= 0)
                {
                    logger.LogInformation($"Reached the bottom of the page on {currentPageUrl}.");
                    break;
                }

                double scrollIncrement = remainingHeight * (0.1 + new Random().NextDouble() * 0.2); // 10% to 30%
                currentScrollPosition = Math.Min(currentScrollPosition + scrollIncrement, pageHeight);

                // Scroll to the target position
                await page.EvaluateFunctionAsync(@"function (position) { window.scrollTo(0, position); }", currentScrollPosition);

                output.AppendLine($"Scrolled to {currentScrollPosition:F2} pixels ");
                logger.LogInformation($"Scrolled to {currentScrollPosition:F2} pixels on {currentPageUrl}. Step {step}/{totalScrollSteps}.");

                // Calculate a random delay for this step, ensuring it fits within the remaining timeout
                int minDelay = Math.Max(1000, maxTimePerStep / 2); // Minimum delay of 1 second
                int maxDelay = Math.Min(10000, maxTimePerStep);    // Maximum delay of 10 seconds or maxTimePerStep
                int delay = new Random().Next(minDelay, maxDelay);

                logger.LogInformation($"Waiting for {delay} ms before next scroll step.");
                await Task.Delay(delay); // Wait for the calculated delay
            }

            // Ensure we scroll to the bottom of the page at the end
            await page.EvaluateFunctionAsync("function () {window.scrollTo(0, document.body.scrollHeight);}");
            logger.LogInformation($"Completed scrolling simulation on {currentPageUrl}.");

            return $"Simulated interaction completed on {currentPageUrl}. {output.ToString()}";
        }
        catch (Exception ex)
        {
            logger.LogError($"Error simulating user interaction: {ex.Message}");
            return $"Failed to simulate user interaction on page {currentPageUrl}. Error: {ex.Message}";
        }
    }

    public static async Task RandomDelay(int min, int max)
    {
        var random = new Random();
        int delay = random.Next(min, max);
        await Task.Delay(delay);
    }

    public static async Task WaitNetIdle(IPage page, int timeout, ILogger logger)
    {
        try
        {
            await page.WaitForNetworkIdleAsync(new WaitForNetworkIdleOptions { Timeout = timeout });
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Timeout occurred while waiting for network idle on {page.Url}: {ex.Message}");
            // Optionally, retry or continue with other tasks
        }
    }



    public static async Task<TResultObj<string>> ExtractContent(IPage page, ILogger logger, int timeout, string? responseContentType = null)
    {
        logger.LogInformation("Waiting for page content to load...");
        try
        {
            await page.WaitForSelectorAsync("body, pre, code", new WaitForSelectorOptions { Timeout = timeout });
        }
        catch (Exception ex)
        {
            logger.LogWarning($"No body/pre/code selector before timeout for {page.Url}: {ex.Message}");
        }

        var structuredPayload = await TryExtractStructuredPayloadFromPageAsync(page);
        var likelyJson = IsLikelyJsonContentType(responseContentType);
        var likelyXml = IsLikelyXmlContentType(responseContentType);

        // Prefer JSON first, then XML, then HTML fallback.
        if ((likelyJson || LooksLikeJsonPayload(structuredPayload))
            && TryPrettyPrintJson(structuredPayload, out var prettyJson))
        {
            logger.LogInformation("Detected JSON/API response and returned formatted JSON content.");
            return new TResultObj<string>
            {
                Success = true,
                Data = prettyJson,
                Message = "JSON content extracted successfully."
            };
        }

        if ((likelyXml || LooksLikeXmlPayload(structuredPayload))
            && TryPrettyPrintXml(structuredPayload, out var prettyXml))
        {
            logger.LogInformation("Detected XML/API response and returned formatted XML content.");
            return new TResultObj<string>
            {
                Success = true,
                Data = prettyXml,
                Message = "XML content extracted successfully."
            };
        }

        logger.LogInformation("Extracting page text and links...");
        var extracted = await page.EvaluateFunctionAsync<string>(@"() => {
    const normalize = (s) => (s || '')
        .replace(/\u00A0/g, ' ')
        .replace(/\r\n/g, '\n')
        .replace(/\r/g, '\n');

    const root = document.querySelector('article, main, [role=""main""]') || document.body || document.documentElement;
    const mainText = normalize(root ? (root.innerText || root.textContent || '') : '');

    const codeText = Array.from(document.querySelectorAll('pre, code'))
        .map(el => normalize(el.textContent || '').trim())
        .filter(Boolean)
        .slice(0, 30)
        .join('\n\n');

    const links = Array.from(document.querySelectorAll('a[href]'))
        .map(a => {
            const href = (a.href || '').trim();
            if (!href) return '';
            const label = normalize(a.textContent || '').replace(/\s+/g, ' ').trim();
            return label ? `[${label}](${href})` : href;
        })
        .filter(Boolean);

    const uniqueLinks = [...new Set(links)].slice(0, 200);
    const parts = [];

    if (mainText.trim().length > 0) {
        parts.push(mainText);
    }

    if (codeText.length > 0) {
        parts.push(codeText);
    }

    if (uniqueLinks.length > 0) {
        parts.push('Links:\n' + uniqueLinks.join('\n'));
    }

    return parts.join('\n\n');
}");

        var content = NormalizeExtractedContent(extracted);
        if (string.IsNullOrWhiteSpace(content))
        {
            var fallback = await page.EvaluateFunctionAsync<string>(
                "() => (document.documentElement?.textContent || document.body?.textContent || '').trim()");
            content = NormalizeExtractedContent(fallback);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            logger.LogWarning("Page content is empty after extraction and fallback.");
            return new TResultObj<string>
            {
                Success = false,
                Message = "No page content found."
            };
        }

        logger.LogInformation("Page content extracted.");
        return new TResultObj<string>
        {
            Success = true,
            Data = content,
            Message = "Content extracted successfully."
        };
    }

    public static async Task<TResultObj<List<string>>> ExtractLinks(IPage page, string baseUrl, ILogger logger)
    {
        try
        {
            logger.LogInformation($"Extracting links from {baseUrl}");

            // Assume that navigation has already occurred
            await page.WaitForSelectorAsync("body");

            // Extract links
            var links = await page.EvaluateFunctionAsync<string[]>(@"() => {
            const anchors = Array.from(document.querySelectorAll('a[href]'));
            return anchors.map(anchor => anchor.href);
        }");

            // Filter out external links
            var internalLinks = links
                .Where(link => IsInternalLink(baseUrl, link))
                .Where(link => !link.Contains("#"))
                .Distinct()
                .ToList();

            logger.LogInformation($"Found {internalLinks.Count} internal links on {baseUrl}");
            return new TResultObj<List<string>>
            {
                Success = true,
                Data = internalLinks,
                Message = "Links extracted successfully."
            };
        }
        catch (Exception ex)
        {
            logger.LogError($"Error extracting links from {baseUrl}: {ex.Message}");
            return new TResultObj<List<string>>
            {
                Success = false,
                Message = $"Error extracting links: {ex.Message}",
                Data = new List<string>() // Return an empty list in case of failure
            };
        }
    }




    public static bool IsInternalLink(string baseUrl, string link)
    {
        try
        {
            var baseUri = new Uri(baseUrl);
            var linkUri = new Uri(link, UriKind.RelativeOrAbsolute);

            if (!linkUri.IsAbsoluteUri)
            {
                linkUri = new Uri(baseUri, linkUri);
            }

            return baseUri.Host == linkUri.Host;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> TryClickElementAsync(IElementHandle element, ILogger logger, int retries = 2, int delayMs = 1000, int operationTimeoutMs = 1000)
    {
        for (int attempt = 1; attempt <= retries; attempt++)
        {
            try
            {
                // Create a task for checking if the element is in the viewport
                var isVisibleTask = element.IsIntersectingViewportAsync();

                // Create a timeout task
                var timeoutTask = Task.Delay(operationTimeoutMs);

                // Race between the visibility check and the timeout
                var completedTask = await Task.WhenAny(isVisibleTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    logger.LogWarning($"Attempt {attempt} to check element visibility timed out after {operationTimeoutMs} ms.");
                }
                else
                {
                    // Visibility check succeeded
                    bool isVisible = await isVisibleTask;
                    if (isVisible)
                    {
                        // Create a task for the click operation
                        var clickTask = element.ClickAsync();

                        // Reset the timeout task
                        timeoutTask = Task.Delay(operationTimeoutMs);

                        // Race between the click task and the timeout task
                        completedTask = await Task.WhenAny(clickTask, timeoutTask);

                        if (completedTask == timeoutTask)
                        {
                            logger.LogWarning($"Attempt {attempt} to click element timed out after {operationTimeoutMs} ms.");
                        }
                        else
                        {
                            // Click succeeded
                            logger.LogInformation("Element clicked successfully.");
                            return true;
                        }
                    }
                    else
                    {
                        logger.LogWarning($"Element is not visible in the viewport (attempt {attempt}).");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Attempt {attempt} to interact with element failed: {ex.Message}");
            }

            // Wait before retrying
            await Task.Delay(delayMs);
        }

        logger.LogWarning("Failed to click element after all retries.");
        return false;
    }
   
    public static async Task HandleCookieConsent(IPage page, ILogger logger, int timeout)
    {
        logger.LogInformation("Checking for cookie consent popup...");
        await page.WaitForSelectorAsync("body", new WaitForSelectorOptions { Timeout = timeout });
        if (page == null) return;

        // List of common selectors for cookie banners
        var cookieSelectors = new[]
        {
        // Buttons with common IDs, classes, or attributes
        "button#accept-cookies",
        "button#cookie-accept",
        "button#cookies-accept",
        "button[class*='accept']",
        "button[class*='agree']",
        "button[class*='consent']",
        "button[class*='cookie']",
        "button[id*='accept']",
        "button[id*='agree']",
        "button[id*='consent']",
        "button[id*='cookie']",
        "button[name*='accept']",
        "button[name*='agree']",
        "button[name*='consent']",
        "button[title*='Accept']",
        "button[title*='Agree']",
        "button[aria-label*='Accept']",
        "button[aria-label*='Agree']",
        "button[onclick*='accept']",
        "button[data-accept-action]",
        "button[value*='Accept']",
        "button[value*='Agree']",
        // Links
        "a[class*='accept']",
        "a[class*='agree']",
        "a[class*='consent']",
        "a[class*='cookie']",
        "a[id*='accept']",
        "a[id*='agree']",
        "a[id*='consent']",
        "a[id*='cookie']",
        "a[title*='Accept']",
        "a[title*='Agree']",
        "a[aria-label*='Accept']",
        "a[aria-label*='Agree']",
        "a[onclick*='accept']",
        // Input elements
        "input[type='button'][value*='Accept']",
        "input[type='button'][value*='Agree']",
        "input[type='submit'][value*='Accept']",
        "input[type='submit'][value*='Agree']",
        // Divs or spans acting as buttons
        "div[class*='accept']",
        "div[class*='agree']",
        "div[class*='consent']",
        "div[class*='cookie']",
        "span[class*='accept']",
        "span[class*='agree']",
        "span[class*='consent']",
        "span[class*='cookie']",
        // Generic selectors
        "[id*='cookie'][id*='accept']",
        "[class*='cookie'][class*='accept']",
        "[class*='Cookie'][class*='Accept']",
        "[onclick*='cookie'][onclick*='accept']",
        "[data-cookie='accept']",
        "[data-action='accept']",
    };

        // Try CSS selectors first
        foreach (var selector in cookieSelectors)
        {
            try
            {
                var elements = await page.QuerySelectorAllAsync(selector);
                if (elements != null && elements.Length > 0)
                {
                    foreach (var element in elements)
                    {
                        if (await TryClickElementAsync(element, logger))
                        {
                            logger.LogInformation($"Cookie consent accepted using selector '{selector}'.");
                            await Task.Delay(1000);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Failed to find or click elements with selector '{selector}': {ex.Message}");
            }
        }

        // Try XPath expressions for buttons containing specific text
        var xPathExpressions = new[]
        {
        "//button[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'accept')]",
        "//button[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'agree')]",
        "//button[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'i accept')]",
        "//button[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'i agree')]",
        "//button[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'ok')]",
        "//button[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'got it')]",
        "//button[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'continue')]",
        "//a[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'accept')]",
        "//a[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'agree')]",
        "//a[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'i accept')]",
        "//a[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'i agree')]",
        "//a[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'ok')]",
        "//a[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'got it')]",
        "//a[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'continue')]",
        "//input[@type='button' or @type='submit'][contains(translate(@value, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'accept')]",
        "//input[@type='button' or @type='submit'][contains(translate(@value, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'agree')]",
    };


        foreach (var xPath in xPathExpressions)
        {
            try
            {
                var elements = await page.QuerySelectorAllAsync($"xpath/{xPath}");
                if (elements != null && elements.Length > 0)
                {
                    foreach (var element in elements)
                    {
                        if (await TryClickElementAsync(element, logger))
                        {
                            logger.LogInformation($"Cookie consent accepted using XPath '{xPath}'.");
                            await Task.Delay(1000);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Failed to find or click elements with XPath '{xPath}': {ex.Message}");
            }
        }

        // Handle iframes
        var frames = page.Frames;
        foreach (var frame in frames)
        {
            if (frame != page.MainFrame)
            {
                try
                {
                    foreach (var selector in cookieSelectors)
                    {
                        try
                        {
                            var elements = await frame.QuerySelectorAllAsync(selector);
                            if (elements != null && elements.Length > 0)
                            {
                                foreach (var element in elements)
                                {
                                    if (await TryClickElementAsync(element, logger))
                                    {
                                        logger.LogInformation($"Cookie consent accepted in iframe using selector '{selector}'.");
                                        await Task.Delay(1000);
                                        return;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning($"Failed to find or click elements in iframe with selector '{selector}': {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"Failed to handle cookie consent in iframe: {ex.Message}");
                }
            }
        }

        // As a last resort, try JavaScript
        try
        {
            var clicked = await page.EvaluateFunctionAsync<bool>(@"() => {
            const texts = ['accept', 'agree', 'ok', 'got it', 'i accept', 'i agree', 'continue'];
            const elements = Array.from(document.querySelectorAll('button, a, input[type=""button""], input[type=""submit""]'));
            for (let element of elements) {
                const text = (element.innerText || element.value || '').toLowerCase();
                if (texts.some(t => text.includes(t))) {
                    element.click();
                    return true;
                }
            }
            return false;
        }");

            if (clicked)
            {
                logger.LogInformation("Cookie consent accepted using JavaScript evaluation.");
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Failed to execute JavaScript for cookie consent: {ex.Message}");
        }
    }

    public static async Task<TResultObj<string>> ExtractContentFromUrl(IPage page, string url, NetConnectConfig netConfig, ILogger logger, int timeout)
    {
        try
        {
            logger.LogInformation($"Navigating to {url}");
            var response = await page.GoToAsync(url, new NavigationOptions
            {
                Timeout = timeout,
                WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
            });
            var responseContentType = GetHeaderValueIgnoreCase(response?.Headers, "content-type");

            var isStructuredResponse = IsLikelyJsonContentType(responseContentType) || IsLikelyXmlContentType(responseContentType);

            if (!isStructuredResponse)
            {
                await WaitNetIdle(page, timeout, logger);
            }
            // Store the URL before handling cookie consent
            var initialUrl = page.Url;

            // Handle cookie consent once for HTML pages only.
            if (!isStructuredResponse)
            {
                await HandleCookieConsent(page, logger, timeout);
            }
            else
            {
                logger.LogInformation("Skipping cookie-consent handling for likely JSON/XML API response.");
            }

            if (page.Url != initialUrl) // Check if the page URL changed after handling consent
            {
                logger.LogInformation("Page navigated after handling cookie consent.");
                if (!isStructuredResponse)
                {
                    await WaitNetIdle(page, timeout, logger);
                }
            }
            else
            {
                logger.LogInformation("No navigation occurred after handling cookie consent.");
            }

            // Call the existing ExtractContent method
            return await ExtractContent(page, logger, timeout, responseContentType);

        }
        catch (Exception e)
        {
            return new TResultObj<string>() { Success = false, Message = $" Error : when navigating page {url} . Error was : {e.Message}" };
        }

    }

    private static string? GetHeaderValueIgnoreCase(Dictionary<string, string>? headers, string key)
    {
        if (headers == null || headers.Count == 0 || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        foreach (var kvp in headers)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }
        return null;
    }

    private static bool IsLikelyJsonContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var normalized = contentType.Trim().ToLowerInvariant();
        return normalized.Contains("/json") || normalized.Contains("+json");
    }

    private static bool IsLikelyXmlContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var normalized = contentType.Trim().ToLowerInvariant();
        if (normalized.Contains("html"))
        {
            return false;
        }

        return normalized.Contains("/xml") || normalized.Contains("+xml");
    }

    private static bool LooksLikeJsonPayload(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var t = raw.Trim();
        return (t.StartsWith('{') && t.EndsWith('}')) || (t.StartsWith('[') && t.EndsWith(']'));
    }

    private static bool LooksLikeXmlPayload(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var t = raw.Trim().TrimStart('\uFEFF');
        if (!t.StartsWith("<", StringComparison.Ordinal))
        {
            return false;
        }

        if (t.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static async Task<string> TryExtractStructuredPayloadFromPageAsync(IPage page)
    {
        try
        {
            return await page.EvaluateFunctionAsync<string>(@"() => {
    const candidates = [];
    const pre = document.querySelector('pre');
    if (pre && pre.textContent) candidates.push(pre.textContent.trim());
    if (document.body && document.body.innerText) candidates.push(document.body.innerText.trim());
    if (document.body && document.body.textContent) candidates.push(document.body.textContent.trim());
    if (document.documentElement && document.documentElement.textContent) candidates.push(document.documentElement.textContent.trim());

    for (const raw of candidates) {
        if (!raw) continue;
        const t = raw.trim();
        if ((t.startsWith('{') && t.endsWith('}')) ||
            (t.startsWith('[') && t.endsWith(']')) ||
            (t.startsWith('<') && t.endsWith('>'))) {
            return t;
        }
    }
    return '';
}");
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool TryPrettyPrintJson(string? raw, out string pretty)
    {
        pretty = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            using var json = JsonDocument.Parse(raw);
            pretty = JsonSerializer.Serialize(
                json.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryPrettyPrintXml(string? raw, out string pretty)
    {
        pretty = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var candidate = raw.Trim().TrimStart('\uFEFF');
        if (!candidate.StartsWith("<", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var doc = XDocument.Parse(candidate);
            var rootName = doc.Root?.Name.LocalName ?? string.Empty;
            if (string.Equals(rootName, "html", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            pretty = doc.ToString(SaveOptions.None);
            return !string.IsNullOrWhiteSpace(pretty);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeExtractedContent(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var lines = input
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');

        var sb = new StringBuilder();
        var wroteBlank = false;

        foreach (var rawLine in lines)
        {
            var line = Regex.Replace(rawLine, @"\s+", " ").Trim();
            if (line.Length == 0)
            {
                if (!wroteBlank && sb.Length > 0)
                {
                    sb.AppendLine();
                    wroteBlank = true;
                }
                continue;
            }

            sb.AppendLine(line);
            wroteBlank = false;
        }

        return sb.ToString().Trim();
    }


}
