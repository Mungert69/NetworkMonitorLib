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
    public class LaunchHelper
    {

        public static bool CheckDisplay(ILogger logger,bool forceHeadless =false)
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

        public static async Task<LaunchOptions> GetLauncher(string commandPath, ILogger? logger = null, bool useHeadless = true)
        {
            ViewPortOptions vpo = new ViewPortOptions();
            vpo.Width = 1920;
            vpo.Height = 1280;
            LaunchOptions lo;
            var downloadPath = Path.Combine(commandPath, "chrome-bin");

            // Create the directory if it doesn't exist
            if (!Directory.Exists(downloadPath))
            {
                Directory.CreateDirectory(downloadPath);
            }

            var bfo = new BrowserFetcherOptions
            {
                Path = downloadPath // Set the download path to "chrome-bin"
            };
            logger?.LogInformation($"LaunchHelper Chromium path is {bfo.Path}");
            var browserFetcher = new BrowserFetcher(bfo);

            // Check if the executable path exists
            string chromiumPath = Path.Combine(bfo.Path, "Chrome");
            if (!Directory.Exists(chromiumPath))
            {
                logger?.LogInformation($"Chromium not found. Downloading...");
                await browserFetcher.DownloadAsync();
            }
            else
            {
                logger?.LogInformation($"Chromium revision already downloaded.");
            }

            // Dynamically find the Chrome executable based on the platform
            string? chromeExecutable = null;
            // Recursively search for the Chrome executable in the directories
            string? FindChromeExecutable(string rootPath)
            {
                string exeName = "chrome";

#if Windows
                exeName = "chrome.exe";
#elif OSX
                exeName = "Google Chrome.app/Contents/MacOS/Google Chrome";
#endif


                foreach (var filePath in Directory.GetFiles(rootPath, exeName, SearchOption.AllDirectories))
                {
                    if (File.Exists(filePath)) // Ensure it's a valid file
                        return filePath;
                }

                return null;
            }
            logger?.LogInformation($"Searching for chrome executable...");

            chromeExecutable = FindChromeExecutable(chromiumPath);
            logger?.LogInformation($"Using Chrome executable path {chromeExecutable} .");
            if (string.IsNullOrEmpty(chromeExecutable))
            {
                throw new FileNotFoundException($"Chrome executable not found");
            }

            lo = new LaunchOptions()
            {
                Headless = useHeadless,
                DefaultViewport = vpo,
                ExecutablePath = chromeExecutable, // Dynamically set the Chrome executable path based on platform
                Args = new[]
                     {
        "--no-sandbox",
        "--disable-setuid-sandbox",
        "--disable-dev-shm-usage",
        "--disable-extensions",
        "--disable-gpu",
         "--disable-blink-features=AutomationControlled"
                    }
            };

            return lo;
        }


    }



}