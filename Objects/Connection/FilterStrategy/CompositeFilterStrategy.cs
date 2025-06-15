using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Connection
{
    public interface INetConnectFilterStrategy
    {
          bool ShouldInclude(INetConnect netConnect);
    }
     public interface IEndpointSettingStrategy
{
    void SetTotalEndpoints(List<INetConnect> netConnects);
}
   public class CompositeFilterStrategy : INetConnectFilterStrategy
{
    private readonly List<INetConnectFilterStrategy> _filterStrategies;

    public CompositeFilterStrategy(params INetConnectFilterStrategy[] filterStrategies)
    {
        _filterStrategies = filterStrategies.ToList();
    }

    public bool ShouldInclude(INetConnect netConnect)
    {
        return _filterStrategies.All(strategy => strategy.ShouldInclude(netConnect));
    }

    public void SetTotalEndpoints(List<INetConnect> netConnects)
    {
        foreach (var strategy in _filterStrategies.OfType<IEndpointSettingStrategy>())
        {
            strategy.SetTotalEndpoints(netConnects);
        }
    }
}


}