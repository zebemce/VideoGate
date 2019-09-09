using System;
using VideoGate.Infrastructure.Models;

namespace VideoGate.Tests.TestFixtures
{
    public class VideoSourcesFixtures: IDisposable
    {
        public Guid VideoSourceOneId;
        
        public VideoSource VideoSourceOne;

        public Guid VideoSourceTwoId;
        
        public VideoSource VideoSourceTwo;
        
        public VideoSourcesFixtures()
        {
            VideoSourceOneId = Guid.Parse("00000000-0000-0000-0000-000000000001");
            
            VideoSourceOne = new VideoSource()
            {
                Id = VideoSourceOneId,
                Url = "rtsp://host:554/video1",
                Enabled = true,
                Caption = "SourceOne"

            };

            VideoSourceTwoId = Guid.Parse("00000000-0000-0000-0000-000000000002");
            
            VideoSourceTwo = new VideoSource()
            {
                Id = VideoSourceTwoId,
                Url = "rtsp://host:554/video2",
                Enabled = true,
                Caption = "SourceTwo"

            };
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
