using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NLog;
using Rtsp;
using Rtsp.Messages;
using Rtsp.Sdp;
using VideoGate.Infrastructure.Interfaces;
using VideoGate.Infrastructure.Models;

namespace RTSPServer
{
    public class RTSPConnection
    {
        const uint GLOBAL_SSRC = 0x4321FADE; // 8 hex digits        
        
        public Guid Id {get; private set;}
        public bool Play {get; private set;} = false;                  // set to true when Session is in Play mode

        protected VideoSource _videoSource = null;
        
        protected Rtsp.RtspListener _listener = null;  // The RTSP client connection
        
        protected DateTime _timeSinceLastRtspKeepAlive = DateTime.UtcNow; // Time since last RTSP message received - used to spot dead UDP clients
        protected UInt32 _ssrc = 0x12345678;           // SSRC value used with this client connection
        protected String _clientHostname = "";        // Client Hostname/IP Address
        protected int timeout_in_seconds = 70;  // must have a RTSP message every 70 seconds or we will close the connection
        
        protected string contentBase = null;

        protected String _videoSessionId = "";             // RTSP Session ID used with this client connection
        protected UInt16 _videoSequenceNumber = 1;         // 16 bit RTP packet sequence number used with this client connection
        protected Rtsp.Messages.RtspTransport _videoClientTransport; // Transport: string from the client to the server
        protected Rtsp.Messages.RtspTransport _videoTransportReply; // Transport: reply from the server to the client
        protected Rtsp.UDPSocket _videoUdpPair = null;     // Pair of UDP sockets (data and control) used when sending via UDP
        protected DateTime _timeSinceLastRtcpKeepAlive = DateTime.UtcNow; // Time since last RTCP message received - used to spot dead UDP clients
        protected int _sessionHandle = 1;
        protected Authentication _auth = null;
        protected IPAddress _ipAddress;

        protected SdpFile _sdpFile = null;
        
        protected String _audioSessionId = ""; 
        protected UInt16 _audioSequenceNumber = 1; 
        protected Rtsp.Messages.RtspTransport _audioClientTransport;
        protected Rtsp.Messages.RtspTransport _audioTransportReply;
        protected Rtsp.UDPSocket _audioUdpPair = null;
 
        
        protected ILogger _logger = LogManager.GetLogger("RtspServer");

        readonly IRequestUrlVideoSourceResolverStrategy _requestUrlVideoSourceResolverStrategy; 

        public event RtspConnectionHandler OnConnectionAdded; 
        public event RtspConnectionHandler OnConnectionRemoved;

        public event RtspProvideSdpDataHandler OnProvideSdpData;
        public event RtspPlayRequestHandler OnPlay;
        public event RtspPlayRequestHandler OnStop;

        public RTSPConnection(Guid connectionId, Rtsp.IRtspTransport rtspTransport, IPAddress ipAddress,
            IRequestUrlVideoSourceResolverStrategy requestUrlVideoSourceResolverStrategy)
        {
            Id = connectionId;
            
            _ssrc = GLOBAL_SSRC;
            _timeSinceLastRtspKeepAlive = DateTime.UtcNow;
            _timeSinceLastRtcpKeepAlive = DateTime.UtcNow; 
            _ipAddress = ipAddress;
            _requestUrlVideoSourceResolverStrategy = requestUrlVideoSourceResolverStrategy;  

            _listener = new Rtsp.RtspListener(rtspTransport,"RtspServer", Guid.Empty, Id);
            _clientHostname = _listener.RemoteAdress.Split(':')[0];
            _listener.MessageReceived += RTSP_Message_Received;
            _listener.SocketExceptionRaised += RTSP_SocketException_Raised;
            _listener.Disconnected += RTSP_Disconnected;

            _logger.Info($"Connection {Id} opened. Client: {rtspTransport.RemoteAddress}");
        }

        public void Start()
        {
            _listener.Start();
        }

