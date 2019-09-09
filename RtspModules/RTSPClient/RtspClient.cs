using Rtsp.Messages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using VideoGate.Infrastructure.Interfaces;
using VideoGate.Infrastructure.Models;
using System.Net;
using Rtsp;
using System.Net.Sockets;
using NLog;
using System.Threading.Tasks;

namespace RTSPClient
{
    public class RtspClient : IRtspClient
    {
       
        // Events that applications can receive
        public event Received_SPS_PPS_Delegate  Received_SPS_PPS;
        public event Received_VPS_SPS_PPS_Delegate Received_VPS_SPS_PPS;
        public event Received_NALs_Delegate     Received_NALs;
        public event Received_G711_Delegate     Received_G711;
		public event Received_AMR_Delegate      Received_AMR;
        public event Received_AAC_Delegate      Received_AAC;
        
        public event Received_Rtp_Data_Delegate Received_Rtp;
        public event Received_Rtcp_Data_Delegate Received_Rtcp;
        public event Rtsp_Client_Started_Delegate OnStarted;
        public event Rtsp_Client_Stopped_Delegate OnStopped;

        // Delegated functions (essentially the function prototype)
        public delegate void Received_SPS_PPS_Delegate (byte[] sps, byte[] pps); // H264
        public delegate void Received_VPS_SPS_PPS_Delegate(byte[] vps, byte[] sps, byte[] pps); // H265
        public delegate void Received_NALs_Delegate (List<byte[]> nal_units); // H264 or H265
        public delegate void Received_G711_Delegate (String format, List<byte[]> g711);
		public delegate void Received_AMR_Delegate (String format, List<byte[]> amr);
        public delegate void Received_AAC_Delegate(String format, List<byte[]> aac, uint ObjectType, uint FrequencyIndex, uint ChannelConfiguration);

        public enum RTP_TRANSPORT { UDP, TCP, MULTICAST, UNKNOWN };
        private enum RTSP_STATUS { WaitingToConnect, Connecting, ConnectFailed, Connected };

        Rtsp.RtspTcpTransport _rtspSocket = null; // RTSP connection
        volatile RTSP_STATUS rtspSocketStatus = RTSP_STATUS.WaitingToConnect;
        Rtsp.RtspListener _rtspClient = null;   // this wraps around a the RTSP tcp_socket stream
        RTP_TRANSPORT _rtpTransport = RTP_TRANSPORT.UNKNOWN; // Mode, either RTP over UDP or RTP over TCP using the RTSP socket
        Rtsp.UDPSocket _videoUdpPair = null;       // Pair of UDP ports used in RTP over UDP mode or in MULTICAST mode
        Rtsp.UDPSocket _audioUdpPair = null;       // Pair of UDP ports used in RTP over UDP mode or in MULTICAST mode
        String _url = "";                 // RTSP URL (username & password will be stripped out
        String _username = "";            // Username
        String _password = "";            // Password
        String _hostname = "";            // RTSP Server hostname or IP address
        int _port = 0;                    // RTSP Server TCP Port number
        String _session = "";             // RTSP Session
		String _authType = null;         // cached from most recent WWW-Authenticate reply
        String _realm = null;             // cached from most recent WWW-Authenticate reply
        String _nonce = null;             // cached from most recent WWW-Authenticate reply
        uint   _ssrc = 12345;
        Uri _videoUri = null;            // URI used for the Video Track
        int _videoPayload = -1;          // Payload Type for the Video. (often 96 which is the first dynamic payload value. Bosch use 35)
        int _videoВataСhannel = -1;     // RTP Channel Number used for the video RTP stream or the UDP port number
        int _videoRtcpChannel = -1;     // RTP Channel Number used for the video RTCP status report messages OR the UDP port number
        bool _h264SpsPpsFired = false; // True if the SDP included a sprop-Parameter-Set for H264 video
        bool _h265VpsSpsPpsFired = false; // True if the SDP included a sprop-vps, sprop-sps and sprop_pps for H265 video
        string _videoCodec = "";         // Codec used with Payload Types 96..127 (eg "H264")

        Uri _audioUrl = null;            // URI used for the Audio Track
        int _audioPayload = -1;          // Payload Type for the Video. (often 96 which is the first dynamic payload value)
        int _audioDataChannel = -1;     // RTP Channel Number used for the audio RTP stream or the UDP port number
        int _audioRtcpChannel = -1;     // RTP Channel Number used for the audio RTCP status report messages OR the UDP port number
        string _audioCodec = "";         // Codec used with Payload Types (eg "PCMA" or "AMR")

        bool _serverSupportsGetParameter = false; // Used with RTSP keepalive
        bool _serverSupportsSetParameter = false; // Used with RTSP keepalive
        System.Timers.Timer _keepaliveTimer = null; // Used with RTSP keepalive

        Rtsp.H264Payload _h264Payload = null;
        Rtsp.H265Payload _h265Payload = null;
        Rtsp.G711Payload _g711Payload = new Rtsp.G711Payload();
		Rtsp.AMRPayload _amrPayload = new Rtsp.AMRPayload();
        Rtsp.AACPayload _aacPayload = null;

        List<Rtsp.Messages.RtspRequestSetup> _setupMessages = new List<Rtsp.Messages.RtspRequestSetup>(); // setup messages still to send

        ManualResetEvent _readyEvent = new ManualResetEvent(false);
        const int DEFAULT_READY_TIMEOUT = 10000;

        readonly int _readyTimeout;
        readonly VideoSource _videoSource;
        readonly IPAddress _ipAddress;
        readonly ILogger _logger = LogManager.GetLogger("RtspClient");

        const int  MAX_RESEND_MESSAGE_TRYS = 1;
        int _resendMessageTrys = 0;
        bool _playing;
        
        public bool ParseMessageData = false;
        

        // Constructor
        public RtspClient(VideoSource videoSource, IPAddress ipAddress) {
            _videoSource = videoSource;
            _ipAddress = ipAddress;
            _readyTimeout = _videoSource.Timeout == 0 ? DEFAULT_READY_TIMEOUT : _videoSource.Timeout;
        }

        public byte[] SdpData {get; private set;}

        public Guid VideoSourceId => _videoSource.Id;

        public int VideoRtpChannel => _videoВataСhannel;

        public int VideoRtcpChannel => _videoRtcpChannel;

        public int AudioRtpChannel => _audioDataChannel;

        public int AudioRtcpChannel => _audioRtcpChannel;

        public bool IsRunning => rtspSocketStatus == RTSP_STATUS.Connected;

        public void Start()
        {
             _logger.Info($"Start rtsp client for source {VideoSourceId}");
            Connect(_videoSource.Url, _videoSource.UseTCP ? RTP_TRANSPORT.TCP : RTP_TRANSPORT.UDP);
        }

