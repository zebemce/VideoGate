using System;

namespace VideoGate.Infrastructure.Interfaces
{
    public enum RtspClientStopReason 
    {
        COMMAND = 0,
        CONNECTION_FAILED = 1,
        CONNECTION_LOST = 2,
        AUTHORIZATION_FAILED = 3,
        SESSION_FAILED = 4,
        SESSION_CLOSED = 5,
        TRANSPORT_FAILED = 6
    }
    
    
    public delegate void Received_Rtp_Data_Delegate (IRtspClient sender, int channel, byte[] data); 
    public delegate void Received_Rtcp_Data_Delegate (IRtspClient sender, int channel, byte[] data); 
    public delegate void Rtsp_Client_Started_Delegate (IRtspClient sender); 
    public delegate void Rtsp_Client_Stopped_Delegate (IRtspClient sender, RtspClientStopReason reason); 
    
    public interface IRtspClient
    {
        Guid VideoSourceId {get;}
        byte[] SdpData {get;}
        
        void Start();
        //TODO: change to acync
        bool WaitReady();

        void Stop(RtspClientStopReason reason = RtspClientStopReason.COMMAND);
        
        int VideoRtpChannel {get;}
        int VideoRtcpChannel {get;}
        int AudioRtpChannel {get;}
        int AudioRtcpChannel {get;}
        bool IsRunning {get;}
        event Received_Rtp_Data_Delegate Received_Rtp;
        event Received_Rtcp_Data_Delegate Received_Rtcp;
        event Rtsp_Client_Started_Delegate OnStarted;
        event Rtsp_Client_Stopped_Delegate OnStopped;
    }
}
