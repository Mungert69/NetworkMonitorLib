using NetworkMonitor.Objects;
using System;
using System.Text;

namespace NetworkMonitor.Utils.Helpers;
public class PrintPropertiesAsJson
{
    public static string MergeJsonStrings(string? jsonString1, string? jsonString2)
    {
        if (String.IsNullOrEmpty(jsonString1) && String.IsNullOrEmpty(jsonString2)) return "";
        if (String.IsNullOrEmpty(jsonString1)) return jsonString2 ?? "";
        if (String.IsNullOrEmpty(jsonString2)) return jsonString1 ?? "";
        // Remove the opening "{" from jsonString2 and the closing "}" from jsonString1
        string modifiedJsonString2 = jsonString2.TrimStart('{').TrimEnd('}');
        string modifiedJsonString1 = jsonString1.TrimStart('{').TrimEnd('}');

        // Concatenate the two strings with a comma
        string mergedJson = "{" + modifiedJsonString2 + ", " + modifiedJsonString1 + "}";

        return mergedJson;
    }

     public static string MergeJsonStrings(string? jsonString1, string? jsonString2, string? jsonString3)
    {
        if (string.IsNullOrEmpty(jsonString1) && string.IsNullOrEmpty(jsonString2) && string.IsNullOrEmpty(jsonString3)) return "{}";
        if (string.IsNullOrEmpty(jsonString1)) return MergeJsonStrings(jsonString2, jsonString3);
        if (string.IsNullOrEmpty(jsonString2)) return MergeJsonStrings(jsonString1, jsonString3);
        if (string.IsNullOrEmpty(jsonString3)) return MergeJsonStrings(jsonString1, jsonString2);

        // Remove the opening "{" and closing "}" from each JSON string
        string modifiedJsonString1 = jsonString1.TrimStart('{').TrimEnd('}');
        string modifiedJsonString2 = jsonString2.TrimStart('{').TrimEnd('}');
        string modifiedJsonString3 = jsonString3.TrimStart('{').TrimEnd('}');

        // Concatenate the three strings with commas
        string mergedJson = "{" + modifiedJsonString1
            + (string.IsNullOrEmpty(modifiedJsonString1) ? "" : ", ") + modifiedJsonString2
            + (string.IsNullOrEmpty(modifiedJsonString2) ? "" : ", ") + modifiedJsonString3 + "}";

        return mergedJson;
    }

     public static string PrintMessageIDProperties(string messageID)
    {
        StringBuilder output = new StringBuilder();

        output.Append("{");

        output.Append("\"message_id\" : \"").Append(messageID).Append("\"");

        output.Append("}");

        return output.ToString();
    }

