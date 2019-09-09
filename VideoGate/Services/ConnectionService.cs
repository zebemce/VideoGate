using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using VideoGate.Infrastructure.Interfaces;
using VideoGate.Infrastructure.Models;
using System.Threading;
using NLog;
using System.Threading.Tasks;

namespace VideoGate.Services
{
    public class ConnectionService : IConnectionService
    {
        protected readonly ConcurrentDictionary<Guid,List<Guid>> _serverConnectionsDictionary;
        protected readonly ConcurrentDictionary<Guid,CancellationTokenSource> _cancellationTokenSourceDictionary;
        protected readonly ConcurrentDictionary<Guid, IRtspClient> _rtspClientsDictionary;
        protected readonly IVideoSourceStorage _videoSourceStorage;
        protected readonly IRtspClientFactory _rtspClientFactory;
        protected readonly IRtspServer _rtspServer;
        
        protected const int TEST_WAIT_TIMEOUT = 100;
        protected const int CONNECTION_IDLE_PERIOD = 30000;
        protected readonly ManualResetEvent _testRtpDataEvent = new ManualResetEvent(true);
        protected readonly ILogger _logger = LogManager.GetLogger("ConnectionService");
        protected readonly object _sychGetSdpTasks = new object();
        protected readonly object _sychSendToConnections = new object();

        public bool TestRtpDataProcessed 
        {
            get 
            {
                return _testRtpDataEvent.WaitOne(TEST_WAIT_TIMEOUT);
            }
        }

        public bool DoNotPostponeClientClosingForTest {get; set;} = false;

        public ConnectionService(IVideoSourceStorage videoSourceStorage, IRtspClientFactory rtspClientfactory,
            IRtspServer rtspServer)
        {
            _videoSourceStorage = videoSourceStorage;
            _rtspClientFactory = rtspClientfactory;
            
            _rtspServer = rtspServer;
            _rtspServer.OnConnectionAdded += RtspServerConnectionAdded;
            _rtspServer.OnConnectionRemoved += RtspServerConnectionRemoved;
            _rtspServer.OnProvideSdpData += RtspServerProvideSdpData;

            _rtspClientsDictionary = new ConcurrentDictionary<Guid, IRtspClient>();

            _serverConnectionsDictionary = new ConcurrentDictionary<Guid,List<Guid>>(
                _videoSourceStorage.VideoSources.ToList().ConvertAll(vs => 
                {
                    return new KeyValuePair<Guid, List<Guid>>(vs.Id,new List<Guid>());
                })
            );

            _cancellationTokenSourceDictionary =  new ConcurrentDictionary<Guid, CancellationTokenSource>();
        }
        
        public Guid[] GetServerConnectionIds(Guid videoSourceId)
        {
            List<Guid> connectionIds;
            if(_serverConnectionsDictionary.TryGetValue(videoSourceId, out connectionIds))
            {
                return connectionIds.ToArray();
            }

            return null;
        }

        public bool IsClientRunning(Guid videoSourceId)
        {
            IRtspClient rtspClient;
            if(_rtspClientsDictionary.TryGetValue(videoSourceId, out rtspClient))
            {
                return rtspClient.IsRunning;
            }

            return false;
        }

        public bool HasClient(Guid videoSourceId)
        {
            IRtspClient rtspClient;
            return _rtspClientsDictionary.TryGetValue(videoSourceId, out rtspClient);
        }

        protected void RtspServerConnectionAdded(Guid connectionId, VideoSource videoSource)
        {
            _serverConnectionsDictionary.AddOrUpdate(
                videoSource.Id, 
                new List<Guid>(), 
                (key,oldValue) => 
                {
                    lock(oldValue)
                    {
                        oldValue.Add(connectionId);
                         _logger.Trace($"{connectionId} RtspServerConnectionAdded for video source {videoSource.Id} count: {oldValue.Count}");
                        _cancellationTokenSourceDictionary.AddOrUpdate(
                            videoSource.Id,
                            new CancellationTokenSource(),
                            (k,v) => {
                                v.Cancel();
                                return new CancellationTokenSource();
                            });
                    }
                    
                    StartRtspClientIfNecessary(videoSource);
                    return oldValue;
                }
            );
        }

        protected void ProcessConnectionRemoved(List<Guid> connectionIds, VideoSource videoSource)
        {
            lock(connectionIds)
            {                        
                if(false == connectionIds.Any())
                {
                    StopRtspClient(videoSource);
                }
            }
        }

        protected void RtspServerConnectionRemoved(Guid connectionId, VideoSource videoSource)
        {
            _serverConnectionsDictionary.AddOrUpdate(
                videoSource.Id, 
                new List<Guid>(), 
                (key,oldValue) => 
                {
                    bool wasRemoved; 
                    bool isEmpty;
                    lock(oldValue)
                    {                        
                        wasRemoved = oldValue.Remove(connectionId);
                        isEmpty = !oldValue.Any();
                        _logger.Trace($"{connectionId} RtspServerConnectionRemoved for video source {videoSource.Id} count: {oldValue.Count}");
                    }
                    
                    if (wasRemoved && isEmpty)
                    {
                        if (DoNotPostponeClientClosingForTest)
                        {
                            ProcessConnectionRemoved(oldValue,videoSource);
                        }
                        else
                        {
                            Task.Run(async delegate {
                                try
                                {
                                    CancellationTokenSource cancellationTokenSource = _cancellationTokenSourceDictionary[videoSource.Id];
                                    await Task.Delay(TimeSpan.FromMilliseconds(CONNECTION_IDLE_PERIOD),cancellationTokenSource.Token);
                                    ProcessConnectionRemoved(oldValue,videoSource);
                                }
                                catch(OperationCanceledException)
                                {
                                    _logger.Trace("ProcessConnectionRemoved cancelled");
                                }
  
                            });
                        }                        
                    }
               
                    return oldValue;
                }
            );
        }

