namespace NetworkMonitor.Objects
{
    // A List<string> that are EndPointTypes that will used to exclude ping checks in AlertMessageService.
    public class ExcludeEndPointTypeList : System.Collections.Generic.List<string>
    {
        public ExcludeEndPointTypeList()
        {
            this.Add("quantum");
        }
    }

}