        public void Connect(String url, RTP_TRANSPORT rtp_transport)
        {

            Rtsp.RtspUtils.RegisterUri();

            _logger.Debug($"{VideoSourceId} Connecting to " + url);
            this._url = url;

            // Use URI to extract username and password
            // and to make a new URL without the username and password
            try {
                Uri uri = new Uri(this._url);
                _hostname = uri.Host;
                _port = uri.Port;

                if (uri.UserInfo.Length > 0) {
                    _username = uri.UserInfo.Split(new char[] {':'})[0];
                    _password = uri.UserInfo.Split(new char[] {':'})[1];
                    this._url = uri.GetComponents((UriComponents.AbsoluteUri &~ UriComponents.UserInfo),
                                                 UriFormat.UriEscaped);
                }
            } catch {
                _username = null;
                _password = null;
            }

            // Connect to a RTSP Server. The RTSP session is a TCP connection
            rtspSocketStatus = RTSP_STATUS.Connecting;
            try
            {
                _rtspSocket = new Rtsp.RtspTcpTransport(_hostname, _port);
            }
            catch
            {
                rtspSocketStatus = RTSP_STATUS.ConnectFailed;
                _logger.Warn($"{VideoSourceId} Error - connection failed");
                Stop(RtspClientStopReason.CONNECTION_FAILED);
                return;
            }

            if (_rtspSocket.Connected == false)
            {
                rtspSocketStatus = RTSP_STATUS.ConnectFailed;
                _logger.Warn($"{VideoSourceId} Error - connection failed");
                Stop(RtspClientStopReason.CONNECTION_FAILED);
                return;
            }

            rtspSocketStatus = RTSP_STATUS.Connected;

            // Connect a RTSP Listener to the RTSP Socket (or other Stream) to send RTSP messages and listen for RTSP replies
            _rtspClient = new Rtsp.RtspListener(_rtspSocket,"RtspClient",VideoSourceId, Guid.Empty);

            _rtspClient.AutoReconnect = false;

            _rtspClient.MessageReceived += Rtsp_MessageReceived;
            _rtspClient.DataReceived += Rtp_DataReceived;
            _rtspClient.SocketExceptionRaised += RTSP_SocketException_Raised;
            _rtspClient.Disconnected += RTSP_Disconnected;

            _rtspClient.Start(); // start listening for messages from the server (messages fire the MessageReceived event)


            // Check the RTP Transport
            // If the RTP transport is TCP then we interleave the RTP packets in the RTSP stream
            // If the RTP transport is UDP, we initialise two UDP sockets (one for video, one for RTCP status messages)
            // If the RTP transport is MULTICAST, we have to wait for the SETUP message to get the Multicast Address from the RTSP server
            this._rtpTransport = rtp_transport;
            if (rtp_transport == RTP_TRANSPORT.UDP)
            {
                 _logger.Trace($"{VideoSourceId} Start setting UDP ports");
                _videoUdpPair = new Rtsp.UDPSocket(_ipAddress, 50000, 51000); // give a range of 500 pairs (1000 addresses) to try incase some address are in use
                _videoUdpPair.DataReceived += Rtp_DataReceived;
                _videoUdpPair.ControlReceived += Rtcp_DataReceived;
                _videoUdpPair.Start(); // start listening for data on the UDP ports
                _audioUdpPair = new Rtsp.UDPSocket(_ipAddress,50000, 51000); // give a range of 500 pairs (1000 addresses) to try incase some address are in use
                _audioUdpPair.DataReceived += Rtp_DataReceived;
                _audioUdpPair.ControlReceived += Rtcp_DataReceived;
                _audioUdpPair.Start(); // start listening for data on the UDP ports
                _logger.Trace($"{VideoSourceId} End setting UDP ports: video {_videoUdpPair._dataPort}-{_videoUdpPair._controlPort}"+
                $" audio {_audioUdpPair._dataPort}-{_audioUdpPair._controlPort}");
            }
            if (rtp_transport == RTP_TRANSPORT.TCP)
            {
                // Nothing to do. Data will arrive in the RTSP Listener
            }
            if (rtp_transport == RTP_TRANSPORT.MULTICAST)
            {
                // Nothing to do. Will open Multicast UDP sockets after the SETUP command
            }


            // Send OPTIONS
            // In the Received Message handler we will send DESCRIBE, SETUP and PLAY
            Rtsp.Messages.RtspRequest options_message = new Rtsp.Messages.RtspRequestOptions(_logger);
            options_message.RtspUri = new Uri(this._url);
            _rtspClient.SendMessage(options_message);
        }

        // return true if this connection failed, or if it connected but is no longer connected.
        public bool StreamingFinished() {
            if (rtspSocketStatus == RTSP_STATUS.ConnectFailed) return true;
            if (rtspSocketStatus == RTSP_STATUS.Connected && _rtspSocket.Connected == false) return true;
            else return false;
        }


        public void Pause()
        {
            if (_rtspClient != null) {
				// Send PAUSE
                Rtsp.Messages.RtspRequest pause_message = new Rtsp.Messages.RtspRequestPause(_logger);
                pause_message.RtspUri = new Uri(_url);
                pause_message.Session = _session;
				if (_authType != null) {
                    AddAuthorization(pause_message,_username,_password,_authType,_realm,_nonce,_url);
                }
                _rtspClient.SendMessage(pause_message);
            }
        }

        public void Play()
        {
            if (_rtspClient != null) {
				// Send PLAY
                Rtsp.Messages.RtspRequest play_message = new Rtsp.Messages.RtspRequestPlay(_logger);
                play_message.RtspUri = new Uri(_url);
                play_message.Session = _session;
				if (_authType != null) {
                    AddAuthorization(play_message,_username,_password,_authType,_realm,_nonce,_url);
                }
                _rtspClient.SendMessage(play_message);
            }
        }

        private void TrySendTeardown()
        {
            try
            {
                
                Rtsp.Messages.RtspRequest teardown_message = new Rtsp.Messages.RtspRequestTeardown(_logger);
                teardown_message.RtspUri = new Uri(_url);
                teardown_message.Session = _session;
                if (_authType != null) 
                {
                     AddAuthorization(teardown_message,_username,_password,_authType,_realm,_nonce,_url);            
                    _rtspClient.SendMessage(teardown_message);  
                }

            }
            catch(Exception ex)
            {
                _logger.Warn($"Error on sending teardown rtsp client for source {VideoSourceId}: {ex}");
            }
            
        }

        const int WAIT_FOR_TEARDOWN_RESPONSE = 1000;
        CancellationTokenSource _waitTeardownResponseCancellationTokenSource = new CancellationTokenSource();
        public void Stop(RtspClientStopReason reason)
        {
            if (_rtspClient == null)
                return;
            lock(_rtspClient)
            {
                
                            
                if (_rtspClient != null && reason == RtspClientStopReason.COMMAND) 
                {
                    TrySendTeardown();
                    Task.Run(() => 
                    {
                        try
                        {
                            Task.Delay(WAIT_FOR_TEARDOWN_RESPONSE,_waitTeardownResponseCancellationTokenSource.Token).Wait();
                            Stop(RtspClientStopReason.SESSION_CLOSED);
                        }
                        catch(OperationCanceledException)
                        {
                            _logger.Trace($"{VideoSourceId} wait for Teardown response cancelled");
                        }
                        
                    });
                    return;
                }
                
                _logger.Info($"Stop rtsp client for source {VideoSourceId}. Reason: {reason}");
                _waitTeardownResponseCancellationTokenSource.Cancel();
                try
                {
                    // Stop the keepalive timer
                    if (_keepaliveTimer != null) 
                    {
                        _keepaliveTimer.Stop();
                        _keepaliveTimer.Dispose();
                        _keepaliveTimer = null;
                    }

                    // clear up any UDP sockets
                    if (_videoUdpPair != null) 
                    {
                        _videoUdpPair.Stop();
                        _videoUdpPair = null;
                    }
                    if (_audioUdpPair != null) 
                    {
                        _audioUdpPair.Stop();
                        _audioUdpPair = null;
                    }

                    // Drop the RTSP session
                    if (_rtspClient != null) 
                    {
                        _rtspClient.Stop();
                        _rtspClient = null;
                    }
                }
                catch(Exception ex)
                {
                    _logger.Warn($"Error on closing rtsp client for source {VideoSourceId}: {ex}");
                }
                _readyEvent.Set();

                var handler = OnStopped;
                if (handler != null)
                {
                    handler(this,reason);
                }

            }
            

        }