        bool _videoDataSendStartInformed = false;
        bool _audioDataSendStartInformed = false;
        public void SendRtpData(byte[] data, Media.MediaTypes mediaType)
        {
            DateTime now = DateTime.UtcNow;            
                    
            // RTSP Timeout (clients receiving RTP video over the RTSP session
            // do not need to send a keepalive (so we check for Socket write errors)
            Boolean sending_rtp_via_tcp = false;


            if (mediaType == Media.MediaTypes.audio)
            {
                if ((_audioClientTransport != null) &&
                (_audioClientTransport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.TCP))
                {
                    sending_rtp_via_tcp = true;
                }
            }
            else if (mediaType == Media.MediaTypes.video)
            {
                if ((_videoClientTransport != null) &&
                (_videoClientTransport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.TCP))
                {
                    sending_rtp_via_tcp = true;
                }
            }
            else
            {
                return;
            }
                    

            if (sending_rtp_via_tcp == false && 
            ((now - _timeSinceLastRtspKeepAlive).TotalSeconds > timeout_in_seconds) &&
            ((now - _timeSinceLastRtcpKeepAlive).TotalSeconds > timeout_in_seconds)
            ) 
            {

                _logger.Info($"{Id} Removing session " + _audioSessionId + " due to TIMEOUT");
                                    
                CloseConnection("timeout");
                
                return;
            }
            
                
            // Only process Sessions in Play Mode
            if (Play == false) return;

            RtspTransport clientTransport;
            RtspTransport transportReply;
            String sessionId;
            UDPSocket udpPair;
            ushort sequenceNumber;

            if (mediaType == Media.MediaTypes.audio)
            {
                clientTransport = _audioClientTransport;
                transportReply = _audioTransportReply;
                sessionId = _audioSessionId;
                udpPair = _audioUdpPair;
                sequenceNumber = _audioSequenceNumber;
                _audioSequenceNumber++;

            }
            else if (mediaType == Media.MediaTypes.video)
            {
                clientTransport = _videoClientTransport;
                transportReply = _videoTransportReply;
                sessionId = _videoSessionId;
                udpPair = _videoUdpPair;
                sequenceNumber = _videoSequenceNumber;
                _videoSequenceNumber++;

            }else 
            {
                return;
            }

            if (clientTransport == null) return;

            String connection_type = "";                

            if (clientTransport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.TCP) connection_type = "TCP";
            if (clientTransport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP
                && clientTransport.IsMulticast == false) connection_type = "UDP";
            if (clientTransport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP
                && clientTransport.IsMulticast == true) connection_type = "Multicast";
            _logger.Trace($"{Id} Sending {mediaType} session " + sessionId + " " + connection_type +  " Sequence="+ sequenceNumber);

            // There could be more than 1 RTP packet (if the data is fragmented)
            Boolean write_error = false;

            // Send as RTP over RTSP (Interleaved)
            if (transportReply.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.TCP)
            {
                int channel = transportReply.Interleaved.First; // second is for RTCP status messages)
                object state = new object();
                try
                {
                    // send the whole NAL. With RTP over RTSP we do not need to Fragment the NAL (as we do with UDP packets or Multicast)
                    //session.listener.BeginSendData(video_channel, rtp_packet, new AsyncCallback(session.listener.EndSendData), state);
                    _listener.SendData(channel, data);
                }
                catch
                {
                    _logger.Error($"{Id} Error writing to listener " + _listener.RemoteAdress);
                    write_error = true;
                    return; 
                }
            }

            // Send as RTP over UDP
            if (transportReply.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP && transportReply.IsMulticast == false)
            {
                try
                {
                    // send the whole NAL. ** We could fragment the RTP packet into smaller chuncks that fit within the MTU
                    // Send to the IP address of the Client
                    // Send to the UDP Port the Client gave us in the SETUP command
                    udpPair.Write_To_Data_Port(data,_clientHostname,clientTransport.ClientPort.First);
                }
                catch (Exception e)
                {
                    _logger.Error($"{Id} UDP Write Exception " + e.ToString());
                    _logger.Error($"{Id} Error writing to listener " + _listener.RemoteAdress);
                    write_error = true;
                    return;
                }
            }
            
            // TODO. Add Multicast

            if (false == _videoDataSendStartInformed && mediaType == Media.MediaTypes.video)
            {
                _videoDataSendStartInformed = true;
                _logger.Info($"Connection {Id} started to send video over {connection_type}");
            }

            if (false == _audioDataSendStartInformed && mediaType == Media.MediaTypes.audio)
            {
                _audioDataSendStartInformed = true;
                _logger.Info($"Connection {Id} started to send audio over {connection_type}");
            }
            
            
            if (write_error)
            {
                _logger.Info($"{Id} Removing session " + _audioSessionId + " due to write error");                   
                CloseConnection("write error");
                
            }
                
            
        }

