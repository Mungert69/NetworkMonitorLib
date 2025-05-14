
using NetworkMonitor.Objects;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace NetworkMonitor.Objects
{
    public class SentUserData
    {
        public SentUserData(){}
        private UserInfo? _user = new UserInfo();
        private int? _dataSetId;
        private int? _monitorPingInfoID;
        private int? _monitorIPID;
        private string? _address;
        private string? _prompt;

        /// <summary>
        /// This field should not be used for api calls.
        /// </summary>

        public UserInfo? User { get => _user; set => _user = value; }
        /// <summary>
        /// DataSetIs for selecting MonitorPingInfos. Dont set this if I date range is set. DataSetID=0 is the current running monitor data set.
        /// </summary>
        public int? DataSetId { get => _dataSetId; set => _dataSetId = value; }
        /// <summary>
        ///  MonitorPingInfo.ID
        /// </summary>
        public int? MonitorPingInfoID { get => _monitorPingInfoID; set => _monitorPingInfoID = value; }
        /// <summary>
        /// Host config data is contained in MonitorIP. This is the ID field for a MonitorIP.
        /// </summary>
        public int? MonitorIPID { get => _monitorIPID; set => _monitorIPID = value; }

      
     
        /// <summary>
        /// The address of the host
        /// </summary>
        
        public string? Address { get => _address; set => _address = value; }
           ///
        /// This field is Required .The prompt that was entered by the user. The backend will use this to assist in creating a useful reponse for the user.
        ///
        public string? Prompt{get => _prompt; set => _prompt = value;}
      
        public string ValidateParameters(bool isDataSetId, bool isMonitorPingInfoID, bool isMonitorIPID, bool isHostAddress)
        {
            var message="";
            if (isDataSetId && _dataSetId==null){
                message+=" DataSetId is required for this query. ";
            }
            if (isMonitorPingInfoID && _monitorPingInfoID==null){
                message+=" MonitorPingInfoID is required for this query. ";
            }
            if (isMonitorIPID && _monitorIPID==null) {
                message+=" MonitorIPID is required for this query. ";
            }
            if (isHostAddress && _address==null){
                message+=" HostAddress is required for this query. ";
            }
          return message;
           
        }
    }
}
