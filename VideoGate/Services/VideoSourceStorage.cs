using System;
using System.Collections.Generic;
using System.Linq;
using VideoGate.Infrastructure.Interfaces;
using VideoGate.Infrastructure.Models;

namespace VideoGate.Services
{
    public class VideoSourceStorage : IVideoSourceStorage
    {
        protected readonly IVideoSourceDatabase _videoSourceDatabase;
        protected readonly List<VideoSource> _videoSources;
        public VideoSource[] VideoSources 
        {
            get 
            {
                return _videoSources.ToArray();
            }
        }
        public event VideoSourceHandler OnVideoSourceCreated;
        public event VideoSourceHandler OnVideoSourceUpdated;
        public event VideoSourceHandler OnVideoSourceDeleted;

        public VideoSourceStorage(IVideoSourceDatabase videoSourceDatabase)
        {
            _videoSourceDatabase = videoSourceDatabase;
            _videoSources = _videoSourceDatabase.Load().ToList();
        }

        public void CreateVideoSource(VideoSource videoSource)
        {
            if(_videoSources.Any(vs => videoSource.Id == vs.Id))
            {
                throw new Exception("Duplicate Id");
            }

            if(_videoSources.Any(vs => videoSource.Caption == vs.Caption))
            {
                throw new Exception("Duplicate Caption");
            }
            _videoSources.Add(videoSource);
            _videoSourceDatabase.Save(_videoSources.ToArray());

            if (OnVideoSourceCreated != null)
            {
                OnVideoSourceCreated(videoSource);
            }
        }

        public void UpdateVideoSource(VideoSource videoSource)
        {
            if(_videoSources.Any(vs => videoSource.Caption == vs.Caption && videoSource.Id != vs.Id))
            {
                throw new Exception("Duplicate Caption");
            }

            int index = _videoSources.FindIndex(vs => vs.Id == videoSource.Id);
            _videoSources[index] = videoSource;
            _videoSourceDatabase.Save(_videoSources.ToArray());
            
            if (OnVideoSourceUpdated != null)
            {
                OnVideoSourceUpdated(videoSource);
            }
        }

        public void DeleteVideoSource(Guid videoSourceId)
        {
            int index = _videoSources.FindIndex(vs => vs.Id == videoSourceId);
            VideoSource videoSource = _videoSources[index];
            _videoSources.RemoveAt(index);
            _videoSourceDatabase.Save(_videoSources.ToArray());
            if (OnVideoSourceDeleted != null)
            {
                OnVideoSourceDeleted(videoSource);
            }
           
        }

        public VideoSource GetVideoSourceById(Guid videoSourceId)
        {
            return _videoSources.Find(vs => vs.Id == videoSourceId);
        }

        public VideoSource GetVideoSourceByCaption(string videoSourceCaption)
        {
            return _videoSources.Find(vs => vs.Caption == videoSourceCaption);
        }

        
    }
}
