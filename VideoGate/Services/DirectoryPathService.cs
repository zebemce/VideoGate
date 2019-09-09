using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using VideoGate.Infrastructure.Interfaces;

namespace VideoGate.Services
{
    public class DirectoryPathService : IDirectoryPathService
    {
        public DirectoryPathService()
        {
            if (false == Directory.Exists(DataPath))
                Directory.CreateDirectory(DataPath);
        }
        
        public string RootPath => Directory.GetParent(typeof(DirectoryPathService).GetTypeInfo().Assembly.Location).FullName;

        public string DataPath  
        {
            get
            {
                return RootPath;                
            }
            
        }

        public string WebContentRootPath
        {
            get
            {
                return RootPath;
            }

        }

        public static bool isDEBUG()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }

    }
}