        public bool WaitReady()
        {
            return _readyEvent.WaitOne(_readyTimeout) && SdpData != null && _playing;
        }


        public void Rtcp_DataReceived(object sender, Rtsp.RtspChunkEventArgs e)
        {
            Rtsp.Messages.RtspData data_received = e.Message as Rtsp.Messages.RtspData;

            if (data_received.Channel == _videoRtcpChannel || data_received.Channel == _audioRtcpChannel)
            {
                var handler = Received_Rtcp;
                if (handler != null)
                    handler(this,data_received.Channel, data_received.Data);
                _logger.Debug("Received a RTCP message on channel " + data_received.Channel);

                // RTCP Packet
                // - Version, Padding and Receiver Report Count
                // - Packet Type
                // - Length
                // - SSRC
                // - payload

                // There can be multiple RTCP packets transmitted together. Loop ever each one

                long packetIndex = 0;
                while (packetIndex < e.Message.Data.Length) {
                    
                    int rtcp_version = (e.Message.Data[packetIndex+0] >> 6);
                    int rtcp_padding = (e.Message.Data[packetIndex+0] >> 5) & 0x01;
                    int rtcp_reception_report_count = (e.Message.Data[packetIndex+0] & 0x1F);
                    byte rtcp_packet_type = e.Message.Data[packetIndex+1]; // Values from 200 to 207
                    uint rtcp_length = (uint)(e.Message.Data[packetIndex+2] << 8) + (uint)(e.Message.Data[packetIndex+3]); // number of 32 bit words
                    uint rtcp_ssrc = (uint)(e.Message.Data[packetIndex+4] << 24) + (uint)(e.Message.Data[packetIndex+5] << 16)
                        + (uint)(e.Message.Data[packetIndex+6] << 8) + (uint)(e.Message.Data[packetIndex+7]);

                    // 200 = SR = Sender Report
                    // 201 = RR = Receiver Report
                    // 202 = SDES = Source Description
                    // 203 = Bye = Goodbye
                    // 204 = APP = Application Specific Method
                    // 207 = XR = Extended Reports

                    _logger.Debug("RTCP Data. PacketType=" + rtcp_packet_type
                                      + " SSRC=" +  rtcp_ssrc);

                    if (rtcp_packet_type == 200) {
                        // We have received a Sender Report
                        // Use it to convert the RTP timestamp into the UTC time

                        UInt32 ntp_msw_seconds = (uint)(e.Message.Data[packetIndex + 8] << 24) + (uint)(e.Message.Data[packetIndex + 9] << 16)
                        + (uint)(e.Message.Data[packetIndex + 10] << 8) + (uint)(e.Message.Data[packetIndex + 11]);

                        UInt32 ntp_lsw_fractions = (uint)(e.Message.Data[packetIndex + 12] << 24) + (uint)(e.Message.Data[packetIndex + 13] << 16)
                        + (uint)(e.Message.Data[packetIndex + 14] << 8) + (uint)(e.Message.Data[packetIndex + 15]);

                        UInt32 rtp_timestamp = (uint)(e.Message.Data[packetIndex + 16] << 24) + (uint)(e.Message.Data[packetIndex + 17] << 16)
                        + (uint)(e.Message.Data[packetIndex + 18] << 8) + (uint)(e.Message.Data[packetIndex + 19]);

                        double ntp = ntp_msw_seconds + (ntp_lsw_fractions / UInt32.MaxValue);

                        // NTP Most Signigicant Word is relative to 0h, 1 Jan 1900
                        // This will wrap around in 2036
                        DateTime time = new DateTime(1900,1,1,0,0,0,DateTimeKind.Utc);

                        time = time.AddSeconds((double)ntp_msw_seconds); // adds 'double' (whole&fraction)

                        _logger.Debug("RTCP time (UTC) for RTP timestamp " + rtp_timestamp + " is " + time);

                        // Send a Receiver Report
                        try
                        {
                            byte[] rtcp_receiver_report = new byte[8];
                            int version = 2;
                            int paddingBit = 0;
                            int reportCount = 0; // an empty report
                            int packetType = 201; // Receiver Report
                            int length = (rtcp_receiver_report.Length/4) - 1; // num 32 bit words minus 1
                            rtcp_receiver_report[0] = (byte)((version << 6) + (paddingBit << 5) + reportCount);
                            rtcp_receiver_report[1] = (byte)(packetType);
                            rtcp_receiver_report[2] = (byte)((length >> 8) & 0xFF);
                            rtcp_receiver_report[3] = (byte)((length >> 0) & 0XFF);
                            rtcp_receiver_report[4] = (byte)((_ssrc >> 24) & 0xFF);
                            rtcp_receiver_report[5] = (byte)((_ssrc >> 16) & 0xFF);
                            rtcp_receiver_report[6] = (byte)((_ssrc >> 8) & 0xFF);
                            rtcp_receiver_report[7] = (byte)((_ssrc >> 0) & 0xFF);

                            if (_rtpTransport == RTP_TRANSPORT.TCP) {
                                // Send it over via the RTSP connection
                                _rtspClient.SendData(_videoRtcpChannel,rtcp_receiver_report);
                            }
                            if (_rtpTransport == RTP_TRANSPORT.UDP || _rtpTransport == RTP_TRANSPORT.MULTICAST) {
                                // Send it via a UDP Packet
                                _logger.Debug("TODO - Need to implement RTCP over UDP");
                            }

                        }
                        catch
                        {
                            _logger.Debug("Error writing RTCP packet");
                        }
                    }

                    packetIndex = packetIndex + ((rtcp_length + 1) * 4);
                }
                return;
            }

           
        }


