using System;
using Moq;
using VideoGate.Infrastructure.Interfaces;
using VideoGate.Infrastructure.Models;
using VideoGate.Services;
using VideoGate.Tests.TestFixtures;
using Xunit;

namespace VideoGate.Tests.Services
{
    public class VideoSourceStorageTest
    {
        protected IVideoSourceStorage _videoSourceStorage;
        protected readonly VideoSourcesFixtures _videoSourcesFixtures;
        protected readonly Mock<IVideoSourceDatabase> _videoSourceDatabaseMock;

        public VideoSourceStorageTest()
        {
            _videoSourceDatabaseMock = new Mock<IVideoSourceDatabase>();
            _videoSourcesFixtures = new VideoSourcesFixtures();
            
            
        }

        protected void SetupTwoSources()
        {
            _videoSourceDatabaseMock.Setup(x => x.Load()).Returns(
                new VideoSource[] {_videoSourcesFixtures.VideoSourceOne,_videoSourcesFixtures.VideoSourceTwo}
            );  
            _videoSourceStorage = new VideoSourceStorage(_videoSourceDatabaseMock.Object);    
        }

        protected void SetupOneSources()
        {
            _videoSourceDatabaseMock.Setup(x => x.Load()).Returns(
                new VideoSource[] {_videoSourcesFixtures.VideoSourceOne}
            );
            _videoSourceStorage = new VideoSourceStorage(_videoSourceDatabaseMock.Object);      
        }

        protected void SetupNoSources()
        {
            _videoSourceDatabaseMock.Setup(x => x.Load()).Returns(
                new VideoSource[]{}
            );   
            _videoSourceStorage = new VideoSourceStorage(_videoSourceDatabaseMock.Object);   
        }

        [Fact]
        public void Load_OnVideoSourceStorageCreation_Once()
        {
            SetupNoSources();
            _videoSourceDatabaseMock.Verify(x => x.Load(), Times.Once);
        }

        [Fact]
        public void Save_OnVideoSourceCreate_Once()
        {
            SetupNoSources();
            _videoSourceStorage.CreateVideoSource(_videoSourcesFixtures.VideoSourceOne);
            _videoSourceDatabaseMock.Verify(x => x.Save(new VideoSource[]{_videoSourcesFixtures.VideoSourceOne}), Times.Once);
        }

        [Fact]
        public void OnVideoSourceCreated_WhenVideoSourceCreate_Occured()
        {
            SetupNoSources();
            bool occured = false;
            _videoSourceStorage.OnVideoSourceCreated += (videoSource) =>
            {
                occured = true;
            };
            _videoSourceStorage.CreateVideoSource(_videoSourcesFixtures.VideoSourceOne);
            Assert.True(occured);
        }

        [Fact]
        public void Save_OnVideoSourceUpdate_Once()
        {
            SetupOneSources();
            _videoSourcesFixtures.VideoSourceTwo.Id = _videoSourcesFixtures.VideoSourceOne.Id; 
            _videoSourceStorage.UpdateVideoSource(_videoSourcesFixtures.VideoSourceTwo);
            _videoSourceDatabaseMock.Verify(x => x.Save(new VideoSource[]{_videoSourcesFixtures.VideoSourceTwo}), Times.Once);
        }

        [Fact]
        public void OnVideoSourceUpdated_WhenVideoSourceCreate_Occured()
        {
            SetupOneSources();
            bool occured = false;
            _videoSourceStorage.OnVideoSourceUpdated += (videoSource) =>
            {
                occured = true;
            };
            _videoSourceStorage.UpdateVideoSource(_videoSourcesFixtures.VideoSourceOne);
            Assert.True(occured);
        }

        [Fact]
        public void Save_OnVideoSourceDelete_Once()
        {
            SetupOneSources();
            _videoSourceStorage.DeleteVideoSource(_videoSourcesFixtures.VideoSourceOne.Id);
            _videoSourceDatabaseMock.Verify(x => x.Save(new VideoSource[]{}), Times.Once);
        }

