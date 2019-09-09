using System;
using VideoGate.Infrastructure.Models;

namespace VideoGate.Infrastructure.Interfaces
{
    public delegate void VideoSourceHandler(VideoSource videoSource);

    public interface IVideoSourceStorage
    {
        VideoSource[] VideoSources {get;}
        VideoSource GetVideoSourceById(Guid videoSourceId);
        VideoSource GetVideoSourceByCaption(string videoSourceCaption);
        void CreateVideoSource(VideoSource videoSource);
        void UpdateVideoSource(VideoSource videoSource);
        void DeleteVideoSource(Guid videoSourceId);

        event VideoSourceHandler OnVideoSourceCreated;
        event VideoSourceHandler OnVideoSourceUpdated;
        event VideoSourceHandler OnVideoSourceDeleted;
    }
}