        private void Rtp_ParseMessageData(byte[] data, out int rtp_payload_type, out int rtp_payload_start, out int rtp_marker, bool trace)
        {
            
            // RTP Packet Header
            // 0 - Version, P, X, CC, M, PT and Sequence Number
            //32 - Timestamp
            //64 - SSRC
            //96 - CSRCs (optional)
            //nn - Extension ID and Length
            //nn - Extension header

            int rtp_version = (data[0] >> 6);
            int rtp_padding = (data[0] >> 5) & 0x01;
            int rtp_extension = (data[0] >> 4) & 0x01;
            int rtp_csrc_count = (data[0] >> 0) & 0x0F;
            rtp_marker = (data[1] >> 7) & 0x01;
            rtp_payload_type = (data[1] >> 0) & 0x7F;
            uint rtp_sequence_number = ((uint)data[2] << 8) + (uint)(data[3]);
            uint rtp_timestamp = ((uint)data[4] << 24) + (uint)(data[5] << 16) + (uint)(data[6] << 8) + (uint)(data[7]);
            uint rtp_ssrc = ((uint)data[8] << 24) + (uint)(data[9] << 16) + (uint)(data[10] << 8) + (uint)(data[11]);

            rtp_payload_start = 4 // V,P,M,SEQ
                                + 4 // time stamp
                                + 4 // ssrc
                                + (4 * rtp_csrc_count); // zero or more csrcs

            uint rtp_extension_id = 0;
            uint rtp_extension_size = 0;
            if (rtp_extension == 1)
            {
                rtp_extension_id = ((uint)data[rtp_payload_start + 0] << 8) + (uint)(data[rtp_payload_start + 1] << 0);
                rtp_extension_size = ((uint)data[rtp_payload_start + 2] << 8) + (uint)(data[rtp_payload_start + 3] << 0) * 4; // units of extension_size is 4-bytes
                rtp_payload_start += 4 + (int)rtp_extension_size;  // extension header and extension payload
            }

            if (trace){
                _logger.Trace("RTP Data"
                + " V=" + rtp_version
                + " P=" + rtp_padding
                + " X=" + rtp_extension
                + " CC=" + rtp_csrc_count
                + " M=" + rtp_marker
                + " PT=" + rtp_payload_type
                + " Seq=" + rtp_sequence_number
                + " Time (MS)=" + rtp_timestamp / 90 // convert from 90kHZ clock to ms
                + " SSRC=" + rtp_ssrc
                + " Size=" + data.Length);
            }
            
        }

        private byte[] Rtp_ExtractPayload(byte[] data, int rtp_payload_start)
        {
            byte[] rtp_payload = new byte[data.Length - rtp_payload_start]; // payload with RTP header removed
            System.Array.Copy(data, rtp_payload_start, rtp_payload, 0, rtp_payload.Length); // copy payload
            return rtp_payload;
        }

        private void Rtp_ProcessH264Payload(byte[] rtp_payload, int rtp_marker)
        {
            // H264 RTP Packet

            // If rtp_marker is '1' then this is the final transmission for this packet.
            // If rtp_marker is '0' we need to accumulate data with the same timestamp

            // ToDo - Check Timestamp
            // Add the RTP packet to the tempoary_rtp list until we have a complete 'Frame'

            List<byte[]> nal_units = _h264Payload.Process_H264_RTP_Packet(rtp_payload, rtp_marker); // this will cache the Packets until there is a Frame

            if (nal_units == null) {
                // we have not passed in enough RTP packets to make a Frame of video
            } else {
                // If we did not have a SPS and PPS in the SDP then search for the SPS and PPS
                // in the NALs and fire the Received_SPS_PPS event.
                // We assume the SPS and PPS are in the same Frame.
                if (_h264SpsPpsFired == false) {

                    // Check this frame for SPS and PPS
                    byte[] sps = null;
                    byte[] pps = null;
                    foreach (byte[] nal_unit in nal_units) {
                        if (nal_unit.Length > 0)
                        {
                            int nal_ref_idc = (nal_unit[0] >> 5) & 0x03;
                            int nal_unit_type = nal_unit[0] & 0x1F;

                            if (nal_unit_type == 7) sps = nal_unit; // SPS
                            if (nal_unit_type == 8) pps = nal_unit; // PPS
                        }
                    }
                    if (sps != null && pps != null) {
                        // Fire the Event
                        if (Received_SPS_PPS != null)
                        {
                            Received_SPS_PPS(sps, pps);
                        }
                        _h264SpsPpsFired = true;
                    }
                }

                // we have a frame of NAL Units. Write them to the file
                if (Received_NALs != null) {
                    Received_NALs(nal_units);
                }
            }
        }

        private void Rtp_ProcessH265Payload(byte[] rtp_payload, int rtp_marker)
        {
            // H265 RTP Packet

            // If rtp_marker is '1' then this is the final transmission for this packet.
            // If rtp_marker is '0' we need to accumulate data with the same timestamp

            // Add the RTP packet to the tempoary_rtp list until we have a complete 'Frame'

            List<byte[]> nal_units = _h265Payload.Process_H265_RTP_Packet(rtp_payload, rtp_marker); // this will cache the Packets until there is a Frame

            if (nal_units == null)
            {
                // we have not passed in enough RTP packets to make a Frame of video
            }
            else
            {
                // If we did not have a VPS, SPS and PPS in the SDP then search for the VPS SPS and PPS
                // in the NALs and fire the Received_VPS_SPS_PPS event.
                // We assume the VPS, SPS and PPS are in the same Frame.
                if (_h265VpsSpsPpsFired == false)
                {

                    // Check this frame for VPS, SPS and PPS
                    byte[] vps = null;
                    byte[] sps = null;
                    byte[] pps = null;
                    foreach (byte[] nal_unit in nal_units)
                    {
                        if (nal_unit.Length > 0)
                        {
                            int nal_unit_type = (nal_unit[0] >> 1) & 0x3F;

                            if (nal_unit_type == 32) vps = nal_unit; // VPS
                            if (nal_unit_type == 33) sps = nal_unit; // SPS
                            if (nal_unit_type == 34) pps = nal_unit; // PPS
                        }
                    }
                    if (vps != null &&  sps != null && pps != null)
                    {
                        // Fire the Event
                        if (Received_VPS_SPS_PPS != null)
                        {
                            Received_VPS_SPS_PPS(vps, sps, pps);
                        }
                        _h265VpsSpsPpsFired = true;
                    }
                }

                // we have a frame of NAL Units. Write them to the file
                if (Received_NALs != null)
                {
                    Received_NALs(nal_units);
                }
            }
        }

        private void Rtp_ProcessG711Payload(byte[] rtp_payload, int rtp_marker)
        {
            // G711 PCMA or G711 PCMU
            List<byte[]> audio_frames = _g711Payload.Process_G711_RTP_Packet(rtp_payload, rtp_marker);

            if (audio_frames == null) {
                // some error
            } else {
                // Write the audio frames to the file
                if (Received_G711 != null) {
                    Received_G711(_audioCodec, audio_frames);
                }
            }
        }

        private void Rtp_ProcessAMRPayload(byte[] rtp_payload, int rtp_marker)
        {
            //AMR
            List<byte[]> audio_frames = _amrPayload.Process_AMR_RTP_Packet(rtp_payload, rtp_marker);

            if (audio_frames == null) {
                // some error
            } else {
                // Write the audio frames to the file
                if (Received_AMR != null) {
                    Received_AMR(_audioCodec, audio_frames);
                }
            }
        }

        private void Rtp_ProcessAACPayload(byte[] rtp_payload, int rtp_marker)
        {
            //AAC
            List<byte[]> audio_frames = _aacPayload.Process_AAC_RTP_Packet(rtp_payload, rtp_marker);

            if (audio_frames == null) {
                // some error
            } else {
                // Write the audio frames to the file
                if (Received_AAC != null) {
                    Received_AAC(_audioCodec, audio_frames, _aacPayload.ObjectType, _aacPayload.FrequencyIndex, _aacPayload.ChannelConfiguration);
                }
            }
        }

