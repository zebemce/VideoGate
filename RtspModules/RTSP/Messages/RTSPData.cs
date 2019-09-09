using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NLog;

namespace Rtsp.Messages
{
    /// <summary>
    /// Message wich represent data. ($ limited message)
    /// </summary>
    public class RtspData : RtspChunk
    {

        /// <summary>
        /// Logs the message to debug.
        /// </summary>
        public override void LogMessage(NLog.LogLevel aLevel, ILogger logger)
        {
            // Default value to debug
            if (aLevel == null)
                aLevel = NLog.LogLevel.Debug;
            // if the level is not logged directly return
            if (!logger.IsEnabled(aLevel))
                return;
            logger.Log(aLevel, "Data message");
            if (Data == null)
                logger.Log(aLevel, "Data : null");
            else
                logger.Log(aLevel, "Data length :-{0}-", Data.Length);
        }

        public int Channel { get; set; }

        /// <summary>
        /// Clones this instance.
        /// <remarks>Listner is not cloned</remarks>
        /// </summary>
        /// <returns>a clone of this instance</returns>
        public override object Clone()
        {
            RtspData result = new RtspData();
            result.Channel = this.Channel;
            if (this.Data != null)
                result.Data = this.Data.Clone() as byte[];
            result.SourcePort = this.SourcePort;
            return result;
        }
    }
}
