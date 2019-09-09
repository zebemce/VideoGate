using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json.Serialization;
using RTSPServer;
using VideoGate.Infrastructure.Interfaces;
using VideoGate.Services;

namespace VideoGate
{
    public class Startup
    {

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddSingleton<IConnectionService,ConnectionService>()
                .AddSingleton<IRequestUrlVideoSourceResolverStrategy, RequestUrlVideoSourceResolverStrategy>()
                .AddSingleton<IRtspClientFactory, RtspClientFactory>()
                .AddSingleton<IRtspServer, RtspServer>()
                .AddSingleton<IVideoSourceDatabase, VideoSourceDatabase>()
                .AddSingleton<IVideoSourceStorage, VideoSourceStorage>()
              
                .AddMvc()
                .AddJsonOptions(options => 
                { 
                    options.SerializerSettings.ContractResolver = new DefaultContractResolver();
                   // options.SerializerSettings.Converters.Add(new SomeConverter());
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.ApplicationServices.GetServices<IConnectionService>();
            IDirectoryPathService directoryPathService = app.ApplicationServices.GetService<IDirectoryPathService>();
            
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseMvc();

            FileServerOptions fileServerOptions = new FileServerOptions()
            {
                FileProvider = new PhysicalFileProvider(Path.Combine(directoryPathService.RootPath, "static")),
                RequestPath = "",
                EnableDirectoryBrowsing = false
            };

            fileServerOptions.DefaultFilesOptions.DefaultFileNames.Add("index.html");
            app.UseFileServer(fileServerOptions);

            ConfigureWebSockets(app);
        }

        private void ConfigureWebSockets(IApplicationBuilder app)
        {
          /*   IWebSocketCommunication SocketCommunication = app.ApplicationServices.GetService<IWebSocketCommunication>();
            
            if (SocketCommunication == null)
                throw new ApplicationException("IWebSocketCommunication not injected");

            var webSocketOptions = new WebSocketOptions()
            {
                KeepAliveInterval = TimeSpan.FromSeconds(10),
                ReceiveBufferSize = 4*1024
            };
            app.UseWebSockets(webSocketOptions);

            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/ws")
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                     //   await SocketCommunication.NewConnection(context);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                }
                else
                {
                    await next();
                }
            });*/
        }
    }
}