        [Fact]
        public void OnVideoSourceDeleted_WhenVideoSourceDelete_Occured()
        {
            SetupOneSources();
            bool occured = false;
            _videoSourceStorage.OnVideoSourceDeleted += (videoSource) =>
            {
                occured = true;
            };
            _videoSourceStorage.DeleteVideoSource(_videoSourcesFixtures.VideoSourceOneId);
            Assert.True(occured);
        }

        [Fact]
        public void DeleteVideoSource_NotExistsId_ThrowsException()
        {
            SetupOneSources();
            Assert.ThrowsAny<Exception>(() => _videoSourceStorage.DeleteVideoSource(_videoSourcesFixtures.VideoSourceTwo.Id));
        }

        [Fact]
        public void UpdateVideoSource_NotExistsId_ThrowsException()
        {
            SetupOneSources();
            Assert.ThrowsAny<Exception>(() => _videoSourceStorage.UpdateVideoSource(_videoSourcesFixtures.VideoSourceTwo));
        }

        [Fact]
        public void CreateVideoSource_ExistsId_ThrowsException()
        {
            SetupOneSources();
            _videoSourcesFixtures.VideoSourceTwo.Id = _videoSourcesFixtures.VideoSourceOne.Id; 
            Assert.ThrowsAny<Exception>(() => _videoSourceStorage.CreateVideoSource(_videoSourcesFixtures.VideoSourceTwo));
        }

        [Fact]
        public void CreateVideoSource_ExistsCaption_ThrowsException()
        {
            SetupOneSources();
            _videoSourcesFixtures.VideoSourceTwo.Caption = _videoSourcesFixtures.VideoSourceOne.Caption; 
            Assert.ThrowsAny<Exception>(() => _videoSourceStorage.CreateVideoSource(_videoSourcesFixtures.VideoSourceTwo));
        }

        [Fact]
        public void UpdateVideoSource_DuplicateCaption_ThrowsException()
        {
            SetupTwoSources();
            _videoSourcesFixtures.VideoSourceTwo.Caption = _videoSourcesFixtures.VideoSourceOne.Caption; 
            Assert.ThrowsAny<Exception>(() => _videoSourceStorage.UpdateVideoSource(_videoSourcesFixtures.VideoSourceTwo));
        }

        [Fact]
        public void GetVideoSourceById_VideoSourceIdExists_ReturnsVideoSource()
        {
            SetupTwoSources();
            Assert.True(_videoSourceStorage.GetVideoSourceById(_videoSourcesFixtures.VideoSourceOneId) == _videoSourcesFixtures.VideoSourceOne);
        }

        [Fact]
        public void GetVideoSourceById_VideoSourceIdNotExists_ReturnsNull()
        {
            SetupOneSources();
            Assert.True(_videoSourceStorage.GetVideoSourceById(_videoSourcesFixtures.VideoSourceTwoId) == null);
        }

        [Fact]
        public void GetVideoSourceByCaption_VideoSourceIdExists_ReturnsVideoSource()
        {
            SetupTwoSources();
            Assert.True(_videoSourceStorage.GetVideoSourceByCaption(_videoSourcesFixtures.VideoSourceOne.Caption) == _videoSourcesFixtures.VideoSourceOne);
        }

        [Fact]
        public void GetVideoSourceByCaption_VideoSourceIdNotExists_ReturnsNull()
        {
            SetupOneSources();
            Assert.True(_videoSourceStorage.GetVideoSourceByCaption(_videoSourcesFixtures.VideoSourceTwo.Caption) == null);
        }

        [Fact]
        public void VideoSources_Length_EqualsTwo()
        {
            SetupTwoSources();
            Assert.True(_videoSourceStorage.VideoSources.Length == 2);
        }

    }
}
