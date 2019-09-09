using System;
using Xunit;
using Moq;
using VideoGate.Infrastructure.Interfaces;
using VideoGate.Services;
using VideoGate.Tests.TestFixtures;
using VideoGate.Infrastructure.Models;
using System.Threading;

namespace VideoGate.Tests.Services
{
    
    public class ConnectionServiceTest
    {
        protected readonly IConnectionService _connectionService;
        protected readonly VideoSourcesFixtures _videoSourcesFixtures;
        protected readonly Mock<IVideoSourceStorage> _videoSourceStorageMock = new Mock<IVideoSourceStorage>();
        protected readonly Mock<IRtspClientFactory> _rtspClientFactoryMock = new Mock<IRtspClientFactory>();
        protected readonly Mock<IRtspClient> _rtspClientMock = new Mock<IRtspClient>();
        protected readonly Mock<IRtspServer> _rtspServerMock = new Mock<IRtspServer>();

        private Guid _connectionOneId = Guid.Parse("00000000-0000-0000-0000-0000000000C1");
        private Guid _connectionTwoId = Guid.Parse("00000000-0000-0000-0000-0000000000C2");
        private int _rtpVideoChannel = 0;
        private int _rtpAudioChannel = 1;
        private int _rtcpVideoChannel = 2;
        private int _rtcpAudioChannel = 3;
        
        public ConnectionServiceTest()
        {
            _videoSourcesFixtures = new VideoSourcesFixtures();

            _rtspClientMock.Setup(x => x.VideoSourceId).Returns(_videoSourcesFixtures.VideoSourceOneId);
            _rtspClientMock.Setup(x => x.IsRunning).Returns(false);
            _rtspClientMock.Setup(x => x.VideoRtpChannel).Returns(_rtpVideoChannel);
            _rtspClientMock.Setup(x => x.VideoRtcpChannel).Returns(_rtcpVideoChannel);
            _rtspClientMock.Setup(x => x.AudioRtpChannel).Returns(_rtpAudioChannel);
            _rtspClientMock.Setup(x => x.AudioRtcpChannel).Returns(_rtcpAudioChannel);

            _rtspClientFactoryMock.Setup(x => x.Create(It.IsAny<VideoSource>())).Returns(_rtspClientMock.Object);
            _videoSourceStorageMock.Setup(x => x.VideoSources).Returns(
                new VideoSource[]{
                    _videoSourcesFixtures.VideoSourceOne,
                    _videoSourcesFixtures.VideoSourceTwo
                });
            _connectionService = new ConnectionService(
                _videoSourceStorageMock.Object,
                _rtspClientFactoryMock.Object,
                _rtspServerMock.Object)
                {
                    DoNotPostponeClientClosingForTest = true
                };
            
        }
        
        protected void VideoSourceOneAddConnection()
        {
            _rtspServerMock.Raise(x => x.OnConnectionAdded += null,_connectionOneId,_videoSourcesFixtures.VideoSourceOne);
        }

        protected void VideoSourceOneAddAndRemoveConnection()
        {
            _rtspServerMock.Raise(x => x.OnConnectionAdded += null,_connectionOneId,_videoSourcesFixtures.VideoSourceOne);
            _rtspServerMock.Raise(x => x.OnConnectionRemoved += null,_connectionOneId,_videoSourcesFixtures.VideoSourceOne);
        }

        protected void VideoSourceOneAddTwoConnections()
        {
            _rtspServerMock.Raise(x => x.OnConnectionAdded += null,_connectionOneId,_videoSourcesFixtures.VideoSourceOne);
            _rtspServerMock.Raise(x => x.OnConnectionAdded += null,_connectionTwoId,_videoSourcesFixtures.VideoSourceOne);
        }

        protected void VideoSourceOneAddTwoAndRemoveOneConnections()
        {
            _rtspServerMock.Raise(x => x.OnConnectionAdded += null,_connectionOneId,_videoSourcesFixtures.VideoSourceOne);
            _rtspServerMock.Raise(x => x.OnConnectionAdded += null,_connectionTwoId,_videoSourcesFixtures.VideoSourceOne);

            _rtspServerMock.Raise(x => x.OnConnectionRemoved += null,_connectionOneId,_videoSourcesFixtures.VideoSourceOne);
        }

        [Fact]
        public void GetConnectionIds_OfEmptyOnCreation_IsNull()
        {            
            Assert.True(_connectionService.GetServerConnectionIds(Guid.Empty) == null);
        }

