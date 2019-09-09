using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rtsp.Messages
{
    public class RtspRequestTeardown : RtspRequest
    {

        // Constructor
        public RtspRequestTeardown(ILogger logger) : base(logger)
        {
            Command = "TEARDOWN * RTSP/1.0";
        }
    }
}
