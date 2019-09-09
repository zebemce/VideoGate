using NLog;

namespace Rtsp.Messages
{
    public class RtspRequestRecord : RtspRequest
    {
        public RtspRequestRecord(ILogger logger) : base(logger)
        {
            Command = "RECORD * RTSP/1.0";
        }
    }
}