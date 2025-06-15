using System.Net.Http;
using System.Text;
using System.Text.Json;
using NetworkMonitor.Objects;
namespace NetworkMonitor.Utils.Helpers;

public class ExceptionHelper {
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

                       
                        string apiUrl = $"https://monitorsrv.{AppConstants.AppDomain}/Admin/LogException/12e1f3f8jcjar";
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
            }
        }


}
