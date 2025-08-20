using NetworkMonitor.Objects;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkMonitor.Connection
{
    public class SMTPConnect : NetConnect
    {
        /* ─────────────  Seams for unit-testing  ───────────── */

        /// <summary>Factory for the <see cref="TcpClient"/> instance.</summary>
        protected virtual TcpClient CreateTcpClient() => new TcpClient();

        /// <summary>Factory for the <see cref="Stream"/> associated with <paramref name="client"/>.</summary>
        protected virtual Stream CreateStream(TcpClient client) => client.GetStream();

        /// <summary>Wrapper for <c>TcpClient.ConnectAsync</c> so tests can short-circuit it.</summary>
        protected virtual ValueTask ConnectAsync(           // ← ValueTask
     TcpClient client,
     string host,
     int port,
     CancellationToken token) =>
     client.ConnectAsync(host, port, token);         // no conversion

        /* ─────────────  Public API (unchanged)  ───────────── */

        public SMTPConnect() { }

        public async Task<TResultObj<string>> TestConnectionAsync(int port)
        {
            var result = new TResultObj<string>();

            using var client = CreateTcpClient();
            await ConnectAsync(client, MpiStatic.Address, port, Cts.Token);

            await using var stream = CreateStream(client);

            // FIX 1: READ THE BANNER FIRST
            // Wait for the server to send its welcome banner ("220 ...")
            var bannerBuffer = new byte[1024];
            var bannerBytes = await stream.ReadAsync(bannerBuffer, 0, bannerBuffer.Length, Cts.Token);
            var bannerResponse = Encoding.ASCII.GetString(bannerBuffer, 0, bannerBytes);

            // Optional: You can check if the banner starts with "220"
            if (!bannerResponse.StartsWith("220 "))
            {
                result.Success = false;
                result.Message = "Invalid banner from SMTP server: ";
                result.Data = bannerResponse;
                return result;
            }

            // FIX 2: NOW send the HELO command
            var helo = Encoding.ASCII.GetBytes($"HELO {MpiStatic.Address}\r\n"); // Ensure correct CRLF
            await stream.WriteAsync(helo, 0, helo.Length, Cts.Token);

            // FIX 3: Read the response to the HELO command
            var responseBuffer = new byte[1024];
            var responseBytes = await stream.ReadAsync(responseBuffer, 0, responseBuffer.Length, Cts.Token);
            var response = Encoding.ASCII.GetString(responseBuffer, 0, responseBytes);

            // FIX 4: Send a QUIT command before disconnecting (Polite SMTP behavior)
            var quit = Encoding.ASCII.GetBytes("QUIT\r\n");
            await stream.WriteAsync(quit, 0, quit.Length, Cts.Token);
            // You can read the QUIT response if you want, but it's not strictly necessary

            if (response.StartsWith("250 "))
            {
                result.Success = true;
                result.Message = "Connect HELO";
            }
            else
            {
                result.Success = false;
                result.Message = "Unexpected response from SMTP server: ";
                result.Data = response;
            }

            return result;
        }
        public override async Task Connect()
        {
            PreConnect();

            var port = MpiStatic.Port != 0 ? MpiStatic.Port : (ushort)25;
            var result = new TResultObj<string>();

            try
            {
                Timer.Reset();
                Timer.Start();

                result = await TestConnectionAsync(port);

                Timer.Stop();

                if (result.Success)
                    ProcessStatus("Connect Ok", (ushort)Timer.ElapsedMilliseconds);
                else
                    ProcessException(result.Data ?? string.Empty, result.Message);
            }
            catch (OperationCanceledException)
            {
                ProcessException("Timeout", "Timeout");
            }
            catch (Exception ex)
            {
                ProcessException(ex.Message, "Exception");
            }
            finally
            {
                PostConnect();
            }
        }
    }
}
