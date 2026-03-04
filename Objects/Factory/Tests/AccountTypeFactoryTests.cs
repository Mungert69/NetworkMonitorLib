using System;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Factory;
using Xunit;

namespace NetworkMonitorLib.Tests.Factory;

public class AccountTypeFactoryTests
{
    [Theory]
    [InlineData("Free")]
    [InlineData("Standard")]
    [InlineData("Professional")]
    [InlineData("Enterprise")]
    [InlineData("God")]
    public void GetFunctionNamesForAccountType_IncludesMemoryExpert(string accountType)
    {
        var functions = AccountTypeFactory.GetFunctionNamesForAccountType(accountType);
        Assert.Contains("call_memory_expert", functions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsFunctionNameAllowed_AllowsExecuteQueryWildcard()
    {
        var functions = AccountTypeFactory.GetFunctionNamesForAccountType("Free");

        Assert.True(AccountTypeFactory.IsFunctionNameAllowed(functions, "execute_query"));
        Assert.True(AccountTypeFactory.IsFunctionNameAllowed(functions, "execute_query_memory"));
    }

    [Fact]
    public void CheckUserFuncPermissions_AllowsExecuteQueryMemory_ForGodAccount()
    {
        var user = new UserInfo
        {
            UserID = "u-1",
            AccountType = "God"
        };

        var (allowed, message) = AccountTypeFactory.CheckUserFuncPermissions(user, "execute_query_memory", "https://frontend");

        Assert.True(allowed);
        Assert.Contains("has access", message, StringComparison.OrdinalIgnoreCase);
    }
}