        int rtp_count = 0; // used for statistics
        // RTP packet (or RTCP packet) has been received.
        public void Rtp_DataReceived(object sender, Rtsp.RtspChunkEventArgs e)
        {
            Rtsp.Messages.RtspData data_received = e.Message as Rtsp.Messages.RtspData;

            // Check which channel the Data was received on.
            // eg the Video Channel, the Video Control Channel (RTCP)
            // the Audio Channel or the Audio Control Channel (RTCP)

          if (data_received.Channel == _videoВataСhannel || data_received.Channel == _audioDataChannel)
            {
                var handler = Received_Rtp;
                if (handler != null)
                {
                    handler(this,data_received.Channel,e.Message.Data);
                }
                
                if (false == ParseMessageData)
                {
                    return;
                }

                int rtp_payload_type;
                int rtp_payload_start;
                int rtp_marker;

                Rtp_ParseMessageData(e.Message.Data,out rtp_payload_type, out rtp_payload_start, out rtp_marker, false);

                // Check the payload type in the RTP packet matches the Payload Type value from the SDP
                if (data_received.Channel == _videoВataСhannel && rtp_payload_type != _videoPayload)
                {
                    _logger.Debug("Ignoring this Video RTP payload");
                    return; // ignore this data
                }

                // Check the payload type in the RTP packet matches the Payload Type value from the SDP
                else if (data_received.Channel == _audioDataChannel && rtp_payload_type != _audioPayload)
                {
                    _logger.Debug("Ignoring this Audio RTP payload");
                    return; // ignore this data
                }
                else if (data_received.Channel == _videoВataСhannel
                         && rtp_payload_type == _videoPayload
                         && _videoCodec.Equals("H264")) 
                {
                    byte[] rtp_payload = Rtp_ExtractPayload(e.Message.Data, rtp_payload_start); 
                    Rtp_ProcessH264Payload(rtp_payload,rtp_marker);
                }
                else if (data_received.Channel == _videoВataСhannel
                         && rtp_payload_type == _videoPayload
                         && _videoCodec.Equals("H265"))
                {                    
                    byte[] rtp_payload = Rtp_ExtractPayload(e.Message.Data, rtp_payload_start); 
                    Rtp_ProcessH265Payload(rtp_payload,rtp_marker);                    
                }
                else if (data_received.Channel == _audioDataChannel && (rtp_payload_type == 0 || rtp_payload_type == 8 || _audioCodec.Equals("PCMA") || _audioCodec.Equals("PCMU"))) {
                    byte[] rtp_payload = Rtp_ExtractPayload(e.Message.Data, rtp_payload_start); 
                    Rtp_ProcessG711Payload(rtp_payload,rtp_marker);
                }
                else if (data_received.Channel == _audioDataChannel
                          && rtp_payload_type == _audioPayload
                          && _audioCodec.Equals("AMR")) {
                    byte[] rtp_payload = Rtp_ExtractPayload(e.Message.Data, rtp_payload_start); 
                    Rtp_ProcessAMRPayload(rtp_payload,rtp_marker);                    
                }
                else if (data_received.Channel == _audioDataChannel
                         && rtp_payload_type == _audioPayload
                         && _audioCodec.Equals("MPEG4-GENERIC")
                        && _aacPayload != null)
                {
                    byte[] rtp_payload = Rtp_ExtractPayload(e.Message.Data, rtp_payload_start); 
                    Rtp_ProcessAACPayload(rtp_payload,rtp_marker);  
                }
                else if (data_received.Channel == _videoВataСhannel && rtp_payload_type == 26) {
                    _logger.Warn("No parser has been written for JPEG RTP packets. Please help write one");
                    return; // ignore this data
                }
                else {
                    _logger.Warn("No parser for RTP payload " + rtp_payload_type);
                }
            }
        }

        private void Rtsp_ProcessResponseAuthorization(RtspResponse message)
        {
            // Process the WWW-Authenticate header
            // EG:   Basic realm="AProxy"
            // EG:   Digest realm="AXIS_WS_ACCC8E3A0A8F", nonce="000057c3Y810622bff50b36005eb5efeae118626a161bf", stale=FALSE
            // EG:   Digest realm="IP Camera(21388)", nonce="534407f373af1bdff561b7b4da295354", stale="FALSE"

            String www_authenticate = message.Headers[RtspHeaderNames.WWWAuthenticate];
            String auth_params = "";

            if (www_authenticate.StartsWith("basic",StringComparison.InvariantCultureIgnoreCase)) {
                _authType = "Basic";
                auth_params = www_authenticate.Substring(5);
            }
            if (www_authenticate.StartsWith("digest",StringComparison.InvariantCultureIgnoreCase)) {
                _authType = "Digest";
                auth_params = www_authenticate.Substring(6);
            }

            string[] items = auth_params.Split(new char[] { ',' }); // NOTE, does not handle Commas in Quotes

            foreach (string item in items) {
                // Split on the = symbol and update the realm and nonce
                string[] parts = item.Trim().Split(new char[] {'='},2); // max 2 parts in the results array
                if (parts.Count() >= 2 && parts[0].Trim().Equals("realm")) {
                    _realm = parts[1].Trim(new char[] {' ','\"'}); // trim space and quotes
                }
                else if (parts.Count() >= 2 && parts[0].Trim().Equals("nonce")) {
                    _nonce = parts[1].Trim(new char[] {' ','\"'}); // trim space and quotes
                }
            }

            _logger.Debug($"{VideoSourceId} WWW Authorize parsed for " + _authType + " " + _realm + " " + _nonce);
        }

        private void Rtsp_ProcessResponseOptions(RtspResponse message)
        {
            // If we get a reply to OPTIONS then start the Keepalive Timer and send DESCRIBE
            
            // Check the capabilities returned by OPTIONS
            // The Public: header contains the list of commands the RTSP server supports
            // Eg   DESCRIBE, SETUP, TEARDOWN, PLAY, PAUSE, OPTIONS, ANNOUNCE, RECORD, GET_PARAMETER]}
            if (message.Headers.ContainsKey(RtspHeaderNames.Public))
            {
                string[] parts = message.Headers[RtspHeaderNames.Public].Split(',');
                foreach (String part in parts) {
                    if (part.Trim().ToUpper().Equals("GET_PARAMETER")) _serverSupportsGetParameter = true;
                    if (part.Trim().ToUpper().Equals("SET_PARAMETER")) _serverSupportsSetParameter = true;
                }
            }

            if (_keepaliveTimer == null)
            {
                // Start a Timer to send an Keepalive RTSP command every 20 seconds
                _keepaliveTimer = new System.Timers.Timer();
                _keepaliveTimer.Elapsed += Timer_Elapsed;
                _keepaliveTimer.Interval = 20 * 1000;
                _keepaliveTimer.Enabled = true;

                // Send DESCRIBE
                Rtsp.Messages.RtspRequest describe_message = new Rtsp.Messages.RtspRequestDescribe(_logger);
                describe_message.RtspUri = new Uri(_url);
                if (_authType != null) {
                    AddAuthorization(describe_message,_username,_password,_authType,_realm,_nonce,_url);
                }
                _rtspClient.SendMessage(describe_message);
            }
            else
            {
                // If the Keepalive Timer was not null, the OPTIONS reply may have come from a Keepalive
                // So no need to generate a DESCRIBE message
                // do nothing
            }
        }

