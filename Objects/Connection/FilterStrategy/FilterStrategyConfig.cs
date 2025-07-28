namespace NetworkMonitor.Connection;
public class FilterStrategyConfig
{
    public string StrategyName { get; set; }
    public int FilterSkip { get; set; }
    public int FilterStart { get; set; }
    // Optional: for JSON config binding, use a string and parse to TimeSpan
    public string? TimeSpanString { get; set; }
    public TimeSpan? TimeSpan
    {
        get
        {
            if (TimeSpanString == null) return null;
            if (System.TimeSpan.TryParse(TimeSpanString, out var ts))
                return ts;
            return null;
        }
    }
   
}   
    