        [Fact]
        public void GetConnectionIds_OfVideoSourceOneOnCreation_IsEmptyArray()
        {                        
            var value = _connectionService.GetServerConnectionIds(_videoSourcesFixtures.VideoSourceOneId);
            Assert.True(value is Guid[] && value.Length == 0);
        }

        [Fact]
        public void SendRtpVideoData_OfVideoSourceOneOnAddConnectionReceivedRtpVideo_Once()
        {            
            VideoSourceOneAddConnection();
            _rtspClientMock.Raise(x => x.Received_Rtp += null,_rtspClientMock.Object, _rtpVideoChannel,null);
            _rtspServerMock.Verify(x => x.SendRtpVideoData(_connectionOneId,It.IsAny<byte[]>()),Times.Once);

        }

        [Fact]
        public void SendRtpAudioData_OfVideoSourceOneOnAddConnectionReceivedRtpAudio_Once()
        {            
            VideoSourceOneAddConnection();
            _rtspClientMock.Raise(x => x.Received_Rtp += null,_rtspClientMock.Object, _rtpAudioChannel,null);
            Assert.True(_connectionService.TestRtpDataProcessed);
            _rtspServerMock.Verify(x => x.SendRtpAudioData(_connectionOneId,It.IsAny<byte[]>()),Times.Once);

        }

         [Fact]
        public void SendRtcpVideoData_OfVideoSourceOneOnAddConnectionReceivedRtcpVideo_Once()
        {            
            VideoSourceOneAddConnection();
            _rtspClientMock.Raise(x => x.Received_Rtcp += null,_rtspClientMock.Object, _rtcpVideoChannel,null);
            Assert.True(_connectionService.TestRtpDataProcessed);
            _rtspServerMock.Verify(x => x.SendRtcpVideoData(_connectionOneId,It.IsAny<byte[]>()),Times.Once);

        }

        [Fact]
        public void SendRtcpAudioData_OfVideoSourceOneOnAddConnectionReceivedRtcpAudio_Once()
        {            
            VideoSourceOneAddConnection();
            _rtspClientMock.Raise(x => x.Received_Rtcp += null,_rtspClientMock.Object, _rtcpAudioChannel,null);
            Assert.True(_connectionService.TestRtpDataProcessed);
            _rtspServerMock.Verify(x => x.SendRtcpAudioData(_connectionOneId,It.IsAny<byte[]>()),Times.Once);

        }

        [Fact]
        public void SendRtpVideoData_OfVideoSourceOneOnAddAndRemoveConnectionReceivedRtpVideo_Never()
        {            
            VideoSourceOneAddAndRemoveConnection();
            _rtspClientMock.Raise(x => x.Received_Rtp += null,_rtspClientMock.Object, _rtpVideoChannel,null);
            Assert.True(_connectionService.TestRtpDataProcessed);
            _rtspServerMock.Verify(x => x.SendRtpVideoData(_connectionOneId,It.IsAny<byte[]>()),Times.Never);

        }

        [Fact]
        public void SendRtpAudioData_OfVideoSourceOneOnAddAndRemoveConnectionReceivedRtpAudio_Never()
        {            
            VideoSourceOneAddAndRemoveConnection();
            _rtspClientMock.Raise(x => x.Received_Rtp += null,_rtspClientMock.Object, _rtpAudioChannel,null);
            Assert.True(_connectionService.TestRtpDataProcessed);
            _rtspServerMock.Verify(x => x.SendRtpAudioData(_connectionOneId,It.IsAny<byte[]>()),Times.Never);

        }

         [Fact]
        public void SendRtcpVideoData_OfVideoSourceOneOnAddAndRemoveConnectionReceivedRtcpVideo_Never()
        {            
            VideoSourceOneAddAndRemoveConnection();
            _rtspClientMock.Raise(x => x.Received_Rtcp += null,_rtspClientMock.Object, _rtcpVideoChannel,null);
            _rtspServerMock.Verify(x => x.SendRtcpVideoData(_connectionOneId,It.IsAny<byte[]>()),Times.Never);

        }

        [Fact]
        public void SendRtcpAudioData_OfVideoSourceOneOnAddAndRemoveConnectionReceivedRtcpAudio_Never()
        {            
            VideoSourceOneAddAndRemoveConnection();
            _rtspClientMock.Raise(x => x.Received_Rtcp += null,_rtspClientMock.Object, _rtcpAudioChannel,null);
            _rtspServerMock.Verify(x => x.SendRtcpAudioData(_connectionOneId,It.IsAny<byte[]>()),Times.Never);

        }

