using System;
using VideoGate.Infrastructure.Interfaces;
using VideoGate.Infrastructure.Models;
using Newtonsoft.Json;
using System.IO;

namespace VideoGate.Services
{
    public class VideoSourceDatabase : IVideoSourceDatabase
    {
        
        private readonly string _filePath;

        public VideoSourceDatabase(IDirectoryPathService directoryPathService)
        {
            _filePath = Path.Combine(directoryPathService.RootPath, "sources.json");
        }

        public VideoSource[] Load()
        {
            if (false == File.Exists(_filePath))
            {
                File.WriteAllText(_filePath, JsonConvert.SerializeObject(new VideoSource[0], Formatting.Indented));
            }

            var text = File.ReadAllText(_filePath);
            return JsonConvert.DeserializeObject<VideoSource[]>(text);            
        }

        public void Save(VideoSource[] videoSources)
        {
            File.WriteAllText(_filePath, JsonConvert.SerializeObject(videoSources, Formatting.Indented));
        }
    }
}
