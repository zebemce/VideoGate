using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
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

                if (false == portBusy && CheckAvailableUdpPort(dataPortNumber, controlPortNumber))
                {
                    portsDictionary[address].Add(dataPortNumber);
                    _logger.Trace($"UdpClient for {address} ports {dataPortNumber}-{controlPortNumber} reserved");

                    return true;
                }

                return false;

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

        public bool CheckAvailableUdpPort(int dataPort, int controlPort)
        {
            _logger.Trace($"UdpClient CheckAvailableUdpPort for ports {dataPort}-{controlPort} started");
            
            IPEndPoint[] endPoints;
            List<int> portArray = new List<int>();

            IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();

            //getting active connections
            TcpConnectionInformation[] connections = properties.GetActiveTcpConnections();
            portArray.AddRange(from n in connections
                               select n.LocalEndPoint.Port);

            //getting active tcp listners - WCF service listening in tcp
            endPoints = properties.GetActiveTcpListeners();
            portArray.AddRange(from n in endPoints
                               select n.Port);

            //getting active udp listeners
            endPoints = properties.GetActiveUdpListeners();
            portArray.AddRange(from n in endPoints
                               select n.Port);

            portArray.Sort();

          /*   for (int i = startingPort; i < UInt16.MaxValue; i++)
                if (!portArray.Contains(i))
                    return i;*/


            var result = !portArray.Contains(dataPort) && !portArray.Contains(controlPort);
            _logger.Trace($"UdpClient CheckAvailableUdpPort for ports {dataPort}-{controlPort} finished : {result}");
            return result;
        }
    }
}