        internal void HandleClientSocketException(SocketException se, RtspListener listener)
        {
            if(se == null) return;

            switch (se.SocketErrorCode)
            {
                case SocketError.TimedOut:
                case SocketError.ConnectionAborted:
                case SocketError.ConnectionReset:
                case SocketError.Disconnecting:
                case SocketError.Shutdown:
                case SocketError.NotConnected:
                    {
                        CloseConnection("socket exception");                                          
                        return;
                    }
                default:
                    {
                        _logger.Error(se);
                        return;
                    }
            }
        }

        public void CloseConnection(string reason)
        {

            try
            {
                Play = false; // stop sending data
                if (_audioUdpPair != null) {
                    _audioUdpPair.Stop();
                    _audioUdpPair = null;
                }

                if (_videoUdpPair != null) {
                    _videoUdpPair.Stop();
                    _videoUdpPair = null;
                }

                _logger.Info($"Connection {Id} closed. Reason: {reason}");
                _listener.MessageReceived -= RTSP_Message_Received;
                _listener.SocketExceptionRaised -= RTSP_SocketException_Raised;
                _listener.Disconnected -= RTSP_Disconnected;
                _listener.Stop();

                _listener.Dispose();
            }
            catch(Exception ex)
            {
                _logger.Warn($"{Id} error closing connection: {ex}");
            }
            finally
            {
                var handler = OnConnectionRemoved;
                handler?.Invoke(Id, _videoSource);
            }
            
            

            
        }

        private void RTSP_SocketException_Raised(object sender, RtspSocketExceptionEventArgs e)
        {
            RtspListener listener = sender as RtspListener;
            SocketException ex = e.Ex;

            HandleClientSocketException(ex,listener);
        }

        private void RTSP_Disconnected(object sender, EventArgs e)
        {
            CloseConnection("disconnected");
        }

        private void RTSP_ProcessOptionsRequest(RtspRequestOptions message, RtspListener listener)
        {
            String requested_url = message.RtspUri.ToString();
            _logger.Info($"Connection {listener.ConnectionId} requested for url: {requested_url}");

            _videoSource = _requestUrlVideoSourceResolverStrategy.ResolveVideoSource(requested_url);
            OnConnectionAdded?.Invoke(Id, _videoSource); //treat connection useful when VideoSource determined

            // Create the reponse to OPTIONS
            Rtsp.Messages.RtspResponse options_response = message.CreateResponse(_logger);
            // Rtsp.Messages.RtspResponse options_response = OnRtspMessageReceived?.Invoke(message as Rtsp.Messages.RtspRequest,targetConnection);
            listener.SendMessage(options_response);
        }

        private void RTSP_ProcessDescribeRequest(RtspRequestDescribe message, RtspListener listener)
        {
            String requested_url = message.RtspUri.ToString();            

            Task<byte[]> sdpDataTask = _videoSource != null ?
                OnProvideSdpData?.Invoke(Id, _videoSource)
                : Task.FromResult<byte[]>(null);

            byte[] sdpData = sdpDataTask.Result;

            if (sdpData != null)
            {
                Rtsp.Messages.RtspResponse describe_response = message.CreateResponse(_logger);

                describe_response.AddHeader("Content-Base: " + requested_url);
                describe_response.AddHeader("Content-Type: application/sdp");
                describe_response.Data = sdpData;
                describe_response.AdjustContentLength();

                // Create the reponse to DESCRIBE
                // This must include the Session Description Protocol (SDP)

                describe_response.Headers.TryGetValue(RtspHeaderNames.ContentBase, out contentBase);

                using (StreamReader sdp_stream = new StreamReader(new MemoryStream(describe_response.Data)))
                {
                    _sdpFile = Rtsp.Sdp.SdpFile.Read(sdp_stream);
                }

                listener.SendMessage(describe_response);
            }
            else
            {


                Rtsp.Messages.RtspResponse describe_response = (message as Rtsp.Messages.RtspRequestDescribe).CreateResponse(_logger);
                //Method Not Valid In This State"
                describe_response.ReturnCode = 455;
                listener.SendMessage(describe_response);
            }
        }