        [Fact]
        public void GetConnectionIds_OfVideoSourceOneOnAddConnection_IsOneGuidArray()
        {            
            VideoSourceOneAddConnection();
            var value = _connectionService.GetServerConnectionIds(_videoSourcesFixtures.VideoSourceOneId);
            Assert.True(value is Guid[] && value.Length == 1);
        }

        [Fact]
        public void GetConnectionIds_OfVideoSourceOneOnTwoAddConnection_IsTwoConnectionIdsArray()
        {            
            VideoSourceOneAddTwoConnections();
            var value = _connectionService.GetServerConnectionIds(_videoSourcesFixtures.VideoSourceOneId);
            Assert.True(value is Guid[] && value.Length == 2 && value[0] == _connectionOneId && value[1] == _connectionTwoId );
        }

        [Fact]
        public void GetConnectionIds_OfVideoSourceOneOnAddAndRemoveConnection_IsEmptyArray()
        {            
            VideoSourceOneAddAndRemoveConnection();
            var value = _connectionService.GetServerConnectionIds(_videoSourcesFixtures.VideoSourceOneId);
            Assert.True(value is Guid[] && value.Length == 0);
        }

        [Fact]
        public void GetConnectionIds__OfVideoSourceOneOnAddTwoAndRemoveOneConnection_IsOneGuidArray()
        {
            VideoSourceOneAddTwoAndRemoveOneConnections();
            var value = _connectionService.GetServerConnectionIds(_videoSourcesFixtures.VideoSourceOneId);
            Assert.True(value is Guid[] && value.Length == 1);
        }

        [Fact]
        public void HasClient_OfEmptyOnCreation_IsFalse()
        {            
            Assert.True(false == _connectionService.HasClient(Guid.Empty) );
        }

        [Fact]
        public void HasClient_OfVideoSourceOneOnCreation_IsFalse()
        {            
            Assert.True(false == _connectionService.HasClient(_videoSourcesFixtures.VideoSourceOneId) );
        }

        [Fact]
        public void HasClient_OfVideoSourceOneOnAddConnection_IsTrue()
        {            
            VideoSourceOneAddConnection();
            Assert.True(_connectionService.HasClient(_videoSourcesFixtures.VideoSourceOneId));
        }



        [Fact]
        public void ClientStarted_OfVideoSourceOneOnAddConnection_Once()
        {            
            VideoSourceOneAddConnection();
            _rtspClientMock.Verify(x => x.Start(),Times.Once);
        }

        [Fact]
        public void ClientCreated_OfVideoSourceOneOnAddConnection_Once()
        {            
            VideoSourceOneAddConnection();
            _rtspClientFactoryMock.Verify(x => x.Create(_videoSourcesFixtures.VideoSourceOne),Times.Once);
        }

        [Fact]
        public void ClientStarted_OfVideoSourceOneOnTwoAddConnection_IsTwoConnectionIdsArray()
        {            
            VideoSourceOneAddTwoConnections();
            _rtspClientMock.Verify(x => x.Start(),Times.Once);
        }

        [Fact]
        public void ClientStarted_OfVideoSourceOneOnAddTwoAndRemoveOneConnection_Once()
        {            
            VideoSourceOneAddTwoAndRemoveOneConnections();
            _rtspClientMock.Verify(x => x.Start(),Times.Once);
        }

        [Fact]
        public void HasClient_OfVideoSourceOneOnAddAndRemoveConnection_IsFalse()
        {            
            VideoSourceOneAddAndRemoveConnection();
            Assert.True(false == _connectionService.HasClient(_videoSourcesFixtures.VideoSourceOneId));
        }

        [Fact]
        public void ClientStopped_OfVideoSourceOneOnAddAndRemoveConnection_Once()
        {            
            VideoSourceOneAddAndRemoveConnection();
            _rtspClientMock.Verify(x => x.Stop(RtspClientStopReason.COMMAND),Times.Once);
        }

        [Fact]
        public void ClientStopped_OfVideoSourceOneOnAddTwoAndRemoveOneConnection_Never()
        {            
            VideoSourceOneAddTwoAndRemoveOneConnections();
            _rtspClientMock.Verify(x => x.Stop(RtspClientStopReason.COMMAND),Times.Never);
        }


        [Fact]
        public void IsClientRunning_OfEmptyOnCreation_IsFalse()
        {            
            Assert.True(false == _connectionService.IsClientRunning(Guid.Empty) );
        }

        [Fact]
        public void IsClientRunning_OfVideoSourceOneOnCreation_IsFalse()
        {            
            Assert.True(false == _connectionService.IsClientRunning(_videoSourcesFixtures.VideoSourceOneId) );
        }
    }
}
