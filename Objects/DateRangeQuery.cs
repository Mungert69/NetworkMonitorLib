 namespace NetworkMonitor.Objects;
 /// <summary>
        /// Query parameters for fetching PingInfo data by date range and MonitorPingInfo.
        /// </summary>
        public class DateRangeQuery
        {
            /// <summary>
            /// MonitorIPID or Host ID to filter PingInfos by.
            /// </summary>
            public int? MonitorIPID { get; set; }
            /// <summary>
            /// MonitorPingInfoID to filter PingInfos by.
            /// </summary>
            public int? MonitorPingInfoID { get; set; }

            /// <summary>
            /// Address to filter PingInfos by.
            /// </summary>
            public string? Address { get; set; }
            /// <summary>
            /// Start date for the range filter. This is assumed to be UTC.
            /// Will be converted to integer for querying.
            /// </summary>
            public DateTime? StartDate { get; set; }

            /// <summary>
            /// End date for the range filter. This is assumed to be UTC.
            /// Will be converted to integer for querying. 
            /// </summary>
            public DateTime? EndDate { get; set; }

            /// <summary>
            /// The user making the query.
            /// </summary>
            public UserInfo? User { get; set; }
            ///
            /// This field is required. The prompt that was entered by the user. The backend will use this to assist in creating a useful reponse for the user.
            ///
            public string? Prompt { get; set; }

        }
