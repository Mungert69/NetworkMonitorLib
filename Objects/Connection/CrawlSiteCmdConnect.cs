using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using System;
using System.Text.RegularExpressions;
using System.Net;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Http;
using System.Diagnostics;

namespace NetworkMonitor.Connection
{
    public class CrawlSiteCmdConnect : NetConnect
    {
        private ICmdProcessor? _cmdProcessor;
        private string _baseArg;

        public CrawlSiteCmdConnect(ICmdProcessorProvider? cmdProcessorProvider, string baseArg){

           if (cmdProcessorProvider!=null) _cmdProcessor = cmdProcessorProvider.GetProcessor("CrawlSite");
             _baseArg = baseArg;
        }

        public override async Task Connect()
        {
            ExtendTimeout=true;
            ExtendTimeoutMultiplier = 20;

            if (_cmdProcessor == null)
            {
                ProcessException("No Command Processor Available", "Error");
                return;
            }

            PreConnect();
            var result = new ResultObj();
            ushort responseTime = 0;
            try
            {
                string address = MpiStatic.Address;
                if (!address.StartsWith("https://") || !address.StartsWith("http://")) address="https://"+address;
                ushort port = MpiStatic.Port;
                string arguments = $"--url {address}";
                if (MpiStatic.Port != 0)
                {
                    arguments = $"--url {address}:{port}";
                }
                arguments+=$" {_baseArg}";

                Timer.Reset();
                Timer.Start();
                var processorScanDataObj = new ProcessorScanDataObj
                {
                    Arguments = arguments,
                    SendMessage = false
                };
                result = await _cmdProcessor.QueueCommand(Cts, processorScanDataObj);
                Timer.Stop();

                string filteredString = result.Message;

                bool isUp = true;
                var (isHostUp, hostStatus) = GetCrawlStatus(filteredString);
                string statusMessage = hostStatus;
                responseTime = (ushort)Timer.ElapsedMilliseconds;
                // Set response time to max value if host is down
                if (!isHostUp)
                {
                    responseTime = ushort.MaxValue;
                    isUp = false;
                }

               
                if (isUp) ProcessStatus(statusMessage, responseTime, filteredString);
                else ProcessException(filteredString, statusMessage);
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


        private (bool, string) GetCrawlStatus(string input)
        {
            string upPattern = @"Scrolled";
            string downPattern = @"Error";

            if (Regex.IsMatch(input, upPattern, RegexOptions.IgnoreCase))
            {
                return (true, "Site Crawl Complete");
            }
            else if (Regex.IsMatch(input, downPattern, RegexOptions.IgnoreCase))
            {
                return (false, "Crawl Failed");
            }
            else
            {
                return (false, "Crawl status unknown");
            }
        }

       
    }
}
