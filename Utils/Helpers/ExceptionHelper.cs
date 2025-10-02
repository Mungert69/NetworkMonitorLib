using System.Net.Http;
using System.Text;
using System.Text.Json;
using NetworkMonitor.Objects;
namespace NetworkMonitor.Utils.Helpers;

public class ExceptionHelper {
    public static string AppID = "notset";

    /// <summary>
    /// Handles global exceptions: logs, sends to API, and (if on MAUI) shows an alert to the user.
    /// </summary>
    public static void HandleGlobalException(Exception ex, string type)
    {
        HttpClient httpClient = new HttpClient();

        if (ex != null)
        {
            Console.WriteLine($"{type}: {ex.Message}\n{ex.StackTrace}");

            // Send exception details to your API
            Task.Run(async () =>
            {
                try
                {
                    var exceptionData = new
                    {
                        ExceptionType = type,
                        Message = ex.Message,
                        StackTrace = ex.StackTrace,
                        Timestamp = DateTime.UtcNow
                    };

                    string json = JsonSerializer.Serialize(exceptionData);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    string apiUrl = $"https://exceptions.{AppConstants.AppDomain}/LogException/{AppID}]";
                    var response = await httpClient.PostAsync(apiUrl, content);

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Failed to send exception details: {response.StatusCode}");
                    }
                }
                catch (Exception apiEx)
                {
                    Console.WriteLine($"Error sending exception details: {apiEx.Message}");
                }
            });


            // Try to show a user-facing error if running in a MAUI app
            try
            {
                // Only attempt if Application.Current is available (i.e., in MAUI context)
                var appType = Type.GetType("Microsoft.Maui.Controls.Application, Microsoft.Maui.Controls");
                var mainPageProp = appType?.GetProperty("Current");
                var appInstance = mainPageProp?.GetValue(null);
                var mainPage = appInstance?.GetType().GetProperty("MainPage")?.GetValue(appInstance);

                if (mainPage != null)
                {
                    // Use reflection to call DisplayAlert if available
                    var displayAlertMethod = mainPage.GetType().GetMethod("DisplayAlert", new[] { typeof(string), typeof(string), typeof(string) });
                    if (displayAlertMethod != null)
                    {
                        // DisplayAlert is async, so use GetAwaiter().GetResult() to block
                        displayAlertMethod.Invoke(mainPage, new object[] { "Error", $"{type}: {ex.Message}", "OK" });
                    }
                }
            }
            catch
            {
                // Fallback: just write to console if UI not available
                Console.WriteLine($"[User Error] {type}: {ex.Message}");
            }

        }
    }
}
