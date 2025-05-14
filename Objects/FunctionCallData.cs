namespace NetworkMonitor.Objects;
public class FunctionCallData
{
    public string? function { get; set; }
    public string? name { get; set; }
    public Dictionary<string, object>? arguments { get; set; }
    public Dictionary<string, object>? parameters { get; set; }

    public FunctionCallData()
    { }
    // Copy Constructor
    public FunctionCallData(FunctionCallData existing)
    {
        if (existing == null) return;
        function = existing.function;
        name = existing.name;

        // Perform deep copy for dictionaries if they are not null
        arguments = existing.arguments != null 
                    ? new Dictionary<string, object>(existing.arguments)
                    : null;
        parameters = existing.parameters != null 
                     ? new Dictionary<string, object>(existing.parameters)
                     : null;
    }
}
