using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rtsp.Messages
{
    public class RtspRequestOptions : RtspRequest
    {

        // Constructor
        public RtspRequestOptions(ILogger logger):base(logger)
        {
            Command = "OPTIONS * RTSP/1.0";
        }

        /// <summary>
        /// Gets the assiociate OK response with the request.
        /// </summary>
        /// <returns>
        /// an Rtsp response corresponding to request.
        /// </returns>
        public override RtspResponse CreateResponse(ILogger logger)
        {
            RtspResponse response = base.CreateResponse(logger);
            // Add genric suported operations.
            response.Headers.Add(RtspHeaderNames.Public, "OPTIONS,DESCRIBE,SETUP,PLAY,PAUSE,TEARDOWN,GET_PARAMETER");

            return response;
        }

    }
}
