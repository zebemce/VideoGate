using System;
using Castle.Core.Configuration;
using Moq;
using VideoGate.Infrastructure.Interfaces;
using VideoGate.Infrastructure.Models;
using VideoGate.Services;
using VideoGate.Tests.TestFixtures;
using Xunit;

namespace VideoGate.Tests.Services
{
    public class RequestUrlVideoSourceResolverStrategyTest
    {
        protected readonly IRequestUrlVideoSourceResolverStrategy _requestUrlVideoSourceResolverStrategy;
        protected readonly Mock<IConfiguration> _configuration;
        protected readonly Mock<IVideoSourceStorage> _videoSourceStorageMock;
        private readonly VideoSourcesFixtures _videoSourcesFixtures;
        public RequestUrlVideoSourceResolverStrategyTest()
        {
            _videoSourceStorageMock = new Mock<IVideoSourceStorage>();
            _videoSourcesFixtures = new VideoSourcesFixtures();
            _videoSourceStorageMock.Setup(x => x.GetVideoSourceById(It.IsAny<Guid>()))
            .Returns<Guid>(
                (videoSourceId) => {
                    if(_videoSourcesFixtures.VideoSourceOne.Id == videoSourceId) return _videoSourcesFixtures.VideoSourceOne;
                    if(_videoSourcesFixtures.VideoSourceTwo.Id == videoSourceId) return _videoSourcesFixtures.VideoSourceTwo;
                    return null;
                }
            );
            _videoSourceStorageMock.Setup(x => x.GetVideoSourceByCaption(It.IsAny<string>()))
            .Returns<string>(
                (videoSourceCaption) => {
                    if(_videoSourcesFixtures.VideoSourceOne.Caption == videoSourceCaption) return _videoSourcesFixtures.VideoSourceOne;
                    if(_videoSourcesFixtures.VideoSourceTwo.Caption == videoSourceCaption) return _videoSourcesFixtures.VideoSourceTwo;
                    return null;
                }
            );
            _requestUrlVideoSourceResolverStrategy = new RequestUrlVideoSourceResolverStrategy(_videoSourceStorageMock.Object);

        }

        [Theory] 
        [InlineData("rtsp://localhost:8554/live/00000000-0000-0000-0000-000000000001")]
        [InlineData("rtsp://localhost:8554/live/00000000-0000-0000-0000-000000000001/")]
        [InlineData("rtsp://localhost:8554/live/SourceOne/")]
        [InlineData("rtsp://localhost:8554/live/SourceOne")]
        [InlineData("rtsp://localhost:8554/live/00000000-0000-0000-0000-000000000002")]
        [InlineData("rtsp://localhost:8554/live/00000000-0000-0000-0000-000000000002/")]
        [InlineData("rtsp://localhost:8554/live/SourceTwo/")]
        [InlineData("rtsp://localhost:8554/live/SourceTwo")]
        public void ResolveVideoSource_CorrectUrl_ReturnsVideoSource(string url)
        {
            Assert.True(_requestUrlVideoSourceResolverStrategy.ResolveVideoSource(url) is VideoSource);
        }

        [Theory] 
        [InlineData("rtsp://localhost:8554")]
        [InlineData("rtsp://localhost:8554/00000000-0000-0000-0000-000000000001/")]
        [InlineData("rtsp://localhost:8554/SourceOne/")]
        [InlineData("rtsp://localhost:8554/live/00000000-0000-0000-0000-0000000000R3")]
        [InlineData("rtsp://localhost:8554/live/00000000-0000-0000-0000-0000000000R3/")]
        [InlineData("rtsp://localhost:8554/live/SourceWrong/")]
        [InlineData("rtsp://localhost:8554/live/SourceWrong")]
        public void ResolveVideoSource_InCorrectUrl_ReturnsNull(string url)
        {
            Assert.True(null == _requestUrlVideoSourceResolverStrategy.ResolveVideoSource(url));
        }
    }
}
