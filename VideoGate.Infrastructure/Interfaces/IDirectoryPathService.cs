using System;

namespace VideoGate.Infrastructure.Interfaces
{
    public interface IDirectoryPathService
    {
        string RootPath {get;}
        string DataPath {get;}
        string WebContentRootPath {get;}
    }
}
