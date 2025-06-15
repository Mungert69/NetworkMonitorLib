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



    public static async Task<TResultObj<string>> ExtractContent(IPage page, ILogger logger, int timeout)
    {

        logger.LogInformation("Waiting for page content to load...");
        await page.WaitForSelectorAsync("body", new WaitForSelectorOptions { Timeout = timeout });

        logger.LogInformation("Extracting content...");

        // Extract text content with inline links, excluding cookie and privacy-related elements
        var content = await page.EvaluateFunctionAsync<string>(@"() => {
    const unwantedKeywords = ['cookies', 'cookie'];

    const getTextWithLinks = (node) => {
        let text = '';
        if (node.nodeType === Node.TEXT_NODE) {
            return node.textContent;
        } else if (node.nodeType === Node.ELEMENT_NODE) {
            if (['SCRIPT', 'STYLE', 'IMG', 'HEADER', 'FOOTER', 'NAV'].includes(node.nodeName)) {
                return '';
            }

            // Check if element contains cookie-related keywords
            const elementText = node.innerText || '';
            if (unwantedKeywords.some(keyword => elementText.toLowerCase().includes(keyword))) {
                return '';
            }

            let prefix = '';
            let suffix = '';

            // Handle different elements
            if (/H[1-6]/.test(node.nodeName)) {
                // Headings (H1-H6)
                let level = parseInt(node.nodeName.charAt(1));
                prefix = '\n' + '#'.repeat(level) + ' ';
                suffix = '\n';
            } else if (node.nodeName === 'P') {
                prefix = '\n';
                suffix = '\n';
            } else if (node.nodeName === 'BR') {
                return '\n';
            } else if (node.nodeName === 'A') {
                return `[${node.textContent}](${node.href})`;
            } else if (node.nodeName === 'LI') {
                prefix = '- ';
                suffix = '\n';
            } else if (node.nodeName === 'UL' || node.nodeName === 'OL') {
                prefix = '\n';
                suffix = '\n';
            } else if (node.nodeName === 'DIV') {
                prefix = '\n';
                suffix = '\n';
            }

            for (let child of node.childNodes) {
                text += getTextWithLinks(child);
            }

            return prefix + text.trim() + suffix;
        }
        return '';
    };

    // Target the main content area (article, main)
    const mainContent = document.querySelector('article, main');
    if (mainContent) {
        return getTextWithLinks(mainContent);
    }

    // Fallback to body if main content not found
    return getTextWithLinks(document.body);
}");

        // Check if the extracted content is valid
        if (string.IsNullOrWhiteSpace(content))
        {
            logger.LogWarning("Page content is empty.");
            return new TResultObj<string>
            {
                Success = false,
                Message = "No page content found."
            };
        }
        else if (content.Split(' ').Length < 50)
        {
            logger.LogWarning("Less than 50 words of content.");
            return new TResultObj<string>
            {
                Success = false,
                Message = "No useful content found; page content too short."
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
            await page.GoToAsync(url, new NavigationOptions { Timeout = timeout });
            await WaitNetIdle(page, timeout, logger);
            // Store the URL before handling cookie consent
            var initialUrl = page.Url;

            // Handle cookie consent once
            await HandleCookieConsent(page, logger, timeout);

            if (page.Url != initialUrl) // Check if the page URL changed after handling consent
            {
                logger.LogInformation("Page navigated after handling cookie consent.");
                await WaitNetIdle(page, timeout, logger);
            }
            else
            {
                logger.LogInformation("No navigation occurred after handling cookie consent.");
            }

            // Call the existing ExtractContent method
            return await ExtractContent(page, logger, timeout);

        }
        catch (Exception e)
        {
            return new TResultObj<string>() { Success = false, Message = $" Error : when navigating page {url} . Error was : {e.Message}" };
        }

    }


}