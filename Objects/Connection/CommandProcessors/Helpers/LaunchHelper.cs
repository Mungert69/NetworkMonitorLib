using System;
using System.Runtime.InteropServices;
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
using NetworkMonitor.Service.Services.OpenAI;

namespace NetworkMonitor.Connection
{
    public interface ILaunchHelper
    {
        bool CheckDisplay(ILogger logger, bool forceHeadless = false);
        Task<LaunchOptions> GetLauncher(string commandPath, ILogger? logger = null, bool useHeadless = true, bool forceRedownload = false);
    }
    public class LaunchHelper : ILaunchHelper
    {
        private  readonly SemaphoreSlim _downloadSemaphore = new SemaphoreSlim(1, 1);

        public  bool CheckDisplay(ILogger logger, bool forceHeadless = false)
        {
            bool isGuiAvailable = false;

            // Check for GUI support on Linux
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Check for DISPLAY (X11) or WAYLAND_DISPLAY (Wayland)
                isGuiAvailable = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) ||
                                 !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

                // Log the result of the check
                if (isGuiAvailable)
                {
                    logger.LogInformation("Graphical environment detected (DISPLAY or WAYLAND_DISPLAY is set).");
                }
                else
                {
                    logger.LogInformation("No graphical environment detected (DISPLAY and WAYLAND_DISPLAY are not set).");
                }

                // Fallback: Check if Xvfb is running (common in headless environments)
                if (!isGuiAvailable)
                {
                    try
                    {
                        // Check if Xvfb is installed and running
                        var xvfbProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "pgrep",
                                Arguments = "Xvfb",
                                RedirectStandardOutput = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };

                        xvfbProcess.Start();
                        xvfbProcess.WaitForExit();

                        if (xvfbProcess.ExitCode == 0)
                        {
                            isGuiAvailable = true;
                            logger.LogInformation("Xvfb (virtual display) is running. Assuming graphical environment is available.");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning($"Failed to check for Xvfb: {ex.Message}");
                    }
                }
            }
            // Check for GUI support on macOS or Windows
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Assume GUI is available on macOS and Windows unless explicitly running in a headless environment
                isGuiAvailable = true;
                logger.LogInformation("Running on macOS or Windows. Assuming graphical environment is available.");
            }
            else
            {
                logger.LogWarning("Unsupported operating system. Assuming no graphical environment is available.");
            }

            // Allow manual override via configuration
            bool useHeadless = forceHeadless || !isGuiAvailable;

            logger.LogInformation($"Launching browser in {(useHeadless ? "headless" : "non-headless")} mode.");
            return useHeadless;
        }

        public  async Task<LaunchOptions> GetLauncher(string commandPath, ILogger? logger = null, bool useHeadless = true, bool forceRedownload = false)
        {
            var vpo = new ViewPortOptions { Width = 1920, Height = 1280 };
            commandPath = Path.GetFullPath(commandPath.Replace('/', Path.DirectorySeparatorChar));
            var downloadPath = Path.Combine(commandPath, "chrome-bin");

            if (!Directory.Exists(downloadPath))
                Directory.CreateDirectory(downloadPath);

            var bfo = new BrowserFetcherOptions { Path = downloadPath };
            logger?.LogInformation($"LaunchHelper Chromium path is {bfo.Path}");
            var browserFetcher = new BrowserFetcher(bfo);

            var chromiumPath = Path.Combine(bfo.Path, "Chrome");
            var successMarker = Path.Combine(bfo.Path, ".chromium_downloaded");

            // Optionally clear download marker
            if (forceRedownload && File.Exists(successMarker))
                File.Delete(successMarker);

            string? chromeExecutable = FindChromeExecutable(chromiumPath);

            bool executableMissingOrInvalid = string.IsNullOrEmpty(chromeExecutable) || !File.Exists(chromeExecutable);

            if (!File.Exists(successMarker) || executableMissingOrInvalid)
            {
                logger?.LogWarning("Chromium not found or corrupted. Downloading...");
                await SafeDownloadChromiumAsync(browserFetcher, logger);
                File.WriteAllText(successMarker, DateTime.UtcNow.ToString("o"));
                chromeExecutable = FindChromeExecutable(chromiumPath);
            }
            else
            {
                logger?.LogInformation("Chromium already downloaded. Skipping download.");
            }

            if (string.IsNullOrEmpty(chromeExecutable))
                throw new FileNotFoundException("Chrome executable not found");

            logger?.LogInformation($"Using Chrome executable path {chromeExecutable}");

            var lo = new LaunchOptions
            {
                Headless = useHeadless,
                DefaultViewport = vpo,
                ExecutablePath = chromeExecutable,
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-extensions",
                    "--disable-gpu",
                    "--disable-blink-features=AutomationControlled",
                    "--disable-infobars",
                    "--user-agent=\"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.5993.90 Safari/537.36\""

                }
            };

            return lo;

             string? FindChromeExecutable(string rootPath)
            {
                string exeName = "chrome";
#if WINDOWS
                exeName = "chrome.exe";
#elif OSX
                exeName = "Google Chrome.app/Contents/MacOS/Google Chrome";
#endif
                try
                {
                    var files = Directory.GetFiles(rootPath, exeName, SearchOption.AllDirectories);
                    return files.FirstOrDefault(File.Exists);
                }
                catch { return null; }
            }
        }

        private  async Task SafeDownloadChromiumAsync(
        BrowserFetcher browserFetcher,
        ILogger? logger = null,
        int timeoutSeconds = 120,
        int maxRetries = 3)
        {
            // Try to enter semaphore immediately
            if (!await _downloadSemaphore.WaitAsync(0))
            {
                logger?.LogError("Another thread is already downloading Chromium.");
                throw new InvalidOperationException("Chromium download already in progress.");
            }

            try
            {
                // Perform download with retry logic
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        logger?.LogInformation($"[Download] Attempt {attempt}...");
                        await browserFetcher.DownloadAsync();
                        logger?.LogInformation("Chromium downloaded successfully.");
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        logger?.LogWarning($"Attempt {attempt}: download timed out.");
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError($"Attempt {attempt} failed: {ex.GetType().Name} - {ex.Message}");
                    }

                    if (attempt < maxRetries)
                        await Task.Delay(3000);
                }

                throw new TimeoutException("Chromium download failed after multiple attempts.");
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }

    }
}
