/*
A class named ICMPConnect which is a subclass of the NetConnect class. This class is responsible for creating an asynchronous connection using the Internet Control Message Protocol (ICMP). It uses the Ping class from the System.Net.NetworkInformation namespace to send an ICMP echo request to a specified host.

The connect method is called to initiate the connection. It creates a new instance of the Ping class and sets up a completion event to be raised when the Ping operation is finished. The completion event handler method, PingCompletedCallback, is called when the Ping operation is complete. The PingCompletedCallback method processes the response from the host, updates the status information of the connection and sets an AutoResetEvent to signal the completion of the connect method.

The ProcessStatus method is called to process the response from the host. It updates the status information of the connection, including the number of packets sent and received, the round-trip time, and whether the connection is up or down.
*/

using NetworkMonitor.Objects;
using NetworkMonitor.Utils.Helpers;
using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
namespace NetworkMonitor.Connection
{
    public class ICMPConnect : NetConnect
    {
        public ICMPConnect()
        {
        }
        public override async Task Connect()
        {
            try
            {
                PreConnect();
                string who = MpiStatic.Address;
                Ping pingSender = new Ping();

                // Use TaskCompletionSource to signal when the ping is completed
                TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

                pingSender.PingCompleted += (sender, e) =>
                {
                    PingCompletedCallback(sender, e);
                    tcs.SetResult(true); // Signal that the ping is completed
                };

                pingSender.SendAsync(who, (int)MpiStatic.Timeout, null);

                await tcs.Task; // Await the completion of the ping
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
        private void PingCompletedCallback(object sender, PingCompletedEventArgs e)
        {
           
            // If the operation was canceled, display a message to the user.
            if (e.Cancelled)
            {
                ProcessException("Ping Canceled","Ping Canceled");
            }
            // If an error occurred, display the exception to the user.
            if (e.Error != null)
            {
                ProcessException(e.Error.ToString(),"Exception");
            }
            PingReply? reply = e.Reply;
            ProcessPingStatus(reply);
        }
        private void ProcessPingStatus(PingReply? reply)
        {

            if (reply == null)
            {
                ProcessException("Ping Reply Null","Ping Reply Null");
                return;
            }
            string replyStatus = reply.Status.ToString();

            if (reply.Status == IPStatus.Success)
            {
                ProcessStatus(replyStatus, (ushort)reply.RoundtripTime);
            }
            else
            {
                ProcessException(reply.Status.ToString(),"Exception");
                return;
            }

            //MonitorPingInfo.PingInfos.Add(PingInfo);
        }
    }
}
