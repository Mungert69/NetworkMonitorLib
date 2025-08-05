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
    TcpClient         client,
    string            host,
    int               port,
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

            var helo = Encoding.ASCII.GetBytes($"HELO {MpiStatic.Address} \r\n");
            await stream.WriteAsync(helo, 0, helo.Length, Cts.Token);

            var buffer   = new byte[1024];
            var bytes    = await stream.ReadAsync(buffer, 0, buffer.Length, Cts.Token);
            var response = Encoding.ASCII.GetString(buffer, 0, bytes);

            if (response.StartsWith("220 "))
            {
                result.Success = true;
                result.Message = "Connect HELO";
            }
            else
            {
                result.Success = false;
                result.Message = "Unexpected response from SMTP server: ";
                result.Data    = response;
            }

            return result;
        }

        public override async Task Connect()
        {
            PreConnect();

            var port   = MpiStatic.Port != 0 ? MpiStatic.Port : (ushort)25;
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
