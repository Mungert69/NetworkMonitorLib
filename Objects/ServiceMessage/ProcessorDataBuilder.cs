
using NetworkMonitor.Utils;

namespace NetworkMonitor.Objects.ServiceMessage
{
    public class ProcessorDataBuilder
    {
        private static void ReplaceMonitorPingInfos(ProcessorDataObj processorDataObj, List<MonitorPingInfo> monitorPingInfos)
        {
            var removeMonitorPingInfos = monitorPingInfos.Where(w => w.AppID == processorDataObj.AppID).ToList();
            removeMonitorPingInfos.ForEach(f => monitorPingInfos.Remove(f));
            monitorPingInfos.AddRange(processorDataObj.MonitorPingInfos);
        }
        /* private static void ReplaceMonitorStatusAlerts(ProcessorDataObj processorDataObj, List<MonitorStatusAlert> monitorStatusAlerts)
         {
             var updateMonitorStatusAlerts = monitorStatusAlerts.Where(w => w.AppID == processorDataObj.AppID).ToList();
             var newMonitorStatusAlerts = new List<MonitorStatusAlert>();
             processorDataObj.MonitorStatusAlerts.ForEach(procStatusAlert =>
             {
                 var f = updateMonitorStatusAlerts.Where(w => w.ID == procStatusAlert.ID).FirstOrDefault();
                 if (f == null)
                 {
                     newMonitorStatusAlerts.Add(procStatusAlert);
                 }
                 else
                 {
                     f.AppID = procStatusAlert.AppID;
                     f.Address = procStatusAlert.Address;
                     f.AlertFlag = procStatusAlert.AlertFlag;
                     f.AlertSent = procStatusAlert.AlertSent;
                     f.DownCount = procStatusAlert.DownCount;
                     f.EndPointType = procStatusAlert.EndPointType;
                     f.EventTime = procStatusAlert.EventTime;
                     f.IsUp = procStatusAlert.IsUp;
                     f.UserName = procStatusAlert.UserName;
                     f.UserID = procStatusAlert.UserID;
                     f.Timeout = procStatusAlert.Timeout;
                     f.MonitorPingInfoID = procStatusAlert.MonitorPingInfoID;
                     f.Message = procStatusAlert.Message;
                 }
             });
             monitorStatusAlerts.AddRange(newMonitorStatusAlerts);
         }*/
        public static void MergeWithoutPingInfos(byte[] monitorPingInfoBytes, List<MonitorPingInfo> monitorPingInfos)
        {
            if (monitorPingInfoBytes == null) throw new ArgumentNullException(nameof(monitorPingInfoBytes));
            ProcessorDataObj? processorDataObj = ExtractFromZ<ProcessorDataObj>(monitorPingInfoBytes);
            if (processorDataObj == null) throw new ArgumentNullException(nameof(processorDataObj));

            ReplaceMonitorPingInfos(processorDataObj, monitorPingInfos);
        }
        /* public static ProcessorDataObj MergeMonitorStatusAlerts(string monitorStatusAlertString, List<MonitorStatusAlert> monitorStatusAlerts)
         {
             if (monitorStatusAlertString == null) throw new ArgumentNullException(nameof(monitorStatusAlertString));
             ProcessorDataObj? processorDataObj = ExtractFromZString<ProcessorDataObj>(monitorStatusAlertString);
             if (processorDataObj == null) throw new ArgumentNullException(nameof(processorDataObj));
             //ReplaceMonitorStatusAlerts(processorDataObj, monitorStatusAlerts);
             monitorStatusAlerts.RemoveAll(r => r.AppID == processorDataObj.AppID);
             monitorStatusAlerts.AddRange(processorDataObj.MonitorStatusAlerts);
             return processorDataObj;
         }*/

        public static ProcessorDataObj? MergeMonitorStatusAlerts(ProcessorDataObj? processorDataObj, List<IAlertable> monitorStatusAlerts)
        {
            if (processorDataObj == null) return null;
            // Update existing objects
            foreach (var existingAlert in monitorStatusAlerts)
            {
                var matchingAlert = processorDataObj.MonitorStatusAlerts.FirstOrDefault(m => m.ID == existingAlert.ID);
                if (matchingAlert != null)
                {
                    // Update properties of existingAlert based on matchingAlert
                    existingAlert.AppID = matchingAlert.AppID;
                    existingAlert.Address = matchingAlert.Address;
                    existingAlert.AlertFlag = matchingAlert.AlertFlag;
                    existingAlert.AlertSent = matchingAlert.AlertSent;
                    existingAlert.DownCount = matchingAlert.DownCount;
                    existingAlert.EndPointType = matchingAlert.EndPointType;
                    existingAlert.EventTime = matchingAlert.EventTime;
                    existingAlert.IsUp = matchingAlert.IsUp;
                    existingAlert.UserName = matchingAlert.UserName;
                    existingAlert.UserID = matchingAlert.UserID;
                    existingAlert.Timeout = matchingAlert.Timeout;
                    existingAlert.MonitorPingInfoID = matchingAlert.MonitorPingInfoID;
                    existingAlert.Message = matchingAlert.Message;
                    existingAlert.AddUserEmail = matchingAlert.AddUserEmail;
                    existingAlert.IsEmailVerified = matchingAlert.IsEmailVerified;
                  
                }
            }

            // Add new objects
            var newAlerts = processorDataObj.MonitorStatusAlerts.Where(m => !monitorStatusAlerts.Any(e => e.ID == m.ID)).ToList();
            monitorStatusAlerts.AddRange(newAlerts);

            // Remove objects not in processorDataObj.MonitorStatusAlerts
            // monitorStatusAlerts.RemoveAll(r => !processorDataObj.MonitorStatusAlerts.Any(m => m.ID == r.ID));

            return processorDataObj;
        }

