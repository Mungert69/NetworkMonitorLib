using System.Collections.Generic;
using NetworkMonitor.Utils.Helpers;
using Xunit;

namespace NetworkMonitorLib.Tests.Helpers;

public class CountryRegionMapperTests
{
    [Fact]
    public void MapCountryToRegion_ReturnsMatchingEnabledRegion()
    {
        var mapper = new CountryRegionMapper("Europe", new List<string> { "Europe", "Asia" });

        var region = mapper.MapCountryToRegion("fr");

        Assert.Equal("Europe", region);
    }

    [Fact]
    public void MapCountryToRegion_FallsBackWhenRegionDisabled()
    {
        var mapper = new CountryRegionMapper("Europe", new List<string> { "America" });

        var region = mapper.MapCountryToRegion("FR");

        Assert.Equal("Europe", region);
    }

    [Fact]
    public void MapCountryToRegion_ReturnsDefaultForUnknown()
    {
        var mapper = new CountryRegionMapper("Europe", new List<string> { "America" });

        var region = mapper.MapCountryToRegion("ZZ");

        Assert.Equal("Europe", region);
    }

    [Fact]
    public void MapCountryToRegion_ReturnsDefaultForNull()
    {
        var mapper = new CountryRegionMapper("Europe", new List<string> { "America" });

        var region = mapper.MapCountryToRegion(null);

        Assert.Equal("Europe", region);
    }
}
