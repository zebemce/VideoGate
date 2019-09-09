using System;

namespace VideoGate.Infrastructure.Interfaces
{
    public interface IAppConfigurationFacade
    {
        int HttpPort {get;}
        int RtspServerPort {get;}
        string RtspServerLogin {get;}
        string RtspServerPassword {get;}
        string RtspServerAddress {get;}
        string RtspClientAddress {get;}

    }
}
