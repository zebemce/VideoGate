using System;
using VideoGate.Infrastructure.Interfaces;
using VideoGate.Infrastructure.Models;

namespace VideoGate.Services
{
    public class RequestUrlVideoSourceResolverStrategy : IRequestUrlVideoSourceResolverStrategy
    {
        protected readonly IVideoSourceStorage _videoSourceStorage;

        public RequestUrlVideoSourceResolverStrategy(IVideoSourceStorage videoSourceStorage)
        {
            _videoSourceStorage = videoSourceStorage;
        }

        public VideoSource ResolveVideoSource(string requestUrl)
        {
            string token = GetSourceToken(requestUrl);
            Guid sourceId;
            if(Guid.TryParse(token,out sourceId))
            {
                return _videoSourceStorage.GetVideoSourceById(sourceId) ?? 
                _videoSourceStorage.GetVideoSourceByCaption(token);
            }

            return _videoSourceStorage.GetVideoSourceByCaption(token);
            
        }

        protected virtual string GetSourceToken(string url)
        {
            Uri uri = new Uri(url);
            if (uri.Segments.Length < 3) 
            {
                return null;
            }

            if (uri.Segments[1] != "live/") 
            {
                return null;
            }
            

            return uri.Segments[2].Replace("/", string.Empty);
        }
    }
}
