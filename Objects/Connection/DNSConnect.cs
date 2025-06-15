using NetworkMonitor.Objects;
using System;
using System.Text;
using System.Net;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
namespace NetworkMonitor.Connection
{
    public class DNSConnect : NetConnect
    {
        public DNSConnect()
        {
        }
        public override async Task Connect()
        {
            PreConnect();
            try
            {

               Timer.Reset();
                Timer.Start();
                IPAddress[] ipAddresses = await Dns.GetHostAddressesAsync(MpiStatic.Address, Cts.Token);
                Timer.Stop();
                if (ipAddresses.Length > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (var ipAddress in ipAddresses)
                    {
                        sb.Append(ipAddress);
                        sb.Append(", ");
                    }
                    string result = " : "+sb.ToString().TrimEnd(new char[] { ',', ' ' });
                    ProcessStatus( "Found IP Addresses", (ushort)Timer.ElapsedMilliseconds, result);
                    //MonitorPingInfo.PingInfos.Add(PingInfo);
                }
                else
                {
                    ProcessException( "No IP addresses found for host","No IP addresses found for host");
                }
            }
            catch (OperationCanceledException) when (Cts.Token.IsCancellationRequested)
            {
                ProcessException( "Timeout","Timeout");
            }
            catch (Exception e)
            {
                ProcessException( e.Message,"Exception");
            }
            finally
            {
                PostConnect();
            }
        }
    }
}
