using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
using System.Text.Json;
using NetworkMonitor.Utils;
using NetworkMonitor.Objects.Factory;

namespace NetworkMonitor.Objects
{
    public class ProcessorObj
    {
        public ProcessorObj() { }

        public ProcessorObj(ProcessorObj other, bool showAuthKey)
        {
            ID = other.ID;
            AppID = other.AppID;
            Location = other.Location;
            Load = other.Load;
            MaxLoad = other.MaxLoad;
            IsPrivate = other.IsPrivate;
            IsEnabled = other.IsEnabled;
            DateCreated = other.DateCreated;
            ScheduleStr = other.ScheduleStr;
            SendAgentDownAlert = other.SendAgentDownAlert;
            Owner = other.Owner;
            LastAccessDate = other.LastAccessDate;
            RabbitHost = other.RabbitHost;
            RabbitPort=other.RabbitPort;
            DisabledEndPointTypes = new List<string>(other.DisabledEndPointTypes);
            DisabledCommands = new List<string>(other.DisabledCommands);
            IsReady = other.IsReady;
            IsReportSent = other.IsReportSent;
            // Set AuthKey based on the showAuthKey parameter
            AuthKey = showAuthKey ? other.AuthKey : "hidden" ;
        }
public void SetAllFields(ProcessorObj other)
{
    ID = other.ID;
    AppID = other.AppID;
    Location = other.Location;
    Load = other.Load;
    MaxLoad = other.MaxLoad;
    IsPrivate = other.IsPrivate;
    IsEnabled = other.IsEnabled;
    DateCreated = other.DateCreated;
    ScheduleStr = other.ScheduleStr;
    SendAgentDownAlert = other.SendAgentDownAlert;
    Owner = other.Owner;
    LastAccessDate = other.LastAccessDate;
    RabbitHost = other.RabbitHost;
    RabbitPort=other.RabbitPort;
    DisabledEndPointTypes = new List<string>(other.DisabledEndPointTypes);
   DisabledCommands = new List<string>(other.DisabledCommands);
    AuthKey =  other.AuthKey;
    IsReady = other.IsReady;
    IsReportSent = other.IsReportSent;
}

        private int _iD;
        private string _appID = "";
        private string _location = "";
        private int _load;
        private bool _isPrivate = false;
        private bool _isEnabled = true;
        private DateTime _dateCreated;
        private string _scheduleStr = "";
        private bool _sendAgentDownAlert;

        private int _maxLoad = 1500;

        private string _authKey = "";
        private string _owner = "";
        private DateTime _lastAccessDate;
        private string _rabbitHost;
        private ushort _rabbitPort=55671;


        /// <summary>
        /// A json object the contains the type of end points this agent can not monitor.
        /// </summary>
        public string DisabledEndPointTypesJson
        {
            get => JsonUtils.WriteJsonObjectToString<List<string>>(_disabledEndPointTypes);
            set
            {
                if (value == null)
                {

                }
                else
                {
                    try
                    {
                        var types = JsonUtils.GetJsonObjectFromString<List<string>>(value);
                        if (types != null)
                            _disabledEndPointTypes = types;
                        else _disabledEndPointTypes = new List<string>();
                    }
                    catch
                    {
                        _disabledEndPointTypes = new List<string>();
                    }
                }
            }
        }

          public string EnabledEndPointTypesJson
        {
            get => JsonUtils.WriteJsonObjectToString<List<string>>(EnabledEndPointTypes);
           
        }



        [NotMapped]
        public List<string> DisabledEndPointTypes
        {
            get => _disabledEndPointTypes;
            set => _disabledEndPointTypes = value;
        }
        private List<string> _disabledEndPointTypes = new List<string>();

        [NotMapped]
        public List<string> EnabledEndPointTypes
        {
            get => EndPointTypeFactory.GetEnabledEndPoints(_disabledEndPointTypes);
        }
     