    public static string PrintTSResultObjProperties<TData, SData>(TResultObj<TData, SData> resultObj)
    {
        StringBuilder output = new StringBuilder();

        output.Append("{");

        output.Append("\"message\" : \"").Append(resultObj.Message).Append("\", ");
        output.Append("\"success\" : ").Append(resultObj.Success.ToString().ToLower()).Append(", ");


        // Remove the trailing comma and space
        if (output.Length >= 2 && output.ToString(output.Length - 2, 2) == ", ")
        {
            output.Length -= 2;
        }

        output.Append("}");

        return output.ToString();

    }
    public static string PrintTResultObjProperties<TData>(TResultObj<TData> resultObj)
    {
        StringBuilder output = new StringBuilder();

        output.Append("{");

        output.Append("\"message\" : \"").Append(resultObj.Message).Append("\", ");
        output.Append("\"success\" : ").Append(resultObj.Success.ToString().ToLower()).Append(", ");


        // Remove the trailing comma and space
        if (output.Length >= 2 && output.ToString(output.Length - 2, 2) == ", ")
        {
            output.Length -= 2;
        }

        output.Append("}");

        return output.ToString();

    }
    public static string PrintResultObjProperties(ResultObj resultObj)
    {
        StringBuilder output = new StringBuilder();

        output.Append("{");

        output.Append("\"message\" : \"").Append(resultObj.Message).Append("\", ");
        output.Append("\"success\" : ").Append(resultObj.Success.ToString().ToLower()).Append(", ");


        // Remove the trailing comma and space
        if (output.Length >= 2 && output.ToString(output.Length - 2, 2) == ", ")
        {
            output.Length -= 2;
        }

        output.Append("}");

        return output.ToString();
    }
    public static string PrintUserInfoPropertiesWithDate(UserInfo user, bool isUserLoggedIn, string currentTime, bool detail)
    {
        StringBuilder output = new StringBuilder();

        output.Append("{");

        output.Append("\"current_time\" : \"").Append(currentTime).Append("\", ");
        output.Append("\"logged_in\" : ").Append(isUserLoggedIn.ToString().ToLower()).Append(", ");

        if (isUserLoggedIn) output.Append("\"email\" : \"").Append(user.Email).Append("\", ");


        if (detail && isUserLoggedIn)
        {
            if (!string.IsNullOrEmpty(user.Name)) output.Append("\"name\" : \"").Append(user.Name).Append("\", ");
            output.Append("\"account_type\" : \"").Append(user.AccountType).Append("\", ");
            output.Append("\"email_verified\" : \"").Append(user.Email_verified.ToString().ToLowerInvariant()).Append("\", ");
            output.Append("\"disabled_email_alerts\" : ").Append(user.DisableEmail.ToString().ToLowerInvariant()).Append(", ");
            output.Append("\"host_limit\" : ").Append(user.HostLimit).Append(", ");
            output.Append("\"turbo_llm_tokens\" : ").Append(user.TokensUsed).Append(", ");

        }
        if (output.Length >= 2 && output.ToString(output.Length - 2, 2) == ", ")
        {
            output.Length -= 2;
        }
        output.Append("}");
        return output.ToString();

    }
    public static string PrintUserInfoProperties(UserInfo user, bool isUserLoggedIn, TimeZoneInfo clientTimeZone, bool detail)
    {
        var currentTime = StringUtils.GetFormattedDateTime(DateTime.UtcNow, clientTimeZone);
        return PrintUserInfoPropertiesWithDate(user, isUserLoggedIn, currentTime, detail);
    }

     public static string PrintMonitorPingInfoDateRangeProperties(MonitorPingInfo monitorPingInfo, TimeZoneInfo clientTimeZone)
    {
        StringBuilder output = new StringBuilder();

        output.Append("{");
          StringUtils.AppendFormattedDateTime(output, monitorPingInfo.DateStarted, clientTimeZone, "date_started");
        StringUtils.AppendFormattedDateTime(output, monitorPingInfo.DateEnded, clientTimeZone, "date_ended");
        if (output.Length >= 2 && output.ToString(output.Length - 2, 2) == ", ")
        {
            output.Length -= 2;
        }
        output.Append("}");

        return output.ToString();
    }
    public static string PrintMonitorPingInfoProperties(MonitorPingInfo monitorPingInfo,  bool detail)
    {
        StringBuilder output = new StringBuilder();

        output.Append("{");
        output.Append("\"status_message\" : \"").Append(monitorPingInfo.MessageForUser).Append("\", ");
        output.Append("\"id\" : ").Append(monitorPingInfo.ID).Append(", ");
        output.Append("\"address\" : \"").Append(monitorPingInfo.Address).Append("\", ");
        output.Append("\"endpoint\" : \"").Append(monitorPingInfo.EndPointType).Append("\", ");
        output.Append("\"agent_location\" : \"").Append(monitorPingInfo.AgentLocation).Append("\", "); 
        if (monitorPingInfo.Port != 0) output.Append("\"port\" : ").Append(monitorPingInfo.Port);
       
        if (detail)
        {
            output.Append("\"dataset_id\" : ").Append(monitorPingInfo.DataSetID).Append(", ");
            output.Append("\"status\" : \"").Append(monitorPingInfo.MonitorStatus.Message).Append("\", ");
            output.Append("\"packets_sent\" : ").Append(monitorPingInfo.PacketsSent).Append(", ");
            output.Append("\"packets_received\" : ").Append(monitorPingInfo.PacketsRecieved).Append(", ");
            output.Append("\"packets_lost\" : ").Append(monitorPingInfo.PacketsLost).Append(", ");
            output.Append("\"packets_lost_percentage\" : ").Append(monitorPingInfo.PacketsLostPercentage).Append(", ");
            output.Append("\"alert_sent\" : ").Append(monitorPingInfo.MonitorStatus.AlertSent.ToString().ToLowerInvariant()).Append(", ");
            output.Append("\"alert_flag\" : ").Append(monitorPingInfo.MonitorStatus.AlertFlag.ToString().ToLowerInvariant()).Append(", ");
            output.Append("\"round_trip_time_maximum\" : ").Append(monitorPingInfo.RoundTripTimeMaximum).Append(", ");
            output.Append("\"round_trip_time_average\" : ").Append(monitorPingInfo.RoundTripTimeAverage).Append(", ");
           }

        if (output.Length >= 2 && output.ToString(output.Length - 2, 2) == ", ")
        {
            output.Length -= 2;
        }
        output.Append("}");

        return output.ToString();
    }
    public static string PrintMonitorIPProperties(MonitorIP monitorIP, bool detail)
    {
        StringBuilder output = new StringBuilder();

        output.Append("{");
        output.Append("\"address\" : \"").Append(monitorIP.Address).Append("\", ");
        output.Append("\"id\" : ").Append(monitorIP.ID).Append(", ");
        if (!string.IsNullOrEmpty(monitorIP.EditAuthKey)) output.Append("\"auth_key\" : \"").Append(monitorIP.EditAuthKey).Append("\", ");
        if (detail)
        {
            if (monitorIP.UserID == "default" && !string.IsNullOrEmpty(monitorIP.AddUserEmail) && monitorIP.AddUserEmail != "hidden") output.Append("\"email\" : \"").Append(monitorIP.AddUserEmail).Append("\", ");
            output.Append("\"endpoint\" : \"").Append(monitorIP.EndPointType).Append("\", ");
            if (monitorIP.Port != 0) output.Append("\"port\" : ").Append(monitorIP.Port).Append(", ");
            output.Append("\"timeout\" : ").Append(monitorIP.Timeout).Append(", ");
            if (!monitorIP.Enabled) output.Append("\"enabled\" : ").Append(monitorIP.Enabled.ToString().ToLowerInvariant()).Append(", ");
            output.Append("\"agent_location\" : \"").Append(monitorIP.AgentLocation).Append("\", ");
            if (!string.IsNullOrEmpty(monitorIP.Username)) output.Append("\"username\" : \"").Append(monitorIP.Username).Append("\", ");
            if (!string.IsNullOrEmpty(monitorIP.Password)) output.Append("\"password\" : \"").Append(MaskSecret(monitorIP.Password)).Append("\", ");
            if (!string.IsNullOrEmpty(monitorIP.Args)) output.Append("\"args\" : \"").Append(monitorIP.Args).Append("\", ");

        }
        if (output.Length >= 2 && output.ToString(output.Length - 2, 2) == ", ")
        {
            output.Length -= 2;
        }
        output.Append("}");
        return output.ToString();
    }