        private void SdpParseMediaAttributes(Rtsp.Sdp.Media media, bool video,bool audio, ref string control, ref Rtsp.Sdp.AttributFmtp fmtp)
        {
            // search the attributes for control, rtpmap and fmtp
            // (fmtp only applies to video)
            foreach (Rtsp.Sdp.Attribut attrib in media.Attributs) {
                if (attrib.Key.Equals("control")) {
                    String sdp_control = attrib.Value;
                    if (sdp_control.ToLower().StartsWith("rtsp://")) {
                        control = sdp_control; //absolute path
                    } else {
                        control = _url + "/" + sdp_control; // relative path
                    }
                    if (video) _videoUri = new Uri(control);
                    if (audio) _audioUrl = new Uri(control);
                }
                if (attrib.Key.Equals("fmtp")) {
                    fmtp = attrib as Rtsp.Sdp.AttributFmtp;
                }
                if (attrib.Key.Equals("rtpmap")) {
                    Rtsp.Sdp.AttributRtpMap rtpmap = attrib as Rtsp.Sdp.AttributRtpMap;

                    // Check if the Codec Used (EncodingName) is one we support
                    String[] valid_video_codecs = {"H264","H265" , "MP4V-ES", "JPEG"};
                    String[] valid_audio_codecs = {"PCMA", "PCMU", "AMR", "MPEG4-GENERIC" /* for aac */}; // Note some are "mpeg4-generic" lower case

                    if (video && Array.IndexOf(valid_video_codecs,rtpmap.EncodingName.ToUpper()) >= 0) {
                        // found a valid codec
                        _videoCodec = rtpmap.EncodingName.ToUpper();
                        _videoPayload = media.PayloadType;
                    }
                    if (audio && Array.IndexOf(valid_audio_codecs,rtpmap.EncodingName.ToUpper()) >= 0) {
                        _audioCodec = rtpmap.EncodingName.ToUpper();
                        _audioPayload = media.PayloadType;
                    }
                }
            }            

        }

        private RtspTransport CreateRtspTransport(bool video, bool audio, ref int next_free_rtp_channel, ref int next_free_rtcp_channel)
        {
            RtspTransport transport = null;

            if (_rtpTransport == RTP_TRANSPORT.TCP)
            {
                // Server interleaves the RTP packets over the RTSP connection
                // Example for TCP mode (RTP over RTSP)   Transport: RTP/AVP/TCP;interleaved=0-1
                if (video) {
                    _videoВataСhannel = next_free_rtp_channel;
                    _videoRtcpChannel = next_free_rtcp_channel;
                }
                if (audio) {
                    _audioDataChannel = next_free_rtp_channel;
                    _audioRtcpChannel = next_free_rtcp_channel;
                }
                transport = new RtspTransport()
                {
                    LowerTransport = RtspTransport.LowerTransportType.TCP,
                    Interleaved = new PortCouple(next_free_rtp_channel, next_free_rtcp_channel), // Eg Channel 0 for RTP video data. Channel 1 for RTCP status reports
                };

                next_free_rtp_channel += 2;
                next_free_rtcp_channel += 2;
            }
            if (_rtpTransport == RTP_TRANSPORT.UDP)
            {
                int rtp_port = 0;
                int rtcp_port = 0;
                // Server sends the RTP packets to a Pair of UDP Ports (one for data, one for rtcp control messages)
                // Example for UDP mode                   Transport: RTP/AVP;unicast;client_port=8000-8001
                if (video) {
                    _videoВataСhannel = _videoUdpPair._dataPort;     // Used in DataReceived event handler
                    _videoRtcpChannel = _videoUdpPair._controlPort;  // Used in DataReceived event handler
                    rtp_port = _videoUdpPair._dataPort;
                    rtcp_port = _videoUdpPair._controlPort;
                }
                if (audio) {
                    _audioDataChannel = _audioUdpPair._dataPort;     // Used in DataReceived event handler
                    _audioRtcpChannel = _audioUdpPair._controlPort;  // Used in DataReceived event handler
                    rtp_port = _audioUdpPair._dataPort;
                    rtcp_port = _audioUdpPair._controlPort;
                }
                transport = new RtspTransport()
                {
                    LowerTransport = RtspTransport.LowerTransportType.UDP,
                    IsMulticast = false,
                    ClientPort = new PortCouple(rtp_port, rtcp_port), // a UDP Port for data (video or audio). a UDP Port for RTCP status reports
                };
            }
            if (_rtpTransport == RTP_TRANSPORT.MULTICAST)
            {
                // Server sends the RTP packets to a Pair of UDP ports (one for data, one for rtcp control messages)
                // using Multicast Address and Ports that are in the reply to the SETUP message
                // Example for MULTICAST mode     Transport: RTP/AVP;multicast
                if (video) {
                    _videoВataСhannel = 0; // we get this information in the SETUP message reply
                    _videoRtcpChannel = 0; // we get this information in the SETUP message reply
                }
                if (audio) {
                    _audioDataChannel = 0; // we get this information in the SETUP message reply
                    _audioRtcpChannel = 0; // we get this information in the SETUP message reply
                }
                transport = new RtspTransport()
                {
                    LowerTransport = RtspTransport.LowerTransportType.UDP,
                    IsMulticast = true
                };
            }

            return transport;
        }

        private void ProcessH264Fmtp(Rtsp.Sdp.AttributFmtp fmtp)
        {
            var param = Rtsp.Sdp.H264Parameters.Parse(fmtp.FormatParameter);
            var sps_pps = param.SpropParameterSets;
            if (sps_pps.Count() >= 2) {
                byte[] sps = sps_pps[0];
                byte[] pps = sps_pps[1];
                if (Received_SPS_PPS != null) {
                    Received_SPS_PPS(sps,pps);
                }
                _h264SpsPpsFired = true;
            }
        }

        private void ProcessH265Fmtp(Rtsp.Sdp.AttributFmtp fmtp)
        {
            var param = Rtsp.Sdp.H265Parameters.Parse(fmtp.FormatParameter);
            var vps_sps_pps = param.SpropParameterSets;
            if (vps_sps_pps.Count() >= 3)
            {
                byte[] vps = vps_sps_pps[0];
                byte[] sps = vps_sps_pps[1];
                byte[] pps = vps_sps_pps[2];
                if (Received_VPS_SPS_PPS != null)
                {
                    Received_VPS_SPS_PPS(vps,sps, pps);
                }
                _h265VpsSpsPpsFired = true;
            }
        }

