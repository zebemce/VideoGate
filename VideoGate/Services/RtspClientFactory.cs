using System;
using System.Net;
using NLog;
using Rtsp;
using VideoGate.Infrastructure.Interfaces;
using VideoGate.Infrastructure.Models;

namespace VideoGate.Services
{
    public class RtspClientFactory : IRtspClientFactory
    {
        private readonly IAppConfigurationFacade _appConfigurationFacade;

        public RtspClientFactory(IAppConfigurationFacade appConfigurationFacade)
        {
            _appConfigurationFacade = appConfigurationFacade;
        }
        
        public IRtspClient Create(VideoSource videoSource)
        {
            Exception getIPexception;
            IPAddress ipAddress = IPUtils.GetIPAddressFromString(_appConfigurationFacade.RtspClientAddress, out getIPexception);
            if (getIPexception != null)
            {
                LogManager.GetLogger("RtspClientFactory").Error("Error getting RtspClient address: "+getIPexception);
            }
            
            return new RTSPClient.RtspClient(videoSource,ipAddress);
        }
    }
}
