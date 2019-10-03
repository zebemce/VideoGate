using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using RTSP;

namespace Rtsp
{
    public class UDPSocket : IDisposable
    {
              
        private UdpClient _dataSocket = null;
        private UdpClient _controlSocket = null;

        private Task _dataReadTask = null;
        private Task _controlReadTask = null;

        public int _dataPort = 50000;
        public int _controlPort = 50001;

        bool _isMulticast = false;
        IPAddress _dataMulticastAddress;
        IPAddress _controlMulticastAddress;
        IPAddress _initialAddress;

        readonly ILogger _logger = LogManager.GetLogger("UDPSocket");
        bool _stopped = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="UDPSocket"/> class.
        /// Creates two new UDP sockets using the start and end Port range
        /// </summary>
        public UDPSocket(IPAddress address, int startPort, int endPort)
        {
             
            _initialAddress = address;
            _isMulticast = false;

            // open a pair of UDP sockets - one for data (video or audio) and one for the status channel (RTCP messages)
            // Video/Audio port must be odd and command even (next one)
            bool portReserved = UdpPortsWatcherSingleton.Instance.TryReservePort(startPort, endPort, out _dataPort,out _controlPort);;

            if (false == portReserved)
            {
                throw new Exception("UdpClient failed to reserve UDP port");
            }   

            try
            {
                _logger.Trace($"Start UdpClient creation for {address} {_dataPort}-{_controlPort}");
                _dataSocket = address == IPAddress.Any ? new UdpClient(_dataPort) : new UdpClient(new IPEndPoint(address,_dataPort));
                _logger.Trace($"UdpClient for {address} {_dataPort} created {_dataSocket.Client.LocalEndPoint}");
                _controlSocket = address == IPAddress.Any ? new UdpClient(_controlPort) : new UdpClient(new IPEndPoint(address,_controlPort));
                _logger.Trace($"UdpClient for {address} {_controlPort} created {_controlSocket.Client.LocalEndPoint}");
                _logger.Trace($"End UdpClient creation for {address} {_dataPort}-{_controlPort}");


                _dataSocket.Client.ReceiveBufferSize = 100 * 1024;
                _dataSocket.Client.SendBufferSize = 65535; // default is 8192. Make it as large as possible for large RTP packets which are not fragmented

                _controlSocket.Client.DontFragment = false;   
            }
            catch(Exception ex)
            {
                _logger.Trace($"UdpClient exception for {_initialAddress} {_dataPort}-{_controlPort} {ex}");
                try
                {
                    UdpPortsWatcherSingleton.Instance.TryReleasePort(_dataPort, _controlPort);
                    DisposeSockets(address);                    
                }
                catch(Exception e)
                {
                    _logger.Warn($"UdpClient exception for {_initialAddress} {_dataPort}-{_controlPort} disposal {e}");
                   throw;
                }      
            }
        }

