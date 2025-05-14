namespace NetworkMonitor.Connection;
public static class NetConnectFilterStrategyFactory
{
    public static INetConnectFilterStrategy CreateStrategy(FilterStrategyConfig config)
    {
        switch (config.StrategyName)
        {
            case "quantum":
                return new QuantumEndpointFilterStrategy(config.FilterSkip, config.FilterStart);
            case "smtp":
                return new SmtpEndpointFilterStrategy(config.FilterSkip, config.FilterStart);
            case "cmd":
                return new CmdEndpointFilterStrategy(config.FilterSkip, config.FilterStart);
            case "randomcmd":
                return new RandomCmdEndpointFilterStrategy(config.FilterSkip, config.FilterStart);
          
            default:
                throw new ArgumentException($"Unknown strategy name: {config.StrategyName}");
        }
    }
    
}
