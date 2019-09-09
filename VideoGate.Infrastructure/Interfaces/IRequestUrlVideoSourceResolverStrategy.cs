using System;
using VideoGate.Infrastructure.Models;

namespace VideoGate.Infrastructure.Interfaces
{
    public interface IRequestUrlVideoSourceResolverStrategy
    {
        VideoSource ResolveVideoSource(string requestUrl);
    }
}
