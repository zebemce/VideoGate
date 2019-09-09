using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VideoGate.Infrastructure.Models;

namespace VideoGate.Infrastructure.Interfaces
{
    public delegate Task<byte[]> RtspProvideSdpDataHandler(Guid connectionId, VideoSource videoSource);
    public delegate void RtspPlayRequestHandler(Guid connectionId);
    public delegate void RtspConnectionHandler(Guid connectionId, VideoSource videoSource);

    public interface IRtspServer : IDisposable
    {
        event RtspConnectionHandler OnConnectionAdded; 
        event RtspConnectionHandler OnConnectionRemoved;

        event RtspProvideSdpDataHandler OnProvideSdpData;
        event RtspPlayRequestHandler OnPlay;
        event RtspPlayRequestHandler OnStop;

        void SendRtpAudioData(Guid connectionId,byte[] data);
        void SendRtcpAudioData(Guid connectionId,byte[] data);
        void SendRtpVideoData(Guid connectionId,byte[] data);
        void SendRtcpVideoData(Guid connectionId,byte[] data);

        void ForceDisconnectPool(List<Guid> connectionIds);

    }
}
