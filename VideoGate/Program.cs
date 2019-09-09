using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DasMulli.Win32.ServiceUtils;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using NLog.Web;
using VideoGate.Services;

namespace VideoGate
{
    
    public class Program
    {
        private readonly static NLog.ILogger logger;

        private const string RunAsServiceFlag = "--run-as-service";
        private const string RunAsDaemonFlag = "--run-as-daemon";
        private const string RegisterServiceFlag = "--register-service";
        private const string UnregisterServiceFlag = "--unregister-service";
        private const string InteractiveFlag = "--interactive";
        private const string HelpFlag = "--help";

        private const string ServiceName = "VideoGateService";
        private const string ServiceDisplayName = "Video Gate Service";
        private const string ServiceDescription = "Video retranslation service";

        static Program()
        {
            ConfigureLoggers();
            logger = LogManager.GetLogger("Program");
        }

        public static void Main(string[] args)
        {
            
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            try
            {
                if (args.Contains(RunAsServiceFlag))
                {
                    RunAsService(args);
                }
                else if (args.Contains(RunAsDaemonFlag))
                {
                    RunAsDaemon(args);
                }
                else if (args.Contains(RegisterServiceFlag))
                {
                    RegisterService();
                }
                else if (args.Contains(UnregisterServiceFlag))
                {
                    UnregisterService();
                }
                else if (args.Contains(InteractiveFlag))
                {
                    RunInteractive(args);
                }
                else if (args.Contains(HelpFlag))
                {
                    DisplayHelp();
                }
                else
                {
                    RunAsDaemon(args);
                }
            }
            catch (Exception ex)
            {
                logger.Error($"An error ocurred: {ex.Message}");
            }
            

        }

        private static void RunAsService(string[] args)
        {
            logger.Info("Running as service");
            var testService = new AppWin32Service(args.Where(a => a != RunAsServiceFlag).ToArray());
            testService.OnStart += Start;
            testService.OnStop += Stop;
            var serviceHost = new Win32ServiceHost(testService);
            serviceHost.Run();
        }

        private static void RunAsDaemon(string[] args)
        {
            logger.Info("Running as daemon");
            try
            {
                Start(null, new StringsEventArgs(args));
                if (CheckConsole())
                {
                    logger.Info("Application running. Press Ctrl+C to shut down.");
                }
                ConsoleHost.WaitForShutdownAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                ShowUnhandledException(ex);
            }
            finally
            {
                Stop(null, EventArgs.Empty);
            }
        }


        private static void RunInteractive(string[] args)
        {
            var testService = new AppWin32Service(args.Where(a => a != InteractiveFlag).ToArray());
            testService.OnStart += Start;
            testService.OnStop += Stop;

            logger.Info("Running as console application");
            try
            {
                testService.Start(new string[0], () => { });
                if (CheckConsole())
                {
                    logger.Info("Press any key to stop...");
                    Console.ReadKey(true);
                }


            }
            catch (Exception ex)
            {
                ShowUnhandledException(ex);

            }
            finally
            {
                testService.Stop();
                if (CheckConsole())
                {
                    logger.Info("Press any key to exit");
                    Console.ReadKey();
                }

            }
        
        }

        private static void Stop(object sender, EventArgs e)
        {
            logger.Info("VideoGate stopping...");
            InitializerSingleton.Instance.Close();
            logger.Info("VideoGate stopped");
        }

        private static void Start(object sender, StringsEventArgs e)
        { 
            logger.Info("VideoGate starting...");
            InitializerSingleton.Instance.Init();
            logger.Info("VideoGate started");
        }

        private static void RegisterService()
        {
            // Environment.GetCommandLineArgs() includes the current DLL from a "dotnet my.dll --register-service" call, which is not passed to Main()
            var remainingArgs = Environment.GetCommandLineArgs()
                .Where(arg => arg != RegisterServiceFlag)
                .Select(EscapeCommandLineArgument)
                .Append(RunAsServiceFlag);

            var host = Process.GetCurrentProcess().MainModule.FileName;

            if (!host.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                // For self-contained apps, skip the dll path
                remainingArgs = remainingArgs.Skip(1);
            }

            var fullServiceCommand = host + " " + string.Join(" ", remainingArgs);

            // Do not use LocalSystem in production.. but this is good for demos as LocalSystem will have access to some random git-clone path
            // Note that when the service is already registered and running, it will be reconfigured but not restarted
            var serviceDefinition = new ServiceDefinitionBuilder(ServiceName)
                .WithDisplayName(ServiceDisplayName)
                .WithDescription(ServiceDescription)
                .WithBinaryPath(fullServiceCommand)
                .WithCredentials(Win32ServiceCredentials.LocalSystem)
                .WithAutoStart(true)
                .Build();

            new Win32ServiceManager().CreateOrUpdateService(serviceDefinition, startImmediately: true);

            Console.WriteLine($@"Successfully registered and started service ""{ServiceDisplayName}"" (""{ServiceDescription}"")");
        }

        private static void UnregisterService()
        {
            new Win32ServiceManager()
                .DeleteService(ServiceName);

            Console.WriteLine($@"Successfully unregistered service ""{ServiceDisplayName}"" (""{ServiceDescription}"")");
        }

        private static void DisplayHelp()
        {
            Console.WriteLine(ServiceDescription);
            Console.WriteLine();
            Console.WriteLine("This application is intened to be run as windows service. Use one of the following options:");
            Console.WriteLine("  --register-service        Registers and starts this program as a windows service named \"" + ServiceDisplayName + "\"");
           // Console.WriteLine("                            All additional arguments will be passed to ASP.NET Core's WebHostBuilder.");
            Console.WriteLine("  --unregister-service      Removes the windows service creatd by --register-service.");
            Console.WriteLine("  --interactive             Runs the underlying asp.net core app. Useful to test arguments.");
        }

        private static string EscapeCommandLineArgument(string arg)
        {
            // http://stackoverflow.com/a/6040946/784387
            arg = Regex.Replace(arg, @"(\\*)" + "\"", @"$1$1\" + "\"");
            arg = "\"" + Regex.Replace(arg, @"(\\+)$", @"$1$1") + "\"";
            return arg;
        }



        private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            ShowUnhandledException(e.Exception);
        }

        public static void ShowUnhandledException(Exception ex)
        {
            if (logger == null)
                throw ex;
            logger.Fatal(ex, "Unhandled EXCEPTION: {0}  \r\n {1} - {2}",
                    ex.ToString(), ex.InnerException?.Message, ex.InnerException?.StackTrace);
        }

        public static void ConfigureLoggers()
        {
            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddNLog();
            string rootPath = Directory.GetParent(typeof(Program).GetTypeInfo().Assembly.Location).FullName;
            string nlogConfigPath = Path.Combine(rootPath, "nlog.config");
            if (isDEBUG())
            {
                nlogConfigPath = Path.Combine(rootPath, "nlog.debug.config");
            }



            if (File.Exists(nlogConfigPath))
                NLog.LogManager.LoadConfiguration(nlogConfigPath);
            
        }

        private static bool CheckConsole()
        {
            try
            {
                string title = Console.Title;
                return true;

            }
            catch
            {
                return false;
            }
            
        }

        private static bool isDEBUG()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }

    }
}