        private void Rtsp_ProcessResponseDescribe(RtspResponse message)
        {
            // If we get a reply to DESCRIBE (which was our second command), then prosess SDP and send the SETUP

            // Got a reply for DESCRIBE
            if (message.IsOk == false) {
                _logger.Debug($"{VideoSourceId} Got Error in DESCRIBE Reply " + message.ReturnCode + " " + message.ReturnMessage);
                return;
            }

            // Examine the SDP

            _logger.Debug($"{VideoSourceId} "+ System.Text.Encoding.UTF8.GetString(message.Data));
            SdpData = message.Data;
            Rtsp.Sdp.SdpFile sdp_data;
            using (StreamReader sdp_stream = new StreamReader(new MemoryStream(message.Data)))
            {
                sdp_data = Rtsp.Sdp.SdpFile.Read(sdp_stream);
            }

            // RTP and RTCP 'channels' are used in TCP Interleaved mode (RTP over RTSP)
            // These are the channels we request. The camera confirms the channel in the SETUP Reply.
            // But, a Panasonic decides to use different channels in the reply.
            int next_free_rtp_channel = 0;
            int next_free_rtcp_channel = 1;

            // Process each 'Media' Attribute in the SDP (each sub-stream)

            for (int x = 0; x < sdp_data.Medias.Count; x++)
            {
                bool audio = (sdp_data.Medias[x].MediaType == Rtsp.Sdp.Media.MediaTypes.audio);
                bool video = (sdp_data.Medias[x].MediaType == Rtsp.Sdp.Media.MediaTypes.video);

                if (video && _videoPayload != -1) continue; // have already matched a video payload. don't match another
                if (audio && _audioPayload != -1) continue; // have already matched an audio payload. don't match another

                if (audio || video)
                {                    
                    String control = "";  // the "track" or "stream id"
                    Rtsp.Sdp.AttributFmtp fmtp = null; // holds SPS and PPS in base64 (h264 video)

                    SdpParseMediaAttributes(sdp_data.Medias[x], video, audio,ref control, ref fmtp);
                                        
                    // Create H264 RTP Parser
                    if (video && _videoCodec.Contains("H264"))
                    {
                        _h264Payload = new Rtsp.H264Payload(_logger);
                    }

                    // If the rtpmap contains H264 then split the fmtp to get the sprop-parameter-sets which hold the SPS and PPS in base64
                    if (video && _videoCodec.Contains("H264") && fmtp != null) {
                        ProcessH264Fmtp(fmtp);
                    }

                    // Create H265 RTP Parser
                    if (video && _videoCodec.Contains("H265"))
                    {
                        // TODO - check if DONL is being used
                        bool has_donl = false;
                        _h265Payload = new Rtsp.H265Payload(has_donl);
                    }

                    // If the rtpmap contains H265 then split the fmtp to get the sprop-vps, sprop-sps and sprop-pps
                    // The RFC makes the VPS, SPS and PPS OPTIONAL so they may not be present. In which we pass back NULL values
                    if (video && _videoCodec.Contains("H265") && fmtp != null)
                    {
                        ProcessH265Fmtp(fmtp);
                    }

                    // Create AAC RTP Parser
                    // Example fmtp is "96 profile-level-id=1;mode=AAC-hbr;sizelength=13;indexlength=3;indexdeltalength=3;config=1490"
                    // Example fmtp is ""96 streamtype=5;profile-level-id=1;mode=AAC-hbr;sizelength=13;indexlength=3;indexdeltalength=3;config=1210"
                    if (audio && _audioCodec.Contains("MPEG4-GENERIC") && fmtp.GetParameter("mode").ToLower().Equals("aac-hbr"))
                    {
                        // Extract config (eg 0x1490 or 0x1210)

                        _aacPayload = new Rtsp.AACPayload(fmtp.GetParameter("config"));
                    }


                    // Send the SETUP RTSP command if we have a matching Payload Decoder
                    if (video && _videoPayload == -1) continue;
                    if (audio && _audioPayload == -1) continue;

                    RtspTransport transport = CreateRtspTransport(video,audio, ref next_free_rtp_channel, ref next_free_rtcp_channel);

                    // Generate SETUP messages
                    Rtsp.Messages.RtspRequestSetup setup_message = new Rtsp.Messages.RtspRequestSetup(_logger);
                    setup_message.RtspUri = new Uri(control);
                    setup_message.AddTransport(transport);
                    if (_authType != null) {
                        AddAuthorization(setup_message,_username,_password,_authType,_realm,_nonce,_url);
                    }

                    // Add SETUP message to list of mesages to send
                    _setupMessages.Add(setup_message);

                }
            }
            // Send the FIRST SETUP message and remove it from the list of Setup Messages
            _rtspClient.SendMessage(_setupMessages[0]);
            _setupMessages.RemoveAt(0);
        }

        private void Rtsp_ProcessResponseSetup(RtspResponse message)
        {
            // If we get a reply to SETUP (which was our third command), then we
            // (i) check if the Interleaved Channel numbers have been modified by the camera (eg Panasonic cameras)
            // (ii) check if we have any more SETUP commands to send out (eg if we are doing SETUP for Video and Audio)
            // (iii) send a PLAY command if all the SETUP command have been sent
            
            // Got Reply to SETUP
            if (message.IsOk == false) {
                _logger.Debug($"{VideoSourceId} Got Error in SETUP Reply " + message.ReturnCode + " " + message.ReturnMessage);
                return;
            }

            _logger.Debug($"{VideoSourceId} Got reply from Setup. Session is " + message.Session);

            _session = message.Session; // Session value used with Play, Pause, Teardown and and additional Setups
            if(message.Timeout > 0 && message.Timeout > _keepaliveTimer.Interval / 1000)
            {
                _keepaliveTimer.Interval = message.Timeout * 1000 / 2;
            }
            
            // Check the Transport header
            if (message.Headers.ContainsKey(RtspHeaderNames.Transport))
            {

                RtspTransport transport = RtspTransport.Parse(message.Headers[RtspHeaderNames.Transport]);

                // Check if Transport header includes Multicast
                if (transport.IsMulticast)
                {
                    String multicast_address = transport.Destination;
                    _videoВataСhannel = transport.Port.First;
                    _videoRtcpChannel = transport.Port.Second;

                    // Create the Pair of UDP Sockets in Multicast mode
                    _videoUdpPair = new Rtsp.UDPSocket(multicast_address, _videoВataСhannel, multicast_address, _videoRtcpChannel);
                    _videoUdpPair.DataReceived += Rtp_DataReceived;
                    _videoUdpPair.ControlReceived += Rtcp_DataReceived;
                    _videoUdpPair.Start();

                    // TODO - Need to set audio_udp_pair for Multicast
                }

                // check if the requested Interleaved channels have been modified by the camera
                // in the SETUP Reply (Panasonic have a camera that does this)
                if (transport.LowerTransport == RtspTransport.LowerTransportType.TCP) {
                    if (message.OriginalRequest.RtspUri == _videoUri) {
                        _videoВataСhannel = transport.Interleaved.First;
                        _videoRtcpChannel = transport.Interleaved.Second;
                    }
                    if (message.OriginalRequest.RtspUri == _audioUrl) {
                        _audioDataChannel = transport.Interleaved.First;
                        _audioRtcpChannel = transport.Interleaved.Second;
                    }

                }
            }

            // Check if we have another SETUP command to send, then remote it from the list
            if (_setupMessages.Count > 0) {
                // send the next SETUP message, after adding in the 'session'
                Rtsp.Messages.RtspRequestSetup next_setup = _setupMessages[0];
                next_setup.Session = _session;
                _rtspClient.SendMessage(next_setup);

                _setupMessages.RemoveAt(0);
            }

            else {
                // Send PLAY
                Rtsp.Messages.RtspRequest play_message = new Rtsp.Messages.RtspRequestPlay(_logger);
                play_message.RtspUri = new Uri(_url);
                play_message.Session = _session;
                if (_authType != null) {
                    AddAuthorization(play_message,_username,_password,_authType,_realm,_nonce,_url);
                }
                _rtspClient.SendMessage(play_message);
            }
        }

