using System;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NetworkMonitor.Objects;
namespace NetworkMonitor.Utils.Helpers;

public class ExceptionHelper {
    public static string AppID = "notset";

    private static readonly HttpClient s_httpClient = new HttpClient();

    /// <summary>
    /// Handles global exceptions: logs, sends to API, and (if on MAUI) shows an alert to the user.
    /// </summary>
    public static void HandleGlobalException(Exception ex, string type)
    {
        if (ex == null)
        {
            return;
        }

        Console.WriteLine($"{type}: {ex.Message}\n{ex.StackTrace}");

        // Send exception details to your API (fire-and-forget)
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

                // fixed stray bracket in URL
                string apiUrl = $"https://exceptions.{AppConstants.AppDomain}/LogException/{AppID}";
                var response = await s_httpClient.PostAsync(apiUrl, content).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Failed to send exception details: {response.StatusCode}");
                }
            }
            catch (Exception apiEx)
            {
                Console.WriteLine($"Error sending exception details: {apiEx.Message}");
            }
        }).ConfigureAwait(false);


        // Try to show a user-facing error if running in a MAUI app.
        // Important: marshal to the UI thread and do NOT block the thread that reported the exception.
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
                    // Prefer MAUI MainThread APIs when present
                    var mainThreadType = Type.GetType("Microsoft.Maui.ApplicationModel.MainThread, Microsoft.Maui.Core")
                                         ?? Type.GetType("Microsoft.Maui.Essentials.MainThread, Microsoft.Maui.Essentials");

                    Action showAlert = () =>
                    {
                        try
                        {
                            var result = displayAlertMethod.Invoke(mainPage, new object[] { "Error", $"{type}: {ex.Message}", "OK" });
                            // If the method is async and returns a Task, fire-and-forget it safely
                            if (result is Task t)
                            {
                                _ = t.ConfigureAwait(false);
                            }
                        }
                        catch
                        {
                            // Best-effort only; swallow any UI invocation errors
                        }
                    };

                    if (mainThreadType != null)
                    {
                        // Try BeginInvokeOnMainThread(Action) or InvokeOnMainThreadAsync signatures
                        var beginInvoke = mainThreadType.GetMethod("BeginInvokeOnMainThread", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Action) }, null)
                                        ?? mainThreadType.GetMethod("InvokeOnMainThreadAsync", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Action) }, null)
                                        ?? mainThreadType.GetMethod("InvokeOnMainThreadAsync", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Func<Task>) }, null);

                        if (beginInvoke != null)
                        {
                            try
                            {
                                // If method expects Func<Task>, wrap action
                                if (beginInvoke.GetParameters()[0].ParameterType == typeof(Func<Task>))
                                {
                                    Func<Task> func = () =>
                                    {
                                        showAlert();
                                        return Task.CompletedTask;
                                    };
                                    beginInvoke.Invoke(null, new object[] { func });
                                }
                                else
                                {
                                    beginInvoke.Invoke(null, new object[] { showAlert });
                                }
                                return;
                            }
                            catch
                            {
                                // Fall through to dispatcher fallback
                            }
                        }
                    }

                    // Dispatcher fallback (Application.Current.Dispatcher.Dispatch(Action))
                    try
                    {
                        var dispatcherProp = appInstance?.GetType().GetProperty("Dispatcher");
                        var dispatcher = dispatcherProp?.GetValue(appInstance);
                        var dispatchMethod = dispatcher?.GetType().GetMethod("Dispatch", new[] { typeof(Action) });
                        if (dispatchMethod != null)
                        {
                            dispatchMethod.Invoke(dispatcher, new object[] { showAlert });
                            return;
                        }
                    }
                    catch
                    {
                        // ignore and fall through to fire-and-forget
                    }

                    // Last resort: run the UI call on a background task (non-blocking), best-effort only
                    //Task.Run(showAlert).ConfigureAwait(false);
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
