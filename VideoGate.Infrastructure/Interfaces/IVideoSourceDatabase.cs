using System;
using VideoGate.Infrastructure.Models;

namespace VideoGate.Infrastructure.Interfaces
{
    public interface IVideoSourceDatabase
    {
        VideoSource[] Load();
        void Save(VideoSource[] videoSources);
    }
}
