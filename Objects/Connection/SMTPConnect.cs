using NetworkMonitor.Objects;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using System.Text;
using System;
namespace NetworkMonitor.Connection
{
    public class SMTPConnect : NetConnect
    {
        public SMTPConnect()
        {

        }
        public async Task<TResultObj<string>> TestConnectionAsync(int port)
        {
            var result = new TResultObj<string>();
            using (var client = new TcpClient())
            {

                await client.ConnectAsync(MpiStatic.Address, port, Cts.Token);
                using (var stream = client.GetStream())
                {
                    var helloCommand = Encoding.ASCII.GetBytes("HELO " + MpiStatic.Address + " \r\n");
                    await stream.WriteAsync(helloCommand, 0, helloCommand.Length);
                    var buffer = new byte[1024];
                    var response = "";


                    var bytes = await stream.ReadAsync(buffer, 0, buffer.Length, Cts.Token);
                    response += Encoding.ASCII.GetString(buffer, 0, bytes);
                    if (response.StartsWith("220 "))
                    {
                        result.Success = true;
                        result.Message = "Connect HELO";
                        //client.Close();
                        return result;
                    }
                    else
                    {
                        //Console.WriteLine("Unexpected response from SMTP server: " + response);
                        //client.Close();
                        result.Success = false;
                        result.Message = "Unexpected response from SMTP server: ";
                        result.Data = response.ToString();
                        return result;
                    }
                }
            }
        }
        public override async Task Connect()
        {
            PreConnect();
            int port = 25;
            if (MpiStatic.Port != 0) port = MpiStatic.Port;
            var result = new TResultObj<string>();
            try
            {
                Timer.Reset();
                Timer.Start();
                result = await TestConnectionAsync(port);
                Timer.Stop();
                if (!result.Success)
                {
                    ProcessException(result.Data ?? "", result.Message);
                }
                else
                {
                    ProcessStatus("Connect Ok", (ushort)Timer.ElapsedMilliseconds);
                }
            }
            catch (OperationCanceledException)
            {
                ProcessException("Timeout", "Timeout");

            }
             catch (Exception e)
            {
                ProcessException(e.Message, "Exception");

            }
            finally{
                PostConnect();
            }
        }
    }
}