        private byte[] GetSdpByRtspClient(object state)
        {                
                IRtspClient rtspClient = ((Tuple<IRtspClient,Guid>)state).Item1;
                Guid connectionId = ((Tuple<IRtspClient,Guid>)state).Item2;
                _logger.Trace($"Waiting RTSP client ready for video source {rtspClient.VideoSourceId} connection {connectionId}");
                if (rtspClient.WaitReady())
                {
                    _logger.Trace($"Waiting RTSP client ready SUCCESS for video source {rtspClient.VideoSourceId} connection {connectionId}");
                    return rtspClient.SdpData;
                } 

                _logger.Error($"Waiting RTSP client ready FAILED for video source {rtspClient.VideoSourceId} connection {connectionId}");
                return null;
        }

        protected Task<byte[]> RtspServerProvideSdpData(Guid connectionId, VideoSource videoSource)
        {
            _logger.Trace($"{connectionId} {videoSource.Id} RtspServerProvideSdpData");
            lock(_sychGetSdpTasks)
            {
                IRtspClient rtspClient;
                if (_rtspClientsDictionary.TryGetValue(videoSource.Id, out rtspClient))
                {
                    Func<object, byte[]> func = new Func<object, byte[]>(GetSdpByRtspClient);
                    
                    return Task.Factory.StartNew<byte[]>(func, new Tuple<IRtspClient,Guid>(rtspClient,connectionId)); 
                                                            
                } 

                return Task.FromResult<byte[]>(null);
            }                                                           
        }


        protected void StartRtspClientIfNecessary(VideoSource videoSource)
        {
            IRtspClient rtspClient;
            if (false == _rtspClientsDictionary.TryGetValue(videoSource.Id, out rtspClient))
            {
                rtspClient = _rtspClientFactory.Create(videoSource);
                if(_rtspClientsDictionary.TryAdd(videoSource.Id,rtspClient))
                {
                    rtspClient.Received_Rtp += RTSPClientReceivedData; 
                    rtspClient.Received_Rtcp += RTSPClientReceivedData; 
                    rtspClient.OnStopped += RTSPClientStopped;
                    rtspClient.Start();
                }
            }                                    
        }

        protected void StopRtspClient(VideoSource videoSource)
        {
            IRtspClient rtspClient;
            if (_rtspClientsDictionary.TryRemove(videoSource.Id, out rtspClient))
            {
                rtspClient.Received_Rtp -= RTSPClientReceivedData; 
                rtspClient.Received_Rtcp -= RTSPClientReceivedData; 
                rtspClient.OnStopped -= RTSPClientStopped;
                rtspClient.Stop();
            }                                    
        }

        protected void DeleteFailedRtspClient(Guid videoSourceId)
        {
            IRtspClient rtspClient;
            if (_rtspClientsDictionary.TryRemove(videoSourceId, out rtspClient))
            {
                rtspClient.Received_Rtp -= RTSPClientReceivedData; 
                rtspClient.Received_Rtcp -= RTSPClientReceivedData; 
                rtspClient.OnStopped -= RTSPClientStopped;
            }                                    
        }

        protected void RTSPClientReceivedData(IRtspClient sender, int channel, byte[] data)
        {            
            _testRtpDataEvent.Reset();
            
            lock(_sychSendToConnections)
            {
                List<Guid> connectionIds = _serverConnectionsDictionary.GetValueOrDefault(sender.VideoSourceId,new List<Guid>()); 
                int index = 0;
                while(index < connectionIds.Count)
                {
                    Guid connectionId;
                    try
                    {
                        connectionId = connectionIds[index];
                        index++;
                    }
                    catch
                    {
                        break;
                    }
                    
                    ThreadPool.QueueUserWorkItem(delegate(object state){
                        if (channel == sender.VideoRtpChannel)
                        {
                            _rtspServer.SendRtpVideoData(connectionId, data);
                        }
                        if (channel == sender.AudioRtpChannel)
                        {
                            _rtspServer.SendRtpAudioData(connectionId, data);
                        }
                        if (channel == sender.VideoRtcpChannel)
                        {
                            _rtspServer.SendRtcpVideoData(connectionId, data);
                        }
                        if (channel == sender.AudioRtcpChannel)
                        {
                            _rtspServer.SendRtcpAudioData(connectionId, data);
                        }
                        _testRtpDataEvent.Set();
                    },null);
                    
                }
            }
            
        }

        void RTSPClientStopped(IRtspClient sender, RtspClientStopReason reason)
        {
            if (reason != RtspClientStopReason.COMMAND)
            {
                List<Guid> connectionIds;
                _serverConnectionsDictionary.Remove(sender.VideoSourceId, out connectionIds);
                DeleteFailedRtspClient(sender.VideoSourceId);
                _rtspServer.ForceDisconnectPool(connectionIds);
                
            }
            
        }

        public void Dispose()
        {
            //TODO: stop all clients
          /*   while (_rtspClientsDictionary.Keys.Count > 0)
            {
                StopRtspClient(_rtspClientsDictionary.Keys[0])   
            }

             */
        }
    }
}
