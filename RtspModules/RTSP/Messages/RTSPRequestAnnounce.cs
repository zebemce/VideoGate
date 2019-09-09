using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rtsp.Messages
{
    public class RtspRequestAnnounce : RtspRequest
    {
        // constructor

        public RtspRequestAnnounce(ILogger logger): base(logger)
        {
            Command = "ANNOUNCE * RTSP/1.0";
        }
    }
}