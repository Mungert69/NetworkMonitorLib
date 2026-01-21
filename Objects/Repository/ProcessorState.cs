using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Objects.Repository
{
    public interface IProcessorState
    {

        List<ProcessorObj> EnabledProcessorList(bool showAuthKey);
        ProcessorObj? GetProcessorFromID(string appID, bool showAuthKey);
        ProcessorObj? GetProcessorFromLocation(string agentLocation, bool showAuthKey);
        List<ProcessorObj> GetProcessorsByLocations(IEnumerable<string> agentLocations, bool showAuthKey);
        bool IsProcessorWithID(string appID);
        bool IsFilteredSystemProcessorWithID(string appID);
        bool IsFilteredUserAndSystemProcessorWithID(string userId, string appID);
        bool SetProcessorObj(ProcessorObj processorObj);
        bool SetProcessorObjIsReportSent(string appID, bool isReportSent);
        bool SetProcessorObjIsReady(string appID, bool isReady);
        void SetAllProcessorObjsIsReportSent(bool isReportSent);
        List<ProcessorObj> GetProcessorListAll(bool showAuthKey);
        void ResetConcurrentProcessorList(List<ProcessorObj> processorObjs);
        ConcurrentBag<ProcessorObj> ConcurrentProcessorList { get; set; }
        //List<ProcessorObj> ProcessorList { get; }
        List<MonitorIP> MonitorIPs { get; set; }
        List<ProcessorObj> FilteredUserProcessorList(string userId, bool showAuthKey);
        List<ProcessorObj> FilteredUserAndSystemProcessorList(string userId, bool showAuthKey);
        string GetNextProcessorAppIDThatCanRunCommand(string command, string agentLocation, string userId);
        ResultObj GetProcessorAppIDIfCanRunCommand(string command, string agentLocation, string userId);
        List<ProcessorObj> UserProcessorListAll(string userId, bool showAuthKey);
        List<ProcessorObj> EnabledSystemProcessorList(bool showAuthKey);
        List<ProcessorObj> EnabledSendAlertProcessorList(bool showAuthKey);
        List<ProcessorObj> FilteredSystemProcessorList(bool showAuthKey);
        bool IsOverMaxload(string appID);
        void SetAllLoads();
        void SetLoads(List<MonitorIP> beforeMonitorIPs, List<MonitorIP> afterMonitorIPs);
        void RemoveLoad(string appID);
        void AddLoad(string appID);
        bool IsEndPointAvailable(string endPointType, string appID);
        string GetNextProcessorAppID(string endPointType);
        event Func<string, ResultObj>? OnAppIDAdded;
        ResultObj AddAppIDStateChange(string value);
        bool HasUserGotProcessor(string userId);
        string LocationFromID(string appID);
        string IDFromLocation(string agentLocation);
        string AuthKeyFromID(string appID);
    }
    public class ProcessorState : IProcessorState
    {
        private ConcurrentBag<ProcessorObj> _processorList = new ConcurrentBag<ProcessorObj>();
        private List<MonitorIP> _monitorIPs = new List<MonitorIP>();

        public event Func<string, ResultObj>? OnAppIDAdded;
        public List<ProcessorObj> EnabledProcessorList(bool showAuthKey) { return _processorList.Where(w => w.IsEnabled).Select(processor => new ProcessorObj(processor, showAuthKey)).ToList(); }

        public ProcessorObj? GetProcessorFromID(string appID, bool showAuthKey) { return _processorList.Where(w => w.AppID == appID).Select(processor => new ProcessorObj(processor, showAuthKey)).FirstOrDefault(); }
        public ProcessorObj? GetProcessorFromLocation(string agentLocation, bool showAuthKey)
        {
            if (string.IsNullOrWhiteSpace(agentLocation))
            {
                return null;
            }
            return _processorList
                .Where(w => string.Equals(w.Location, agentLocation, StringComparison.OrdinalIgnoreCase))
                .Select(processor => new ProcessorObj(processor, showAuthKey))
                .FirstOrDefault();
        }
        public List<ProcessorObj> GetProcessorsByLocations(IEnumerable<string> agentLocations, bool showAuthKey)
        {
            var locationSet = new HashSet<string>(
                agentLocations?.Where(location => !string.IsNullOrWhiteSpace(location)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            if (locationSet.Count == 0)
            {
                return new List<ProcessorObj>();
            }
            return _processorList
                .Where(w => !string.IsNullOrWhiteSpace(w.Location) && locationSet.Contains(w.Location))
                .Select(processor => new ProcessorObj(processor, showAuthKey))
                .ToList();
        }
        public bool IsProcessorWithID(string appID) { return _processorList.Any(w => w.AppID == appID); }

        public List<ProcessorObj> GetProcessorListAll(bool showAuthKey) { return _processorList.Select(processor => new ProcessorObj(processor, showAuthKey)).ToList(); }


        public bool SetProcessorObj(ProcessorObj processorObj)
        {
            bool isExists = _processorList.Any(a => a.AppID == processorObj.AppID);
            if (!isExists) return false;
            ProcessorObj? setProcessorObj = _processorList.Where(w => w.AppID == processorObj.AppID).FirstOrDefault();
            if (setProcessorObj != null) setProcessorObj.SetAllFields(processorObj);
            return true;
        }
        public bool SetProcessorObjIsReportSent(string appID, bool isReportSent)
        {
            bool isExists = _processorList.Any(a => a.AppID == appID);
            if (!isExists) return false;
            ProcessorObj? setProcessorObj = _processorList.Where(w => w.AppID == appID).FirstOrDefault();
            if (setProcessorObj != null) setProcessorObj.IsReportSent = isReportSent;
            return true;

        }
        public bool SetProcessorObjIsReady(string appID, bool isReady)
        {
            bool isExists = _processorList.Any(a => a.AppID == appID);
            if (!isExists) return false;
            ProcessorObj? setProcessorObj = _processorList.Where(w => w.AppID == appID).FirstOrDefault();
            if (setProcessorObj != null) setProcessorObj.IsReady = isReady;
            return true;

        }

        public void SetAllProcessorObjsIsReportSent(bool isReportSent)
        {
            foreach (var processor in _processorList)
            {
                processor.IsReportSent = isReportSent;
            }

        }
        public string LocationFromID(string appID)
        {
            var processorObj = _processorList.Where(w => w.AppID == appID).FirstOrDefault();
            if (processorObj != null) return processorObj.Location;
            return $" (Agent not found with agentID {appID}) ";
        }

           public string IDFromLocation(string agentLocation)
        {
            var selectedProcessor = _processorList
                 .FirstOrDefault(p => p.Location == agentLocation);
            if (selectedProcessor != null && !string.IsNullOrEmpty(selectedProcessor.AppID)) return selectedProcessor.AppID;
            else return "";
        }
        public string AuthKeyFromID(string appID)
        {
            var processorObj = _processorList.Where(w => w.AppID == appID).FirstOrDefault();
            if (processorObj != null) return processorObj.AuthKey;
            return $" (Agent not found with agentID {appID}) ";
        }

        public ResultObj AddAppIDStateChange(string value)
        {
            if (OnAppIDAdded != null)
            {
                return OnAppIDAdded(value);
            }
            return new ResultObj() { Message = " No OnAppIDAdded Func defined .", Success = true };

        }

        public List<ProcessorObj> EnabledSystemProcessorList(bool showAuthKey)
        {
            return _processorList
                .Where(w => w.IsEnabled && !w.IsPrivate)
                .Select(processor => new ProcessorObj(processor, showAuthKey)) // Use copy constructor
                .ToList();
        }
        public List<ProcessorObj> EnabledSendAlertProcessorList(bool showAuthKey)
        {
            return _processorList
                .Where(w => w.IsEnabled && w.SendAgentDownAlert)
                .Select(processor => new ProcessorObj(processor, showAuthKey)) // Use copy constructor
                .ToList();
        }
        public bool IsFilteredSystemProcessorWithID(string appID) { return _processorList.Any(w => w.AppID == appID && w.Load < w.MaxLoad && w.IsEnabled && !w.IsPrivate); }

        public List<ProcessorObj> FilteredSystemProcessorList(bool showAuthKey)
        {
            return _processorList
                .Where(w => w.Load < w.MaxLoad && w.IsEnabled && !w.IsPrivate)
                .Select(processor => new ProcessorObj(processor, showAuthKey)) // Use copy constructor
                .ToList();
        }

        public List<ProcessorObj> FilteredUserProcessorList(string userId, bool showAuthKey) { return _processorList.Where(w => w.Load < w.MaxLoad && w.IsEnabled && w.Owner == userId).Select(processor => new ProcessorObj(processor, showAuthKey)).ToList(); }
        public List<ProcessorObj> UserProcessorListAll(string userId, bool showAuthKey) { return _processorList.Where(w => w.Owner == userId).Select(processor => new ProcessorObj(processor, showAuthKey)).ToList(); }
        public bool IsFilteredUserAndSystemProcessorWithID(string userId, string appID)
        {
            return FilteredUserAndSystemProcessorList(userId, false).Any(a => a.AppID == appID);
        }
        public List<ProcessorObj> FilteredUserAndSystemProcessorList(string userId, bool showAuthKey)
        {
            List<ProcessorObj> userProcessorList = _processorList
                .Where(w => w.Load < w.MaxLoad && w.IsEnabled && w.Owner == userId)
                .Select(processor => new ProcessorObj(processor, showAuthKey)) // Use copy constructor
                .ToList();

            if (userProcessorList == null) userProcessorList = new List<ProcessorObj>();

            userProcessorList.AddRange(FilteredSystemProcessorList(showAuthKey));

            return userProcessorList;
        }

        public ResultObj GetProcessorAppIDIfCanRunCommand(string command, string agentLocation, string userId)
        {
            var result = new ResultObj();
            // Check if user selected a processor based on agent_location
            if (agentLocation == "") return new ResultObj { Success = false, Message = $" Error : You must set agent_location" };
            var selectedProcessor = _processorList
                .FirstOrDefault(p => p.Location == agentLocation);
            if (selectedProcessor == null) return new ResultObj { Success = false, Message = $" Error : There is no agent with agent_location {agentLocation} . You need to check what agents you have available before trying again." };
            if (selectedProcessor.Owner != userId) return new ResultObj { Success = false, Message = $" Error : You do not own this agent. Consider installing your own agent. Follow the instructions at {AppConstants.FrontendUrl}/download to download and install your own agent." };
            if (!selectedProcessor.IsEnabled) return new ResultObj { Success = false, Message = $" Error : The agent is not enabled. If you are unabled to re-enable the agent consider installing a new agent. Follow the instructions at {AppConstants.FrontendUrl}/download to download and install your own agent." };
            if (selectedProcessor.Load > selectedProcessor.MaxLoad) return new ResultObj { Success = false, Message = $" Error : The agent is over max load. Consider installing a new agent to share the load. Follow the instructions at {AppConstants.FrontendUrl}/download to download and install your own agent." };
            if (!selectedProcessor.IsPrivate) return new ResultObj { Success = false, Message = $" Error : The agent is a system agent and is not avaiable for this command. Consider installing your own agent. Follow the instructions at {AppConstants.FrontendUrl}/download to download and install your own agent." };
            if (selectedProcessor.DisabledCommands.Contains(command)) return new ResultObj { Success = false, Message = $" Error : The agent is unabled to run the {command} command. Consider installing your own agent with the {command} command enabled. Follow the instructions at {AppConstants.FrontendUrl}/download to download and install your own agent." };
            return new ResultObj { Success = true, Message = selectedProcessor.AppID };
        }

        public string GetNextProcessorAppIDThatCanRunCommand(string command, string agentLocation, string userId)
        {
            // Check if user selected a processor based on agent_location
            var selectedProcessor = _processorList
                .FirstOrDefault(p => p.Location == agentLocation && p.IsEnabled && p.Load < p.MaxLoad);

            if (selectedProcessor != null)
            {
                // Check if the selected processor is a system processor or owned by the user
                if (!selectedProcessor.IsPrivate || selectedProcessor.Owner == userId)
                {
                    // Check if the command is not disabled for the selected processor
                    if (selectedProcessor.DisabledCommands == null || !selectedProcessor.DisabledCommands.Contains(command))
                    {
                        //selectedProcessor.Load++;
                        return selectedProcessor.AppID;
                    }
                }
            }

            // If the selected processor cannot be used, find the next available system processor
            var availableProcessors = _processorList
                .Where(p => p.IsEnabled && !p.IsPrivate && p.Load < p.MaxLoad &&
                            (p.DisabledCommands == null || !p.DisabledCommands.Contains(command)))
                .OrderBy(p => p.Load)
                .ToList();

            if (availableProcessors.Count == 0)
            {
                return "Error"; // No processor available
            }

            var nextAvailableProcessor = availableProcessors.First();
            //nextAvailableProcessor.Load++;
            return nextAvailableProcessor.AppID;
        }

        public ConcurrentBag<ProcessorObj> ConcurrentProcessorList { get => _processorList; set => _processorList = value; }
        //public List<ProcessorObj> ProcessorList { get => _processorList.ToList(); }
        public void ResetConcurrentProcessorList(List<ProcessorObj> processorObjs)
        {
            _processorList.Clear();
            // Add all processors from received list
            foreach (var processorObj in processorObjs)
            {
                _processorList.Add(processorObj);
            }

        }

        public List<MonitorIP> MonitorIPs { get => _monitorIPs; set => _monitorIPs = value; }

        public bool HasUserGotProcessor(string userId)
        {
            return _processorList.Any(w => w.Owner == userId);
        }
        public void SetAllLoads()
        {
            _processorList.ToList().ForEach(p =>
            {
                p.Load = _monitorIPs.Count(c => c.AppID == p.AppID);
            });
        }

        public void SetLoads(List<MonitorIP> beforeMonitorIPs, List<MonitorIP> afterMonitorIPs)
        {

            _processorList.ToList().ForEach(p =>
            {
                p.Load += afterMonitorIPs.Count(w => w.AppID == p.AppID) - beforeMonitorIPs.Count(w => w.AppID == p.AppID);
            });
        }

        public void RemoveLoad(string appID)
        {
            var processorObj = _processorList.Where(w => w.AppID == appID).FirstOrDefault();
            if (processorObj != null && processorObj.Load > 0)
            {
                processorObj.Load--;
            }
        }
        public void AddLoad(string appID)
        {
            var processorObj = _processorList.Where(w => w.AppID == appID).FirstOrDefault();
            if (processorObj != null)
            {
                processorObj.Load++;
            }
        }

        public bool IsEndPointAvailable(string endPointType, string appID)
        {
            var processorObj = _processorList.Where(w => (w.DisabledEndPointTypes == null || !w.DisabledEndPointTypes.Contains(endPointType)) && w.AppID == appID).FirstOrDefault();
            if (processorObj == null) return false;
            return true;
        }

        public bool IsOverMaxload(string appID)
        {
            var processorObj = _processorList.Where(w => w.AppID == appID && w.Load > w.MaxLoad).FirstOrDefault();
            if (processorObj == null) return false;
            return true;
        }
        public string GetNextProcessorAppID(string endPointType)
        {
            var availableProcessors = _processorList.Where(o => o.IsEnabled && !o.IsPrivate && o.Load < o.MaxLoad && (o.DisabledEndPointTypes == null || !o.DisabledEndPointTypes.Contains(endPointType))).ToList();

            if (availableProcessors.Count == 0)
            {
                return "0";
            }

            var processorObj = availableProcessors.OrderBy(o => o.Load).First();
            processorObj.Load++;
            return processorObj.AppID;
        }



    }
}