        public static ProcessorDataObj? MergePredictStatusAlerts(ProcessorDataObj? processorDataObj, List<IAlertable> predictStatusAlerts)
        {
            if (processorDataObj == null) return null;
            // Update existing objects
            foreach (var existingAlert in predictStatusAlerts)
            {
                var matchingAlert = processorDataObj.PredictStatusAlerts.FirstOrDefault(m => m.ID == existingAlert.ID);
                if (matchingAlert != null)
                {
                    // Update properties of existingAlert based on matchingAlert
                    existingAlert.AppID = matchingAlert.AppID;
                    existingAlert.Address = matchingAlert.Address;
                    existingAlert.AlertFlag = matchingAlert.AlertFlag;
                    existingAlert.AlertSent = matchingAlert.AlertSent;
                    //existingAlert.DownCount = matchingAlert.DownCount;
                    existingAlert.EndPointType = matchingAlert.EndPointType;
                    existingAlert.EventTime = matchingAlert.EventTime;
                    // existingAlert.IsUp = matchingAlert.IsUp;

                    existingAlert.UserName = matchingAlert.UserName;
                    existingAlert.UserID = matchingAlert.UserID;
                    existingAlert.Timeout = matchingAlert.Timeout;
                    existingAlert.MonitorPingInfoID = matchingAlert.MonitorPingInfoID;
                    existingAlert.Message = matchingAlert.Message;
                    existingAlert.AddUserEmail = matchingAlert.AddUserEmail;
                    existingAlert.IsEmailVerified = matchingAlert.IsEmailVerified;


                    if (existingAlert is PredictStatusAlert existingPSA && matchingAlert is PredictStatusAlert matchingPSA)
                    {
                        existingPSA.ChangeDetectionResult = matchingPSA.ChangeDetectionResult;
                        existingPSA.SpikeDetectionResult = matchingPSA.SpikeDetectionResult;
                        // Other PredictStatusAlert-specific properties...
                    }
                }
            }

            // Add new objects
            var newAlerts = processorDataObj.PredictStatusAlerts.Where(m => !predictStatusAlerts.Any(e => e.ID == m.ID)).ToList();
            predictStatusAlerts.AddRange(newAlerts);

            // Remove objects not in processorDataObj.MonitorStatusAlerts
            // monitorStatusAlerts.RemoveAll(r => !processorDataObj.MonitorStatusAlerts.Any(m => m.ID == r.ID));

            return processorDataObj;
        }

        public static List<MonitorPingInfo> Build(byte[] processorDataBytes)
        {
            if (processorDataBytes == null) throw new ArgumentNullException(nameof(processorDataBytes));
            ProcessorDataObj? extract = ExtractFromZ<ProcessorDataObj>(processorDataBytes);
            if (extract == null) throw new ArgumentNullException(nameof(extract));
            return (Build(extract));
        }
        public static T? ExtractFromZ<T>(byte[] dataBytes) where T : class
        {
            var json = StringCompressor.Decompress(dataBytes);
            var obj = JsonUtils.GetJsonObjectFromString<T>(json);
            return obj;
        }
        public static T? ExtractFromZString<T>(string dataString) where T : class
        {
            var json = StringCompressor.Decompress(dataString);
            var obj = JsonUtils.GetJsonObjectFromString<T>(json);
            return obj;
        }
        public static List<MonitorPingInfo> Build(ProcessorDataObj processorDataObj)
        {
            if (processorDataObj == null) throw new ArgumentNullException(nameof(processorDataObj));
            var monitorPingInfos = new List<MonitorPingInfo>();
            monitorPingInfos = processorDataObj.MonitorPingInfos;
            foreach (var mon in monitorPingInfos)
            {
                processorDataObj.PingInfos.Where(w => w.MonitorPingInfoID == mon.ID).ToList().ForEach(f => mon.PingInfos.Add(f));
            }
            return monitorPingInfos;
        }
    }
}