        private RtspTransport RTSP_ConstructReplyTransport(RtspTransport transport, out UDPSocket udp_pair)
        {
            RtspTransport transport_reply = new RtspTransport();
            transport_reply.SSrc = GLOBAL_SSRC.ToString("X8"); // Convert to Hex, padded to 8 characters

            if (transport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.TCP)
            {
                // RTP over RTSP mode}
                transport_reply.LowerTransport = Rtsp.Messages.RtspTransport.LowerTransportType.TCP;
                transport_reply.Interleaved = new Rtsp.Messages.PortCouple(transport.Interleaved.First, transport.Interleaved.Second);
            }

            udp_pair = null;

            if (transport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP
                && transport.IsMulticast == false)
            {
                Boolean udp_supported = true;
                if (udp_supported)
                {
                    // RTP over UDP mode
                    // Create a pair of UDP sockets - One is for the Video, one is for the RTCP
                    _logger.Trace($"{Id} Start creating UDPSocket");
                    udp_pair = new Rtsp.UDPSocket(_ipAddress, 50000, 51000); // give a range of 500 pairs (1000 addresses) to try incase some address are in use
                    udp_pair.DataReceived += (object local_sender, RtspChunkEventArgs local_e) => {
                        // RTP data received
                        //_logger.Debug($"{listener.ConnectionId} RTP data received " + local_sender.ToString() + " " + local_e.ToString());
                    };
                    udp_pair.ControlReceived += (object local_sender, RtspChunkEventArgs local_e) => {
                        _timeSinceLastRtcpKeepAlive = DateTime.UtcNow;
                        // RTCP data received
                        //_logger.Debug($"{listener.ConnectionId} RTCP data received " + local_sender.ToString() + " " + local_e.ToString());
                    };
                    udp_pair.Start(); // start listening for data on the UDP ports
                    _logger.Trace($"{Id} End creating UDPSocket");

                    // Pass the Port of the two sockets back in the reply
                    transport_reply.LowerTransport = Rtsp.Messages.RtspTransport.LowerTransportType.UDP;
                    transport_reply.IsMulticast = false;
                    transport_reply.ClientPort = new Rtsp.Messages.PortCouple(udp_pair._dataPort, udp_pair._controlPort);
                }
                else
                {
                    transport_reply = null;
                }
            }

            if (transport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP
                && transport.IsMulticast == true)
            {
                // RTP over Multicast UDP mode}
                // Create a pair of UDP sockets in Multicast Mode
                // Pass the Ports of the two sockets back in the reply
                transport_reply.LowerTransport = Rtsp.Messages.RtspTransport.LowerTransportType.UDP;
                transport_reply.IsMulticast = true;
                transport_reply.Port = new Rtsp.Messages.PortCouple(7000, 7001);  // FIX

                // for now until implemented
                transport_reply = null;
            }

            return transport_reply;
        }

