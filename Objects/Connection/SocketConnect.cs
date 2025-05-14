using NetworkMonitor.Objects;
using NetworkMonitor.Utils.Helpers;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace NetworkMonitor.Connection
{
    public class SocketConnect : NetConnect
    {
        private const int DefaultHttpPort = 443;

        public SocketConnect()
        {
        }

        public override async Task Connect()
        {
            Timer.Reset();
            try
            {
                PreConnect();
                string host = MpiStatic.Address;
                var port = DefaultHttpPort;
                if (MpiStatic.Port != 0)
                {
                    port = MpiStatic.Port;
                }

                // Resolve the domain name to an IP address
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(host);
                if (addresses.Length == 0)
                {
                    ProcessException("Unable to resolve domain.", "Unable to resolve domain.");
                    return;
                }

                var endpoint = new IPEndPoint(addresses[0], port);
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    Timer.Start();

                    var connectTask = socket.ConnectAsync(endpoint);
                    if (await Task.WhenAny(connectTask, Task.Delay(MpiStatic.Timeout)) != connectTask)
                    {
                        ProcessException("Connection timed out.", "Connection timed out.");
                        return;
                    }

                    await connectTask; // Ensure any exceptions are thrown
                }
                Timer.Stop();
                ProcessStatus($"Connected", (ushort)Timer.ElapsedMilliseconds);
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
