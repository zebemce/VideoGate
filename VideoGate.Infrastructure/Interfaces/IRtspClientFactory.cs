using System;
using VideoGate.Infrastructure.Models;

namespace VideoGate.Infrastructure.Interfaces
{
    public interface IRtspClientFactory
    {
        IRtspClient Create(VideoSource videoSource);
    }
}
