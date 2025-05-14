
namespace NetworkMonitor.Objects.ServiceMessage
{
    public class MonitorMLCheckObj
    {
        public MonitorMLCheckObj(){}
        private int _monitorIPID;
        private int _dataSetID;
        private DateTime _dateStart;
        private DateTime _dateEnd;

        public int MonitorIPID { get => _monitorIPID; set => _monitorIPID = value; }
        public DateTime DateStart { get => _dateStart; set => _dateStart = value; }
        public DateTime DateEnd { get => _dateEnd; set => _dateEnd = value; }
        public int DataSetID { get => _dataSetID; set => _dataSetID = value; }
    }
}