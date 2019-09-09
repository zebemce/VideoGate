using NLog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace RTSP
{
    public class UdpPortsWatcherSingleton
    {
        private readonly Dictionary<IPAddress, HashSet<int>> portsDictionary = new Dictionary<IPAddress, HashSet<int>>();
        private readonly object portsSetLock = new object();
        private readonly ILogger _logger = LogManager.GetLogger("UdpPortsWatcherSingleton");

        static UdpPortsWatcherSingleton()
        {
            Instance = new UdpPortsWatcherSingleton();
        }

        public static UdpPortsWatcherSingleton Instance { get; private set; }


        public bool TryReservePort(IPAddress address, int dataPortNumber, int controlPortNumber)
        {
            bool portBusy = false;
            lock (portsSetLock)
            {
                HashSet<int> portsSet;
                if (portsDictionary.TryGetValue(address, out portsSet))
                {
                    portBusy = portsSet.Contains(dataPortNumber);

                }
                else
                {
                    portsDictionary.Add(address, new HashSet<int>());
                }

                if (false == portBusy)
                {
                    portsDictionary[address].Add(dataPortNumber);
                    _logger.Trace($"UdpClient for {address} ports {dataPortNumber}-{controlPortNumber} reserved");
                }

                return !portBusy;

            }
        }

        public bool TryReleasePort(IPAddress address, int dataPortNumber, int controlPortNumber)
        {

            if (address == null)
            {
                return false;
            }

            lock (portsSetLock)
            {
                bool portRemoved = portsDictionary[address]?.Remove(dataPortNumber) ?? false;
                _logger.Trace($"UdpClient for {address} ports {dataPortNumber}-{controlPortNumber} released");
                return portRemoved;
            }

        }
    }
}
