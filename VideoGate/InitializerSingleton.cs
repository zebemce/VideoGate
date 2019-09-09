using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Web;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VideoGate.Infrastructure.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using VideoGate.Services;


namespace VideoGate
{
    public class InitializerSingleton
    {


        private readonly NLog.ILogger _logger = LogManager.GetLogger("Initializer");
        private IWebHost _webHost;

        public static InitializerSingleton Instance { get; private set; }

        static InitializerSingleton()
        {
            Instance = new InitializerSingleton();
        }

        public void Init()
        {
            _logger.Info(
             "\r\n^*********************************^" +
             "\r\n!                                 !" +
             "\r\n!    VideoGate started            !" +
             "\r\n!                                 !" +
             "\r\n^*********************************^ ");

            if (DirectoryPathService.isDEBUG()) _logger.Info("DEBUG mode ON");
          
            IDirectoryPathService directoryPathService = new DirectoryPathService();
            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(directoryPathService.RootPath,"appsettings.json"))
                .Build();
            IAppConfigurationFacade appConfigurationFacade = new AppConfigurationFacade(configuration);

           
            _logger.Info($"WebServer starting at http://+:"+appConfigurationFacade.HttpPort);


           _webHost = WebHost.CreateDefaultBuilder()
                .UseConfiguration(configuration)
                .ConfigureServices(provider =>
                    {
                        provider.TryAddSingleton<IAppConfigurationFacade>(appConfigurationFacade);
                        provider.TryAddSingleton<IDirectoryPathService>(directoryPathService);
                    }
                )
                .UseUrls("http://+:"+appConfigurationFacade.HttpPort)
                .UseStartup<Startup>() 
                .UseContentRoot(directoryPathService.WebContentRootPath)      
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
                })
                .UseNLog()      
                .Build();


            _webHost.Start();
        }

        public void Close()
        {
            _webHost.Dispose();
        }
    }

}