        private void Rtsp_ProcessResponsePlay(RtspResponse message)
        {
            // If we get a reply to PLAY (which was our fourth command), then we should have video being received

            // Got Reply to PLAY
            if (message.IsOk == false) {
                _logger.Debug($"{VideoSourceId} Got Error in PLAY Reply " + message.ReturnCode + " " + message.ReturnMessage);
                _readyEvent.Set();
                return;
            }
            
            _logger.Debug($"{VideoSourceId} Got reply from Play  " + message.Command);
            _readyEvent.Set();
            _playing = true;

            var handler = OnStarted;
            if (handler != null)
            {
                handler(this);
            }
        }
        private void Rtsp_ProcessResponseTeardown(RtspResponse message)
        {
            Stop(RtspClientStopReason.SESSION_CLOSED);
        }

        private void Rtsp_TryResendMessage(RtspResponse message)
        {
            if (_resendMessageTrys < MAX_RESEND_MESSAGE_TRYS)
            {
                _resendMessageTrys++;  
                RtspMessage resend_message = message.OriginalRequest.Clone() as RtspMessage;

                if (_authType != null) {
                    AddAuthorization(resend_message,_username,_password,_authType,_realm,_nonce,_url);
                }
                _logger.Debug($"{VideoSourceId} Resend failed message " + resend_message.GetType().ToString());
                _rtspClient.SendMessage(resend_message);                                  
            }
            else 
            {
                Stop(RtspClientStopReason.SESSION_FAILED);
            }
        }

        // RTSP Messages are OPTIONS, DESCRIBE, SETUP, PLAY etc
        private void Rtsp_MessageReceived(object sender, Rtsp.RtspChunkEventArgs e)
        {
            Rtsp.Messages.RtspResponse message = e.Message as Rtsp.Messages.RtspResponse;

            _logger.Debug($"{VideoSourceId} Received RTSP Message " + message.OriginalRequest.ToString());

            // If message has a 401 - Unauthorised Error, then we re-send the message with Authorization
            // using the most recently received 'realm' and 'nonce'
			if (message.IsOk == false) {
                _logger.Debug($"{VideoSourceId} Got Error in RTSP Reply " + message.ReturnCode + " " + message.ReturnMessage);

				if (message.ReturnCode == 401 && (message.OriginalRequest.Headers.ContainsKey(RtspHeaderNames.Authorization)==true)) {
					// the authorization failed.
					Stop(RtspClientStopReason.AUTHORIZATION_FAILED);
					return;
				}
                    
                // Check if the Reply has an Authenticate header.
				if (message.ReturnCode == 401 && message.Headers.ContainsKey(RtspHeaderNames.WWWAuthenticate)) {

                    Rtsp_ProcessResponseAuthorization(message);
				}
				
				Rtsp_TryResendMessage(message);                

				return;

            }
            else
            {
                _resendMessageTrys = 0;
            }

            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestOptions)
            {
                Rtsp_ProcessResponseOptions(message);
            }

            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestDescribe)
            {
                Rtsp_ProcessResponseDescribe(message);                
            }

            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestSetup)
            {
                Rtsp_ProcessResponseSetup(message);
            }

            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestPlay)
            {                
                Rtsp_ProcessResponsePlay(message);    
            }

            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestTeardown)
            {                
                Rtsp_ProcessResponsePlay(message);    
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
            Stop(RtspClientStopReason.CONNECTION_LOST);
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
                        Stop(RtspClientStopReason.CONNECTION_LOST);                                         
                        return;
                    }
                default:
                    {
                        _logger.Error(se);
                        return;
                    }
            }
        }

        void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Send Keepalive message
            // The ONVIF Standard uses SET_PARAMETER as "an optional method to keep an RTSP session alive"
            // RFC 2326 (RTSP Standard) says "GET_PARAMETER with no entity body may be used to test client or server liveness("ping")"

            // This code uses GET_PARAMETER (unless OPTIONS report it is not supported, and then it sends OPTIONS as a keepalive)

            if (_serverSupportsGetParameter) {

                Rtsp.Messages.RtspRequest getparam_message = new Rtsp.Messages.RtspRequestGetParameter(_logger);
                getparam_message.RtspUri = new Uri(_url);
                getparam_message.Session = _session;
                if (_authType != null)
                {
                    AddAuthorization(getparam_message, _username, _password, _authType, _realm, _nonce, _url);
                }
                _rtspClient.SendMessage(getparam_message);

            } else {

                Rtsp.Messages.RtspRequest options_message = new Rtsp.Messages.RtspRequestOptions(_logger);
                options_message.RtspUri = new Uri(_url);
    			if (_authType != null) {
                    AddAuthorization(options_message,_username,_password,_authType,_realm,_nonce,_url);
                }
                _rtspClient.SendMessage(options_message);
            }
        }

        // Generate Basic or Digest Authorization
        public void AddAuthorization(RtspMessage message, string username, string password,
            string auth_type, string realm, string nonce, string url)  {

            if (username == null || username.Length == 0) return;
            if (password == null || password.Length == 0) return;
            if (realm == null || realm.Length == 0) return;
			if (auth_type.Equals("Digest") && (nonce == null || nonce.Length == 0)) return;

			if (auth_type.Equals("Basic")) {
				byte[] credentials = System.Text.Encoding.UTF8.GetBytes(username+":"+password);
				String credentials_base64 = Convert.ToBase64String(credentials);
                String basic_authorization = "Basic " + credentials_base64;

				message.Headers.Add(RtspHeaderNames.Authorization, basic_authorization);

				return;
            }
            else if (auth_type.Equals("Digest")) {

				string method = message.Method; // DESCRIBE, SETUP, PLAY etc
               
                MD5 md5 = System.Security.Cryptography.MD5.Create();
                String hashA1 = CalculateMD5Hash(md5, username+":"+realm+":"+password);
                String hashA2 = CalculateMD5Hash(md5, method + ":" + url);
                String response = CalculateMD5Hash(md5, hashA1 + ":" + nonce + ":" + hashA2);

                const String quote = "\"";
                String digest_authorization = "Digest username=" + quote + username + quote +", "
                    + "realm=" + quote + realm + quote + ", "
                    + "nonce=" + quote + nonce + quote + ", "
                    + "uri=" + quote + url + quote + ", "
                    + "response=" + quote + response + quote;

				message.Headers.Add(RtspHeaderNames.Authorization, digest_authorization);
                
				return;
			}
			else {
				return;
			}
            
        }

        // MD5 (lower case)
        public string CalculateMD5Hash(MD5 md5_session, string input)
        {
            byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
            byte[] hash = md5_session.ComputeHash(inputBytes);

            StringBuilder output = new StringBuilder();
            for (int i = 0; i < hash.Length; i++) {
                output.Append(hash[i].ToString("x2"));
            }

            return output.ToString();
        }

        
    }
}
