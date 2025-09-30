using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkMonitor.Connection
{
    public class DNSConnect : NetConnect
    {
        // ❶ ––– the seam: override in a test spy
        protected virtual Task<IPAddress[]> ResolveAsync(
            string host, CancellationToken token) =>
            Dns.GetHostAddressesAsync(host, token);

        public override async Task Connect()
        {
            PreConnect();
            try
            {
                Timer.Reset();
                Timer.Start();

                // ❷ ––– use the seam
                IPAddress[] ipAddresses =
                    await ResolveAsync(MpiStatic.Address, Cts.Token);

                Timer.Stop();

                if (ipAddresses.Length > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var ip in ipAddresses)
                        sb.Append(ip).Append(", ");

                    ProcessStatus(
                        "Found IP Addresses",
                        (ushort)Timer.ElapsedMilliseconds,
                        " : " + sb.ToString().TrimEnd(',', ' '));
                }
                else
                {
                    ProcessException(
                        "No IP addresses found for host",
                        "Exception");
                }
            }
            catch (OperationCanceledException) when (Cts.Token.IsCancellationRequested)
            {
                ProcessException("Timeout while resolving host address", "Exception");
            }
            catch (Exception e)
            {
                ProcessException(e.Message, "Exception");
            }
            finally
            {
                PostConnect();
            }
        }
    }
}
