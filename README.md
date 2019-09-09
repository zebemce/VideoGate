# VideoGate
RTSP video relay service

* This program can receive RTSP video and audio streams and pass them to clients with multipexing.
* It supports both UDP and TCP transport and allows to transfer video streams from one network to another.
* The solution has been built by .NET Core 2.1 and tested on Windows 10 and Ubuntu 18.
* Great thanks to contributors of https://github.com/ngraziano/SharpRTSP project for their RTSP modules

To start using the relay just build it, publish and edit sources.json in output directory. 

On windows program can be installed as a service by calling 
 ```
dotnet App.dll --register-service 
 ```

On both platforms it can be started by calling 
 ```
dotnet App.dll
 ```
 
 You can open result stream in VLC or ffplay player by url
  ```
  rtsp://localhost:8556/live/00000000-0000-0000-0000-000000000001
 ```
 
