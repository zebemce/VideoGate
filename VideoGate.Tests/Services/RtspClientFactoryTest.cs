using System;
using Xunit;
using Moq;
using VideoGate.Infrastructure.Interfaces;
using VideoGate.Services;
using VideoGate.Tests.TestFixtures;
using VideoGate.Infrastructure.Models;


namespace VideoGate.Tests.Services
{
    public class RtspClientFactoryTest
    {
        protected readonly VideoSourcesFixtures _videoSourcesFixtures;
        protected readonly IRtspClientFactory _rtspClientFactory;
        protected readonly Mock<IAppConfigurationFacade> _appConfigurationFacadeMock;
        public RtspClientFactoryTest()
        {
            _appConfigurationFacadeMock = new Mock<IAppConfigurationFacade>();
            _videoSourcesFixtures = new VideoSourcesFixtures();
            _rtspClientFactory = new RtspClientFactory(_appConfigurationFacadeMock.Object);
           
        }

        [Fact]
        public void Create_WithVideoSourceOne_ClientVideoSourceIdIsCorrect()
        {
            IRtspClient rtspClient = _rtspClientFactory.Create(_videoSourcesFixtures.VideoSourceOne);
            Assert.True(rtspClient.VideoSourceId == _videoSourcesFixtures.VideoSourceOneId);
        }
    }
}