        void DisposeSockets(IPAddress address)
        {
            if (_dataSocket != null)
            {
                string remoteAddress = _dataSocket.Client != null ? _dataSocket.Client.LocalEndPoint.ToString() : "null";
                _dataSocket.Close();
                _logger.Trace($"UdpClient for {address} {_dataPort} disposed {_dataSocket.Client?.LocalEndPoint}");
                _dataSocket = null;
            }
                
            if (_controlSocket != null)
            {
                string remoteAddress = _controlSocket.Client != null ? _controlSocket.Client.LocalEndPoint.ToString() : "null";
                _controlSocket.Close();
                _logger.Trace($"UdpClient for {address} {_controlPort} disposed {_controlSocket.Client?.LocalEndPoint}");
                _controlSocket = null;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UDPSocket"/> class.
        /// Used with Multicast mode with the Multicast Address and Port
        /// </summary>
        public UDPSocket(String data_multicast_address, int data_multicast_port, String control_multicast_address, int control_multicast_port)
        {

            _isMulticast = true;

            // open a pair of UDP sockets - one for data (video or audio) and one for the status channel (RTCP messages)
            this._dataPort = data_multicast_port;
            this._controlPort = control_multicast_port;

            try
            {
                IPEndPoint data_ep = new IPEndPoint(IPAddress.Any, _dataPort);
                IPEndPoint control_ep = new IPEndPoint(IPAddress.Any, _controlPort);

                _dataMulticastAddress = IPAddress.Parse(data_multicast_address);
                _controlMulticastAddress = IPAddress.Parse(control_multicast_address);

                _dataSocket = new UdpClient();
                _dataSocket.Client.Bind(data_ep);
                _dataSocket.JoinMulticastGroup(_dataMulticastAddress);

                _controlSocket = new UdpClient();
                _controlSocket.Client.Bind(control_ep);
                _controlSocket.JoinMulticastGroup(_controlMulticastAddress);


                _dataSocket.Client.ReceiveBufferSize = 100 * 1024;
                _dataSocket.Client.SendBufferSize = 65535; // default is 8192. Make it as large as possible for large RTP packets which are not fragmented


                _controlSocket.Client.DontFragment = false;

            }
            catch (SocketException)
            {
                // Fail to allocate port, try again
                if (_dataSocket != null)
                {
                    _dataSocket.Close();
                    _dataSocket = null;
                }
                   
                if (_controlSocket != null)
                {
                    _controlSocket.Close();
                    _controlSocket = null;
                }
                    

                return;
            }
        }

        /// <summary>
        /// Starts this instance.
        /// </summary>
        public void Start()
        {
            _logger.Trace($"UdpSocket.Start() dataPort={_dataPort}");
            if (_dataSocket == null || _controlSocket == null)
            {
                throw new InvalidOperationException("UDP Forwader host was not initialized, can't continue");
            }

            if (_dataReadTask != null)
            {
                throw new InvalidOperationException("Forwarder was stopped, can't restart it");
            }

            _dataReadTask = Task.Factory.StartNew(() => DoWorkerDataJob(), TaskCreationOptions.LongRunning);
            _controlReadTask = Task.Factory.StartNew(() => DoWorkerControlJob(), TaskCreationOptions.LongRunning);
        }

        /// <summary>
        /// Stops this instance.
        /// </summary>
        public void Stop()
        {
            _logger.Trace($"UdpSocket.Stop() dataPort={_dataPort}");
            
            if (_stopped) return;
            _stopped = true;
            try
            {
                if (_isMulticast)
                {
                    try
                    {
                        // leave the multicast groups
                        _dataSocket?.DropMulticastGroup(_dataMulticastAddress);
                        _controlSocket?.DropMulticastGroup(_controlMulticastAddress);
                    }
                    catch(Exception ex)
                    {
                        _logger.Error(ex);
                    }
                    
                }
                if (_dataSocket != null)
                {
                    string remoteAddress = _dataSocket.Client != null ? _dataSocket.Client.LocalEndPoint.ToString() : "null";
                    _dataSocket.Close();
                    _logger.Trace($"UdpClient for {(_initialAddress == null ? string.Empty : _initialAddress.ToString())} {_dataPort} disposed {remoteAddress}");
                }

                if (_controlSocket != null)
                {
                    string remoteAddress = _controlSocket.Client != null ? _controlSocket.Client.LocalEndPoint.ToString() : "null";
                    _controlSocket.Close();
                    _logger.Trace($"UdpClient for {(_initialAddress == null ? string.Empty : _initialAddress.ToString())} {_controlPort} disposed {_controlSocket.Client?.LocalEndPoint}");
                }

            }
            catch(Exception ex)
            {
                _logger.Trace($"UdpClient exception for {_initialAddress} {_dataPort}-{_controlPort} disposal {ex}");
                throw;
            }
            finally
            {
                UdpPortsWatcherSingleton.Instance.TryReleasePort(_dataPort, _controlPort);
            }
        }

        /// <summary>
        /// Occurs when message is received.
        /// </summary>
        public event EventHandler<Rtsp.RtspChunkEventArgs> DataReceived;
        public event EventHandler<Rtsp.RtspChunkEventArgs> ControlReceived;

        /// <summary>
        /// Raises the <see cref="E:DataReceived"/> event.
        /// </summary>
        /// <param name="rtspChunkEventArgs">The <see cref="Rtsp.RtspChunkEventArgs"/> instance containing the event data.</param>
        protected void OnDataReceived(Rtsp.RtspChunkEventArgs rtspChunkEventArgs)
        {
            EventHandler<Rtsp.RtspChunkEventArgs> handler = DataReceived;

            if (handler != null)
                handler(this, rtspChunkEventArgs);
        }

        protected void OnControlReceived(Rtsp.RtspChunkEventArgs rtspChunkEventArgs)
        {
            EventHandler<Rtsp.RtspChunkEventArgs> handler = ControlReceived;

            if (handler != null)
                handler(this, rtspChunkEventArgs);
        }


        /// <summary>
        /// Does the video job.
        /// </summary>
        private void DoWorkerDataJob()
        {

            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, _dataPort);
            try
            {
                // loop until we get an exception eg the socket closed
                while (true)
                {
                    byte[] frame = _dataSocket.Receive(ref ipEndPoint);

                    // We have an RTP frame.
                    // Fire the DataReceived event with 'frame'
                    //Console.WriteLine("Received RTP data on port " + data_port);

                    Rtsp.Messages.RtspChunk currentMessage = new Rtsp.Messages.RtspData();
                    // aMessage.SourcePort = ??
                    currentMessage.Data = frame;
                    ((Rtsp.Messages.RtspData)currentMessage).Channel = _dataPort;


                    OnDataReceived(new Rtsp.RtspChunkEventArgs(currentMessage));

                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        private void DoWorkerControlJob()
        {

            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, _controlPort);
            try
            {
                // loop until we get an exception eg the socket closed
                while (true)
                {
                    byte[] frame = _controlSocket.Receive(ref ipEndPoint);

                    // We have an RTP frame.
                    // Fire the DataReceived event with 'frame'
                    Console.WriteLine("Received RTCP data on port " + _controlSocket);

                    Rtsp.Messages.RtspChunk currentMessage = new Rtsp.Messages.RtspData();
                    // aMessage.SourcePort = ??
                    currentMessage.Data = frame;
                    ((Rtsp.Messages.RtspData)currentMessage).Channel = _dataPort;


                    OnDataReceived(new Rtsp.RtspChunkEventArgs(currentMessage));

                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        /// <summary>
        /// Write to the RTP Data Port
        /// </summary>
        public void Write_To_Data_Port(byte[] data, String hostname, int port) {
            _dataSocket.Send(data,data.Length, hostname, port);
        }

        /// <summary>
        /// Write to the RTCP Control Port
        /// </summary>
        public void Write_To_Control_Port(byte[] data, String hostname, int port)
        {
            _controlSocket.Send(data, data.Length, hostname, port);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