    private static string MaskSecret(string value)
    {
        return string.IsNullOrEmpty(value) ? "" : "****";
    }

    public static string PrintAgentLocation(string agentLocation)
    {
        StringBuilder output = new StringBuilder();

        output.Append("{");
        output.Append("\"agent_location\" : \"").Append(agentLocation).Append("\", ");
        output.Append("}");
        return output.ToString();
    }

    public static string PrintAgentProperties(ProcessorObj processorObj, bool detail, string llmRunnerType)
    {

        StringBuilder output = new StringBuilder();

        output.Append("{");
        output.Append("\"agent_location\" : \"").Append(processorObj.Location).Append("\", ");

        if (detail)
        {
            output.Append("\"available_functions\" : ").Append(processorObj.AvailableFunctionsJson(llmRunnerType)).Append(", ");
            output.Append("\"agent_id\" : \"").Append(processorObj.AppID).Append("\", ");
            output.Append("\"enabled\" : ").Append(processorObj.IsEnabled.ToString().ToLowerInvariant()).Append(", ");
            output.Append("\"load\" : ").Append(processorObj.Load).Append(", ");
            output.Append("\"max_load\" : \"").Append(processorObj.MaxLoad).Append("\", ");
            output.Append("\"send_agent_down_alert\" : ").Append(processorObj.SendAgentDownAlert.ToString().ToLowerInvariant()).Append(", ");
            output.Append("\"disabled_endpoints\" : ").Append(processorObj.DisabledEndPointTypesJson).Append(", ");
            output.Append("\"enabled_endpoints\" : ").Append(processorObj.EnabledEndPointTypesJson).Append(", ");
        
        }
        if (output.Length >= 2 && output.ToString(output.Length - 2, 2) == ", ")
        {
            output.Length -= 2;
        }
        output.Append("}");
        return output.ToString();
    }

}
