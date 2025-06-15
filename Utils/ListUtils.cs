using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using NetworkMonitor.Objects;
using System.Collections.Generic;

namespace NetworkMonitor.Utils
{
    public class ListUtils
    {

     

        /*public static void RemoveNestedMonitorPingInfos(List<MonitorPingInfo> mons){
             foreach (var mon in mons)
                {
                    foreach (var info in mon.PingInfos)
                    {
                        //info.MonitorPingInfo = null;
                    }
                    mon.MonitorStatus.MonitorPingInfo=null;
                }
        }*/

        public static void RemoveNestedMonitorIPs(List<MonitorIP> mons){
            foreach (var mon in mons)
                {
                    if (mon.UserInfo!=null)
                    mon.UserInfo.MonitorIPs = new List<MonitorIP>();
                }
        }

         public static void RemoveNestedMonitorIPs(List<UserInfo> userInfos){
            foreach (var userInfo in userInfos)
                {
                    userInfo.MonitorIPs = new List<MonitorIP>();
                }
        }
    }
}
