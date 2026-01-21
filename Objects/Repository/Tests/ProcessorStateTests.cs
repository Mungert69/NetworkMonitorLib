using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using Xunit;

namespace NetworkMonitor.Objects.Repository.Tests;

public class ProcessorStateTests
{
    private static ProcessorState CreateState(IEnumerable<ProcessorObj> processors)
    {
        var state = new ProcessorState();
        state.ResetConcurrentProcessorList(processors.ToList());
        return state;
    }

    [Fact]
    public void GetProcessorFromLocation_MatchesCaseInsensitive()
    {
        var state = CreateState(new[]
        {
            new ProcessorObj { AppID = "A1", Location = "London - UK" },
            new ProcessorObj { AppID = "A2", Location = "Kansas - USA" },
        });

        var processor = state.GetProcessorFromLocation("london - uk", showAuthKey: false);

        Assert.NotNull(processor);
        Assert.Equal("A1", processor!.AppID);
    }

    [Fact]
    public void GetProcessorFromLocation_Empty_ReturnsNull()
    {
        var state = CreateState(new[]
        {
            new ProcessorObj { AppID = "A1", Location = "London - UK" },
        });

        var processor = state.GetProcessorFromLocation("", showAuthKey: false);

        Assert.Null(processor);
    }

    [Fact]
    public void GetProcessorsByLocations_FiltersCaseInsensitive()
    {
        var state = CreateState(new[]
        {
            new ProcessorObj { AppID = "A1", Location = "London - UK" },
            new ProcessorObj { AppID = "A2", Location = "Kansas - USA" },
        });

        var processors = state.GetProcessorsByLocations(new[] { "LONDON - UK", "Berlin - DE" }, showAuthKey: false);

        Assert.Single(processors);
        Assert.Equal("A1", processors[0].AppID);
    }

    [Fact]
    public void GetProcessorsByLocations_Empty_ReturnsEmpty()
    {
        var state = CreateState(new[]
        {
            new ProcessorObj { AppID = "A1", Location = "London - UK" },
        });

        var processors = state.GetProcessorsByLocations(new string[0], showAuthKey: false);

        Assert.Empty(processors);
    }

    [Fact]
    public void SetProcessorObj_UpdatesFields()
    {
        var state = CreateState(new[]
        {
            new ProcessorObj { AppID = "A1", Location = "London - UK", Load = 1 },
        });

        var updated = new ProcessorObj { AppID = "A1", Location = "Berlin - DE", Load = 5 };
        var result = state.SetProcessorObj(updated);

        Assert.True(result);
        var processor = state.GetProcessorFromID("A1", showAuthKey: false);
        Assert.NotNull(processor);
        Assert.Equal("Berlin - DE", processor!.Location);
        Assert.Equal(5, processor.Load);
    }

    [Fact]
    public void SetProcessorObjIsReady_UpdatesState()
    {
        var state = CreateState(new[]
        {
            new ProcessorObj { AppID = "A1", IsReady = false },
        });

        var result = state.SetProcessorObjIsReady("A1", true);

        Assert.True(result);
        var processor = state.GetProcessorFromID("A1", showAuthKey: false);
        Assert.NotNull(processor);
        Assert.True(processor!.IsReady);
    }

    [Fact]
    public void EnabledSystemProcessorList_ExcludesPrivateAndDisabled()
    {
        var state = CreateState(new[]
        {
            new ProcessorObj { AppID = "A1", IsEnabled = true, IsPrivate = false },
            new ProcessorObj { AppID = "A2", IsEnabled = true, IsPrivate = true },
            new ProcessorObj { AppID = "A3", IsEnabled = false, IsPrivate = false },
        });

        var processors = state.EnabledSystemProcessorList(showAuthKey: false);

        Assert.Single(processors);
        Assert.Equal("A1", processors[0].AppID);
    }

    [Fact]
    public void FilteredSystemProcessorList_RespectsLoadAndEnabled()
    {
        var state = CreateState(new[]
        {
            new ProcessorObj { AppID = "A1", Load = 1, MaxLoad = 5, IsEnabled = true, IsPrivate = false },
            new ProcessorObj { AppID = "A2", Load = 6, MaxLoad = 5, IsEnabled = true, IsPrivate = false },
            new ProcessorObj { AppID = "A3", Load = 1, MaxLoad = 5, IsEnabled = false, IsPrivate = false },
        });

        var processors = state.FilteredSystemProcessorList(showAuthKey: false);

        Assert.Single(processors);
        Assert.Equal("A1", processors[0].AppID);
    }

    [Fact]
    public void FilteredUserProcessorList_UsesOwnerAndLoad()
    {
        var state = CreateState(new[]
        {
            new ProcessorObj { AppID = "A1", Owner = "user", Load = 1, MaxLoad = 2, IsEnabled = true },
            new ProcessorObj { AppID = "A2", Owner = "user", Load = 3, MaxLoad = 2, IsEnabled = true },
            new ProcessorObj { AppID = "A3", Owner = "other", Load = 1, MaxLoad = 2, IsEnabled = true },
        });

        var processors = state.FilteredUserProcessorList("user", showAuthKey: false);

        Assert.Single(processors);
        Assert.Equal("A1", processors[0].AppID);
    }

    [Fact]
    public void IsEndPointAvailable_RespectsDisabledList()
    {
        var state = CreateState(new[]
        {
            new ProcessorObj { AppID = "A1", DisabledEndPointTypes = new List<string> { "http" } },
        });

        Assert.False(state.IsEndPointAvailable("http", "A1"));
        Assert.True(state.IsEndPointAvailable("tcp", "A1"));
    }

    [Fact]
    public void GetNextProcessorAppID_ReturnsLowestLoadAndIncrements()
    {
        var state = CreateState(new[]
        {
            new ProcessorObj { AppID = "A1", Load = 1, MaxLoad = 5, IsEnabled = true, IsPrivate = false },
            new ProcessorObj { AppID = "A2", Load = 0, MaxLoad = 5, IsEnabled = true, IsPrivate = false },
        });

        var selected = state.GetNextProcessorAppID("http");

        Assert.Equal("A2", selected);
        var processor = state.GetProcessorFromID("A2", showAuthKey: false);
        Assert.NotNull(processor);
        Assert.Equal(1, processor!.Load);
    }
}
