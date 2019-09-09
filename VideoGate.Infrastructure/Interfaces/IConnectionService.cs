using System;

namespace VideoGate.Infrastructure.Interfaces
{
    public interface IConnectionService : IDisposable
    {

        Guid[] GetServerConnectionIds(Guid videoSourceId); 
        bool IsClientRunning(Guid videoSourceId); 
        bool HasClient(Guid videoSourceId); 

        //just for tests
        bool TestRtpDataProcessed {get;}

    }
}