        /// <summary>
        /// A json object the contains the commands that are disabled on this agent.
        /// </summary>
        public string DisabledCommandsJson
        {
            get => JsonUtils.WriteJsonObjectToString<List<string>>(_disabledCommands);
            set
            {
                if (value == null)
                {

                }
                else
                {
                    try
                    {
                        var commands = JsonUtils.GetJsonObjectFromString<List<string>>(value);
                        if (commands != null)
                            _disabledCommands = commands;
                        else _disabledCommands = new List<string>();
                    }
                    catch
                    {
                        _disabledCommands = new List<string>();
                    }
                }
            }
        }


        [NotMapped]
        public List<string> DisabledCommands
        {
            get => _disabledCommands;
            set => _disabledCommands = value;
        }
        private List<string> _disabledCommands = new List<string>();

        public List<string> AvailableFunctions(string llmRunnerType)
        {
            var functionCommandMap=AccountTypeFactory.GetFunctionCommandMap(llmRunnerType );
           
                // Get available functions based on disabled commands
                return functionCommandMap
                    .Where(fc => !_disabledCommands.Contains(fc.Value))
                    .Select(fc => fc.Key)
                    .ToList();
            
        }

        /// <summary>
        /// A JSON object that contains the available functions for this agent.
        /// </summary>
  
        public string AvailableFunctionsJson(string llmRunnerType)
        {
            return JsonUtils.WriteJsonObjectToString<List<string>>(AvailableFunctions(llmRunnerType));
        }
        /// <summary>
        /// The Agent ID (AppID)
        /// </summary>
        public string AppID { get => _appID; set => _appID = value; }
        /// <summary>
        /// The location of this Agent. For internet agents location is City - Country . For local agents the format is user mail address - agentid .
        /// </summary>
        public string Location { get => _location; set => _location = value; }
        /// <summary>
        /// The Load or number of hosts monitored by this Agent.
        /// </summary>
        public int Load { get => _load; set => _load = value; }
        /// <summary>
        /// The maximum number of hosts this agent can monitor.
        /// </summary>
        public int MaxLoad { get => _maxLoad; set => _maxLoad = value; }
        /// <summary>
        /// Is the agent at MaxLoad
        /// </summary>
        public bool IsAtMaxLoad { get => _load > _maxLoad; }
        /// <summary>
        /// Is this agent a local agent.
        /// </summary>
        public bool IsPrivate { get => _isPrivate; set => _isPrivate = value; }

        /// <summary>
        /// The schedule of the agent . Not used yet.
        /// </summary>
        public string ScheduleStr { get => _scheduleStr; set => _scheduleStr = value; }
        /// <summary>
        /// The database Primary Key for the agent.
        /// </summary>
        [Key]
        public int ID { get => _iD; set => _iD = value; }
        /// <summary>
        /// Is this agent enabled
        /// </summary>
        public bool IsEnabled { get => _isEnabled; set => _isEnabled = value; }
        /// <summary>
        /// The date the agent was created.
        /// </summary>
        public DateTime DateCreated { get => _dateCreated; set => _dateCreated = value; }
        /// <summary>
        /// Is the agent ready to monitor.
        /// </summary>
        [NotMapped]
        public bool IsReady { get; set; } = true;
        /// <summary>
        /// Has a report been sent indicating this agent is down.
        /// </summary>
        [NotMapped]
        public bool IsReportSent { get; set; } = false;
        /// <summary>
        /// The AuthKey used by this agent.
        /// </summary>
        public string AuthKey { get => _authKey; set => _authKey = value; }
        /// <summary>
        /// The owner of the agent for private agents this is the UserInfo.UserID.
        /// </summary>
        public string Owner { get => _owner; set => _owner = value; }
        public DateTime LastAccessDate { get => _lastAccessDate; set => _lastAccessDate = value; }
        public bool SendAgentDownAlert { get => _sendAgentDownAlert; set => _sendAgentDownAlert = value; }
        public string RabbitHost { get => _rabbitHost; set => _rabbitHost = value; }
        public ushort RabbitPort { get => _rabbitPort; set => _rabbitPort = value; }
    }
}