using System;
using System.Diagnostics.Contracts;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Rtsp;
using System.Collections.Generic;
using NLog;
using Rtsp.Sdp;
using System.Linq;
using VideoGate.Infrastructure.Interfaces;

using System.Threading.Tasks;
using VideoGate.Infrastructure.Models;

// RTSP Server Example (c) Roger Hardiman, 2016, 2018
// Released uder the MIT Open Source Licence

namespace RTSPServer
{
    public class RtspServer : IRtspServer, IDisposable
    {
        const uint GlobalSsrc = 0x4321FADE; // 8 hex digits
        TcpListener _RTSPServerListener;
        ManualResetEvent _stopping;
        Task _listenTask;
        IPAddress _ipAddress = IPAddress.Any;

        List<RTSPConnection> _rtspList = new List<RTSPConnection>(); // list of RTSP Listeners

        Random _random = new Random();

        Authentication _authentication = null;

        ILogger _logger = LogManager.GetLogger("RtspServer");

        readonly IRequestUrlVideoSourceResolverStrategy _requestUrlVideoSourceResolverStrategy; 
        readonly int _portNumber;

        public RtspServer(IAppConfigurationFacade appConfigurationFacade,
            IRequestUrlVideoSourceResolverStrategy requestUrlVideoSourceResolverStrategy)
        {
            _portNumber = appConfigurationFacade.RtspServerPort;
            _requestUrlVideoSourceResolverStrategy = requestUrlVideoSourceResolverStrategy;
            if (_portNumber < System.Net.IPEndPoint.MinPort || _portNumber > System.Net.IPEndPoint.MaxPort)
                throw new ArgumentOutOfRangeException("aPortNumber", _portNumber, "Port number must be between System.Net.IPEndPoint.MinPort and System.Net.IPEndPoint.MaxPort");
            Contract.EndContractBlock();

            if (String.IsNullOrEmpty(appConfigurationFacade.RtspServerLogin) == false
                && String.IsNullOrEmpty(appConfigurationFacade.RtspServerPassword) == false) {
                String realm = "SharpRTSPServer";
                var authenticationParameters = new AuthenticationParameters()
                {
                    username = appConfigurationFacade.RtspServerLogin,
                    password = appConfigurationFacade.RtspServerPassword,
                    realm = realm,
                    authenticationType = AuthenticationType.Digest,
                };
                _authentication = new Authentication(authenticationParameters, _logger);
            } else {
                _authentication = null;
            }

            RtspUtils.RegisterUri();

            Exception getIPException;
            _ipAddress = IPUtils.GetIPAddressFromString(appConfigurationFacade.RtspServerAddress, out getIPException);


            if (getIPException != null)
            {
                _logger.Error("Setting RtspServerAddress failed: "+getIPException);
            }
            
            _RTSPServerListener = new TcpListener(_ipAddress, _portNumber);

            try 
            {
                StartListen();
            } 
            catch(Exception ex) 
            {
                _logger.Error($"Error: Could not start rtsp server on {_ipAddress}:{_portNumber} : "+ex.ToString());
                throw;
            } 
        }

        void StartListen()
        {
            _RTSPServerListener.Start();
            _logger.Info($"Rtsp server started at {_ipAddress}:{_portNumber}");

            _stopping = new ManualResetEvent(false);
            _listenTask = Task.Factory.StartNew(() =>  AcceptConnection(), TaskCreationOptions.LongRunning);
        }

