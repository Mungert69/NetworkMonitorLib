using System.Collections.Generic;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Utils
{

    public class PingInfoComparer : IEqualityComparer<PingInfo>
    {
        public bool Equals(PingInfo? x, PingInfo? y)
        {
            //First check if both object reference are equal then return true
            if(object.ReferenceEquals(x, y))
            {
                return true;
            }

            //If either one of the object refernce is null, return false
            if (object.ReferenceEquals(x,null) || object.ReferenceEquals(y, null))
            {
                return false;
            }

            //Comparing all the properties one by one
            return x.DateSentInt == y.DateSentInt ;
        }

        public int GetHashCode(PingInfo obj)
        {
            return obj.DateSentInt.GetHashCode() ;
        }
    }
}
