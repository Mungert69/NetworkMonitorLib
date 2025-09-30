using System;
using System.Collections.Generic;
using NetworkMonitor.Objects;
using NetworkMonitor.Utils.Helpers;
using NetworkMonitor.Utils;
using Xunit;

namespace NetworkMonitorLib.Tests.Helpers;

public class PrintPropertiesAsJsonTests
{
    [Fact]
    public void MergeJsonStrings_TwoInputs_CombineInOrder()
    {
        var merged = PrintPropertiesAsJson.MergeJsonStrings("{\"a\":1}", "{\"b\":2}");

        Assert.Equal("{\"b\":2, \"a\":1}", merged);
    }

    [Fact]
    public void MergeJsonStrings_ThreeInputs_SkipsEmpty()
    {
        var merged = PrintPropertiesAsJson.MergeJsonStrings("{\"a\":1}", null, "{\"c\":3}");

        Assert.Equal("{\"c\":3, \"a\":1}", merged);
    }

    [Fact]
    public void PrintMessageIdProperties_FormatsCorrectly()
    {
        var json = PrintPropertiesAsJson.PrintMessageIDProperties("abc-123");

        Assert.Equal("{\"message_id\" : \"abc-123\"}", json);
    }

    [Fact]
    public void PrintResultObjProperties_SerializesMinimalFields()
    {
        var resultObj = new ResultObj { Message = "ok", Success = true };

        var json = PrintPropertiesAsJson.PrintResultObjProperties(resultObj);

        Assert.Equal("{\"message\" : \"ok\", \"success\" : true}", json);
    }

    [Fact]
    public void PrintUserInfoPropertiesWithDate_IncludesOptionalFields()
    {
        var user = new UserInfo
        {
            Email = "user@example.com",
            Name = "Test",
            AccountType = "Premium",
            Email_verified = true,
            DisableEmail = false,
            HostLimit = 5,
            TokensUsed = 42
        };

        var json = PrintPropertiesAsJson.PrintUserInfoPropertiesWithDate(user, true, "2024-01-01T00:00:00", detail: true);

        Assert.Equal("{\"current_time\" : \"2024-01-01T00:00:00\", \"logged_in\" : true, \"email\" : \"user@example.com\", \"name\" : \"Test\", \"account_type\" : \"Premium\", \"email_verified\" : \"true\", \"disabled_email_alerts\" : false, \"host_limit\" : 5, \"turbo_llm_tokens\" : 42}", json);
    }

    [Fact]
    public void PrintMonitorPingInfoDateRangeProperties_FormatsDates()
    {
        var info = new MonitorPingInfo
        {
            DateStarted = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            DateEnded = new DateTime(2024, 1, 1, 12, 5, 0, DateTimeKind.Utc)
        };
        var tz = TimeZoneInfo.Utc;

        var json = PrintPropertiesAsJson.PrintMonitorPingInfoDateRangeProperties(info, tz);

        Assert.Equal("{\"date_started\" : \"2024-01-01T12:00:00\", \"date_ended\" : \"2024-01-01T12:05:00\"}", json);
    }

    [Fact]
    public void PrintMonitorPingInfoProperties_IncludesDetailFields()
    {
        var info = new MonitorPingInfo
        {
            MessageForUser = "All good",
            ID = 10,
            Address = "example.com",
            EndPointType = "http",
            AgentLocation = "Scanner - EU",
            Port = 80,
            DataSetID = 7,
            PacketsSent = 4,
            PacketsRecieved = 4,
            PacketsLost = 0,
            PacketsLostPercentage = 0,
            RoundTripTimeAverage = 100,
            RoundTripTimeMaximum = 150
        };
        info.MonitorStatus.Message = "Online";
        info.MonitorStatus.AlertSent = true;
        info.MonitorStatus.AlertFlag = false;

        var json = PrintPropertiesAsJson.PrintMonitorPingInfoProperties(info, detail: true);

        Assert.Contains("\"status_message\" : \"All good\"", json);
        Assert.Contains("\"dataset_id\" : 7", json);
        Assert.Contains("\"round_trip_time_average\" : 100", json);
    }

    [Fact]
    public void PrintMonitorIPProperties_HandlesDetail()
    {
        var monitorIp = new MonitorIP
        {
            Address = "example.com",
            ID = 5,
            EditAuthKey = "auth",
            EndPointType = "http",
            Port = 443,
            Timeout = 1000,
            Enabled = false,
            AgentLocation = "Scanner - EU",
            UserID = "default",
            AddUserEmail = "owner@example.com"
        };

        var json = PrintPropertiesAsJson.PrintMonitorIPProperties(monitorIp, detail: true);

        Assert.Contains("\"address\" : \"example.com\"", json);
        Assert.Contains("\"auth_key\" : \"auth\"", json);
        Assert.Contains("\"enabled\" : false", json);
    }

    [Fact]
    public void PrintAgentProperties_IncludesAvailableFunctions()
    {
        var processor = new ProcessorObj
        {
            Location = "Scanner - EU",
            AppID = "agent-1",
            IsEnabled = true,
            Load = 10,
            MaxLoad = 100,
            SendAgentDownAlert = true
        };
        processor.DisabledCommands = new List<string> { "nmap" };

        var json = PrintPropertiesAsJson.PrintAgentProperties(processor, detail: true, llmRunnerType: "TurboLLM");

        Assert.Contains("\"agent_location\" : \"Scanner - EU\"", json);
        Assert.Contains("\"available_functions\" : ", json);
        Assert.DoesNotContain("call_security_expert", json);
        Assert.Contains("call_penetration_expert", json);
    }

    [Fact]
    public void PrintAgentLocation_WrapsLocation()
    {
        var json = PrintPropertiesAsJson.PrintAgentLocation("Scanner - EU");

        Assert.Equal("{\"agent_location\" : \"Scanner - EU\", }", json);
    }
}
