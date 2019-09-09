using System;
using Microsoft.Extensions.Configuration;
using VideoGate.Infrastructure.Interfaces;

namespace VideoGate.Services
{
    public class AppConfigurationFacade : IAppConfigurationFacade
    {
        protected readonly IConfiguration _configuration;

        public AppConfigurationFacade(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public int HttpPort => _configuration.GetSection("App").GetValue<int>("HttpPort");
        
        public int RtspServerPort => _configuration.GetSection("App").GetValue<int>("RtspServerPort");

        public string RtspServerLogin => _configuration.GetSection("App").GetValue<string>("RtspServerLogin");

        public string RtspServerPassword => _configuration.GetSection("App").GetValue<string>("RtspServerPassword");

        public string RtspServerAddress => _configuration.GetSection("App").GetValue<string>("RtspServerAddress");

        public string RtspClientAddress => _configuration.GetSection("App").GetValue<string>("RtspClientAddress");
    }
}