        private void RTSP_ProcessSetupRequest(RtspRequestSetup message, RtspListener listener)
        {
            // 
            var setupMessage = message;

            // Check the RTSP transport
            // If it is UDP or Multicast, create the sockets
            // If it is RTP over RTSP we send data via the RTSP Listener

            // FIXME client may send more than one possible transport.
            // very rare
            RtspTransport transport = setupMessage.GetTransports()[0];


            // Construct the Transport: reply from the Server to the client
            Rtsp.UDPSocket udp_pair;
            RtspTransport transport_reply = RTSP_ConstructReplyTransport(transport, out udp_pair);
            
            if (transport_reply != null)
            {

                // Update the session with transport information
                String copy_of_session_id = "";


                // ToDo - Check the Track ID to determine if this is a SETUP for the Video Stream
                // or a SETUP for an Audio Stream.
                // In the SDP the H264 video track is TrackID 0


                // found the connection
                // Add the transports to the connection

                if (contentBase != null)
                {
                    string controlTrack = setupMessage.RtspUri.AbsoluteUri.Replace(contentBase, string.Empty);
                    var requestMedia = _sdpFile.Medias.FirstOrDefault(media =>
                        media.Attributs.FirstOrDefault(a => a.Key == "control"
                            && (a.Value == controlTrack || "/" + a.Value == controlTrack)) != null);

                    if (requestMedia != null)
                    {
                        if (requestMedia.MediaType == Media.MediaTypes.video)
                        {
                            _videoClientTransport = transport;
                            _videoTransportReply = transport_reply;

                            // If we are sending in UDP mode, add the UDP Socket pair and the Client Hostname
                            _videoUdpPair = udp_pair;

                            if (setupMessage.Session == null)
                            {
                                _videoSessionId = _sessionHandle.ToString();
                                _sessionHandle++;
                            }
                            else
                            {
                                _videoSessionId = setupMessage.Session;
                            }



                            // Copy the Session ID
                            copy_of_session_id = _videoSessionId;

                        }

                        if (requestMedia.MediaType == Media.MediaTypes.audio)
                        {
                            _audioClientTransport = transport;
                            _audioTransportReply = transport_reply;

                            // If we are sending in UDP mode, add the UDP Socket pair and the Client Hostname
                            _audioUdpPair = udp_pair;


                            if (setupMessage.Session == null)
                            {
                                _audioSessionId = _sessionHandle.ToString();
                                _sessionHandle++;
                            }
                            else
                            {
                                _audioSessionId = setupMessage.Session;
                            }

                            // Copy the Session ID
                            copy_of_session_id = _audioSessionId;

                        }
                    }

                }

                _videoClientTransport = transport;
                _videoTransportReply = transport_reply;

                // If we are sending in UDP mode, add the UDP Socket pair and the Client Hostname
                _videoUdpPair = udp_pair;


                if (setupMessage.Session == null)
                {
                    _videoSessionId = _sessionHandle.ToString();
                    _sessionHandle++;
                }
                else
                {
                    _videoSessionId = setupMessage.Session;
                }

                // Copy the Session ID
                copy_of_session_id = _videoSessionId;

                Rtsp.Messages.RtspResponse setup_response = setupMessage.CreateResponse(_logger);
                setup_response.Headers[Rtsp.Messages.RtspHeaderNames.Transport] = transport_reply.ToString();
                setup_response.Session = copy_of_session_id;
                setup_response.Timeout = timeout_in_seconds;
                listener.SendMessage(setup_response);
            }
            else
            {
                Rtsp.Messages.RtspResponse setup_response = setupMessage.CreateResponse(_logger);
                // unsuported transport
                setup_response.ReturnCode = 461;
                listener.SendMessage(setup_response);
            }
        }

        private void RTSP_ProcessPlayRequest(RtspRequestPlay message, RtspListener listener)
        {
            OnPlay?.Invoke(Id);

            Play = true;  // ACTUALLY YOU COULD PAUSE JUST THE VIDEO (or JUST THE AUDIO)
            _logger.Info($"Connection {Id} play started");

            string range = "npt=0-";   // Playing the 'video' from 0 seconds until the end
            string rtp_info = "url=" + message.RtspUri + ";seq=" + _videoSequenceNumber; // TODO Add rtptime  +";rtptime="+session.rtp_initial_timestamp;

            // Send the reply
            Rtsp.Messages.RtspResponse play_response = message.CreateResponse(_logger);
            play_response.AddHeader("Range: " + range);
            play_response.AddHeader("RTP-Info: " + rtp_info);
            listener.SendMessage(play_response);



            //TODO: find a p[lace for this check]
            // Session ID was not found in the list of Sessions. Send a 454 error
            /*   Rtsp.Messages.RtspResponse play_failed_response = (e.Message as Rtsp.Messages.RtspRequestPlay).CreateResponse();
              play_failed_response.ReturnCode = 454; // Session Not Found
              listener.SendMessage(play_failed_response);*/
        }

        private void RTSP_ProcessPauseRequest(RtspRequestPause message, RtspListener listener)
        {
            if (message.Session == _videoSessionId /* OR AUDIO SESSION ID */)
            {
                OnStop?.Invoke(Id);

                // found the session
                Play = false; // COULD HAVE PLAY/PAUSE FOR VIDEO AND AUDIO

            }


            // ToDo - only send back the OK response if the Session in the RTSP message was found
            Rtsp.Messages.RtspResponse pause_response = message.CreateResponse(_logger);
            listener.SendMessage(pause_response);
        }

        private void RTSP_ProcessGetParameterRequest(RtspRequestGetParameter message, RtspListener listener)
        {
            // Create the reponse to GET_PARAMETER
            Rtsp.Messages.RtspResponse getparameter_response = message.CreateResponse(_logger);
            listener.SendMessage(getparameter_response);
        }