        void AcceptConnection()
        {
            Guid newConnectionId = Guid.Empty;
            try
            {
                 
                while (!_stopping.WaitOne(0))
                {
                    // Wait for an incoming TCP Connection
                    TcpClient oneClient = _RTSPServerListener.AcceptTcpClient();
                    newConnectionId = Guid.NewGuid();                    

                    // Hand the incoming TCP connection over to the RTSP classes
                    var rtspSocket = new RtspTcpTransport(oneClient);
                    RTSPConnection newRTSPConnection = null; 
                    // Add the RtspListener to the RTSPConnections List
                    lock (_rtspList) {
                        newRTSPConnection = new RTSPConnection(newConnectionId, rtspSocket, _ipAddress, _requestUrlVideoSourceResolverStrategy); 
                        newRTSPConnection.OnConnectionAdded += ProcessConnectionAdded;   
                        newRTSPConnection.OnConnectionRemoved += ProcessConnectionRemoved;  
                        newRTSPConnection.OnProvideSdpData += ProcessRtspProvideSdpData;                                      
                        _rtspList.Add(newRTSPConnection);                    
                    }

                    newRTSPConnection.Start();
                }
            }
            catch (SocketException error)
            {
                 _logger.Debug($"{newConnectionId} Got an error listening, I have to handle the stopping which also throw an error: " + error);
            }
            catch (Exception error)
            {
                _logger.Debug($"{newConnectionId} Got an error listening:" + error);
            }


        }

        protected void ProcessConnectionAdded(Guid connectionId, VideoSource videoSource)
        {
            OnConnectionAdded?.Invoke(connectionId,videoSource);
        }

        protected void ProcessConnectionRemoved(Guid connectionId, VideoSource videoSource)
        {
            if (videoSource != null)
            {
                OnConnectionRemoved?.Invoke(connectionId,videoSource);
            }
            
            lock(_rtspList)
            {
                _rtspList.RemoveAll(c => c.Id == connectionId);
            }
            
        }

        protected Task<byte[]> ProcessRtspProvideSdpData(Guid connectionId, VideoSource videoSource)
        {
            return OnProvideSdpData?.Invoke(connectionId,videoSource);
        }

        public void StopListen()
        {
            _RTSPServerListener.Stop();
            _stopping.Set();
            _listenTask.Wait();
        }

        #region IDisposable Membres

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopListen();
                _stopping.Dispose();
            }
        }

        #endregion

    
        public void SendRtpDataToConnection(Guid connectionId,byte[] data, Media.MediaTypes mediaType)
        {

            RTSPConnection targetConnection = null;
            lock (_rtspList) 
            {
                foreach (RTSPConnection connection in _rtspList.ToArray()) 
                { // Convert to Array to allow us to delete from rtsp_list
                    if (connectionId == connection.Id) 
                    {
                        targetConnection = connection;
                        break;
                    }                                    
                }
            }

            if (targetConnection != null)
            {
                targetConnection.SendRtpData(data,mediaType);
            }

        }

        public event RtspConnectionHandler OnConnectionAdded; 
        public event RtspConnectionHandler OnConnectionRemoved;

        public event RtspProvideSdpDataHandler OnProvideSdpData;
        public event RtspPlayRequestHandler OnPlay;
        public event RtspPlayRequestHandler OnStop;


        public void SendRtpAudioData(Guid connectionId, byte[] data)
        {
            SendRtpDataToConnection(connectionId,data, Media.MediaTypes.audio);
        }

        public void SendRtcpAudioData(Guid connectionId, byte[] data)
        {
            //throw new NotImplementedException();
        }

        public void SendRtpVideoData(Guid connectionId, byte[] data)
        {
          //  _logger.Info($"{Id} {current_rtp_count} RTSP clients connected. " + current_rtp_play_count + " RTSP clients in PLAY mode");


            SendRtpDataToConnection(connectionId,data, Media.MediaTypes.video);
        }

        public void SendRtcpVideoData(Guid connectionId, byte[] data)
        {
            //throw new NotImplementedException();
        }

        public void ForceDisconnectPool(List<Guid> connectionIds)
        {
                           
            foreach(Guid connectionId in connectionIds.ToList())
            {
                RTSPConnection connection  = null;
                lock(_rtspList)
                { 
                    connection = _rtspList.FirstOrDefault(c => c.Id == connectionId);
                }
                if (connection != null)
                {
                    connection.CloseConnection("forced");
                }
            }
            
        }
    }

}

