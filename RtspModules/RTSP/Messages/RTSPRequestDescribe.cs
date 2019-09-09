using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rtsp.Messages
{
    public class RtspRequestDescribe : RtspRequest
    {

        // constructor

        public RtspRequestDescribe(ILogger logger) : base(logger)
        {
            Command = "DESCRIBE * RTSP/1.0";
            Headers[RtspHeaderNames.Accept] = "application/sdp";
        }
    }
}