        private void RTSP_ProcessTeardownRequest(RtspRequestTeardown message, RtspListener listener)
        {
            if (message.Session == _videoSessionId) // SHOULD HAVE AN AUDIO TEARDOWN AS WELL
            {
                // If this is UDP, close the transport
                // For TCP there is no transport to close (as RTP packets were interleaved into the RTSP connection)

                Rtsp.Messages.RtspResponse getparameter_response = message.CreateResponse(_logger);
                listener.SendMessage(getparameter_response);

                CloseConnection("teardown");
            }
        }

        private void RTSP_ProcessAuthorization(RtspRequest message, RtspListener listener)
        {
            bool authorized = false;
            if (message.Headers.ContainsKey("Authorization") == true)
            {
                // The Header contained Authorization
                // Check the message has the correct Authorization
                // If it does not have the correct Authorization then close the RTSP connection
                authorized = _auth.IsValid(message);

                if (authorized == false)
                {
                    // Send a 401 Authentication Failed reply, then close the RTSP Socket
                    Rtsp.Messages.RtspResponse authorization_response = message.CreateResponse(_logger);
                    authorization_response.AddHeader("WWW-Authenticate: " + _auth.GetHeader());
                    authorization_response.ReturnCode = 401;
                    listener.SendMessage(authorization_response);

                    CloseConnection("unauthorized");
                    listener.Dispose();
                    return;

                }
            }
            if ((message.Headers.ContainsKey("Authorization") == false))
            {
                // Send a 401 Authentication Failed with extra info in WWW-Authenticate
                // to tell the Client if we are using Basic or Digest Authentication
                Rtsp.Messages.RtspResponse authorization_response = message.CreateResponse(_logger);
                authorization_response.AddHeader("WWW-Authenticate: " + _auth.GetHeader()); // 'Basic' or 'Digest'
                authorization_response.ReturnCode = 401;
                listener.SendMessage(authorization_response);
                return;
            }
        }

        // Process each RTSP message that is received
        private void RTSP_Message_Received(object sender, RtspChunkEventArgs e)
        {
            // Cast the 'sender' and 'e' into the RTSP Listener (the Socket) and the RTSP Message
            Rtsp.RtspListener listener = sender as Rtsp.RtspListener;
            Rtsp.Messages.RtspMessage message = e.Message as Rtsp.Messages.RtspMessage;

            _logger.Debug($"{listener.ConnectionId} RTSP message received " + message);     

            
            // Check if the RTSP Message has valid authentication (validating against username,password,realm and nonce)
            if (_auth != null) {
                RTSP_ProcessAuthorization(message as RtspRequest, listener);
            }


            // Update the RTSP Keepalive Timeout
            // We could check that the message is GET_PARAMETER or OPTIONS for a keepalive but instead we will update the timer on any message
            _timeSinceLastRtcpKeepAlive = DateTime.UtcNow;



            // Handle OPTIONS message
            if (message is Rtsp.Messages.RtspRequestOptions)
            {
                RTSP_ProcessOptionsRequest(message as RtspRequestOptions, listener);
            }

            // Handle DESCRIBE message
            if (message is Rtsp.Messages.RtspRequestDescribe)
            {
                RTSP_ProcessDescribeRequest(message as RtspRequestDescribe, listener);
            }

            // Handle SETUP message
            if (message is Rtsp.Messages.RtspRequestSetup)
            {
                RTSP_ProcessSetupRequest(message as RtspRequestSetup, listener);               
            }

            // Handle PLAY message (Sent with a Session ID)
            if (message is Rtsp.Messages.RtspRequestPlay)        
            {
                RTSP_ProcessPlayRequest(message as RtspRequestPlay, listener);
            }

            // Handle PAUSE message (Sent with a Session ID)
            if (message is Rtsp.Messages.RtspRequestPause)
            {
                RTSP_ProcessPauseRequest(message as RtspRequestPause, listener);
            }


            // Handle GET_PARAMETER message, often used as a Keep Alive
            if (message is Rtsp.Messages.RtspRequestGetParameter)
            {
                RTSP_ProcessGetParameterRequest(message as RtspRequestGetParameter, listener);
            }


            // Handle TEARDOWN (sent with a Session ID)
            if (message is Rtsp.Messages.RtspRequestTeardown)
            {
                RTSP_ProcessTeardownRequest(message as RtspRequestTeardown, listener);
            }


        }

    }
}

