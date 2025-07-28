using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Connection
{
    public class RandomCmdEndpointFilterStrategy : INetConnectFilterStrategy, IEndpointSettingStrategy
    {
        private int _counter;
        private readonly int _n;
        private int _totalEndpoints;
        private int _offset;
        private static readonly ThreadLocal<Random> _random = new ThreadLocal<Random>(() => new Random());

        public RandomCmdEndpointFilterStrategy(int n, int counter = 0, int offset = 0)
        {
            if (n == 0) n = 1;
            _counter = counter;
            _n = n;
            _offset = offset;
        }

        private List<string> _matchingConnectNames = new List<string>() { "crawlsite" };
        public void SetTotalEndpoints(List<INetConnect> netConnects)
        {
            int totalCmdEndpoints = netConnects.Count(x =>
            _matchingConnectNames.Any(connectName => x.MpiStatic.EndPointType.ToLower().Contains(connectName))
        );
            _totalEndpoints = totalCmdEndpoints;
        }
        public bool ShouldInclude(INetConnect netConnect)
        {
            if (_matchingConnectNames.Any(connectName => netConnect.MpiStatic.EndPointType.ToLower().Contains(connectName)))
            {
                bool include = (_counter + _offset) % _n == 0;

                _counter++;

                if (_counter >= _totalEndpoints)
                {
                    _counter = 0;
                    _offset = (_offset + 1) % _n;
                }

                if (include)
                {
                    bool randomInclude = _random.Value!.NextDouble() >= 0.5;
                    // Log the decision
                    Console.WriteLine($"RandomCmdEndpointFilterStrategy: Endpoint '{netConnect.MpiStatic.EndPointType}' inclusion decision: {randomInclude}");
                    return randomInclude;
                }
                return false;
            }
            return true;
        }
    }


}
