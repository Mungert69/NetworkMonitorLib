using System;

namespace NetworkMonitor.Objects
{
    public class DataSetObj
    {
        public DataSetObj(){}
        private int dataSetId;
        private DateTime dateStarted;

        public int DataSetId { get => dataSetId; set => dataSetId = value; }
        public DateTime DateStarted { get => dateStarted; set => dateStarted = value; }
    }
}
