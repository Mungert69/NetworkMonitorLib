using System.Collections.Generic;
namespace NetworkMonitor.Utils.Helpers;
public class CountryRegionMapper
{
    private readonly Dictionary<string, string> CountryToRegionMap = new Dictionary<string, string>
    {
        // Europe
        { "AL", "Europe" }, { "AD", "Europe" }, { "AM", "Europe" }, { "AT", "Europe" }, { "AZ", "Europe" },
        { "BY", "Europe" }, { "BE", "Europe" }, { "BA", "Europe" }, { "BG", "Europe" }, { "HR", "Europe" },
        { "CZ", "Europe" }, { "DK", "Europe" }, { "EE", "Europe" }, { "FI", "Europe" },
        { "FR", "Europe" }, { "GE", "Europe" }, { "DE", "Europe" }, { "GR", "Europe" }, { "HU", "Europe" },
        { "IS", "Europe" }, { "IE", "Europe" }, { "IT", "Europe" }, { "LV", "Europe" },
        { "LI", "Europe" }, { "LT", "Europe" }, { "LU", "Europe" }, { "MT", "Europe" }, { "MD", "Europe" },
        { "MC", "Europe" }, { "ME", "Europe" }, { "NL", "Europe" }, { "MK", "Europe" }, { "NO", "Europe" },
        { "PL", "Europe" }, { "PT", "Europe" }, { "RO", "Europe" }, { "RU", "Europe" }, { "SM", "Europe" },
        { "RS", "Europe" }, { "SK", "Europe" }, { "SI", "Europe" }, { "ES", "Europe" }, { "SE", "Europe" },
        { "CH", "Europe" }, { "TR", "Europe" }, { "UA", "Europe" }, { "GB", "Europe" }, { "VA", "Europe" },

        // America
        { "AG", "America" }, { "AR", "America" }, { "BS", "America" }, { "BB", "America" }, { "BZ", "America" },
        { "BO", "America" }, { "BR", "America" }, { "CA", "America" }, { "CL", "America" }, { "CO", "America" },
        { "CR", "America" }, { "CU", "America" }, { "DM", "America" }, { "DO", "America" }, { "EC", "America" },
        { "SV", "America" }, { "GD", "America" }, { "GT", "America" }, { "GY", "America" }, { "HT", "America" },
        { "HN", "America" }, { "JM", "America" }, { "MX", "America" }, { "NI", "America" }, { "PA", "America" },
        { "PY", "America" }, { "PE", "America" }, { "KN", "America" }, { "LC", "America" }, { "VC", "America" },
        { "SR", "America" }, { "TT", "America" }, { "US", "America" }, { "UY", "America" }, { "VE", "America" },

        // Asia
       { "AF", "Asia" }, { "BH", "Asia" }, { "BD", "Asia" }, { "BT", "Asia" }, { "BN", "Asia" },
        { "KH", "Asia" }, { "CN", "Asia" }, { "CY", "Asia" }, { "IN", "Asia" }, { "ID", "Asia" },
        { "IR", "Asia" }, { "IQ", "Asia" }, { "IL", "Asia" }, { "JP", "Asia" }, { "JO", "Asia" },
        { "KZ", "Asia" }, { "KW", "Asia" }, { "KG", "Asia" }, { "LA", "Asia" }, { "LB", "Asia" },
        { "MY", "Asia" }, { "MV", "Asia" }, { "MN", "Asia" }, { "MM", "Asia" }, { "NP", "Asia" },
        { "KP", "Asia" }, { "OM", "Asia" }, { "PK", "Asia" }, { "PS", "Asia" }, { "PH", "Asia" },
        { "QA", "Asia" }, { "SA", "Asia" }, { "SG", "Asia" }, { "KR", "Asia" }, { "LK", "Asia" },
        { "SY", "Asia" }, { "TW", "Asia" }, { "TJ", "Asia" }, { "TH", "Asia" }, { "TL", "Asia" },
        { "TM", "Asia" }, { "AE", "Asia" }, { "UZ", "Asia" }, { "VN", "Asia" }, { "YE", "Asia" },
        
        // Additional regions (optional and could be mapped to the closest major region if needed)
        // ...
    };

      private string _defaultRegion ;
    private List<string> _enabledRegions ;

    public CountryRegionMapper(string defaultRegion, List<string> enabledRegions) {
        _defaultRegion = defaultRegion;
        _enabledRegions = enabledRegions;
    } 

    public string MapCountryToRegion(string? countryCode)
    {
        if (countryCode == null) return _defaultRegion;

        if (CountryToRegionMap.TryGetValue(countryCode.ToUpper(), out var region))
        {
            if (_enabledRegions.Any(w => w == region))
                return region;
            else return _defaultRegion;
        }
   
        return _defaultRegion;
    }
}
