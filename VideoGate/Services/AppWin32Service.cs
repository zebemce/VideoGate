using System;
using DasMulli.Win32.ServiceUtils;
using NLog;

namespace VideoGate.Services
{
     public class StringsEventArgs : EventArgs
    {

        public string[] Args { get; private set;}

        public StringsEventArgs(string[] args)
        {
            Args = args;
        }
    }

    public class AppWin32Service : IWin32Service
    {
        private readonly string[] commandLineArguments;
        private readonly ILogger _logger = LogManager.GetLogger("AppWin32Service");
        private bool stopRequestedByWindows;

        public AppWin32Service(string[] commandLineArguments)
        {
            this.commandLineArguments = commandLineArguments;
        }

        public string ServiceName => "ITCL Licensing Runtime Service";

        public event EventHandler<StringsEventArgs> OnStart;

        public event EventHandler OnStop;



        public void Start(string[] startupArguments, ServiceStoppedCallback serviceStoppedCallback)
        {
            // in addition to the arguments that the service has been registered with,
            // each service start may add additional startup parameters.
            // To test this: Open services console, open service details, enter startup arguments and press start.
            _logger.Info("Windows service starting");
            try
            {
                string[] combinedArguments;
                if (startupArguments.Length > 0)
                {
                    combinedArguments = new string[commandLineArguments.Length + startupArguments.Length];
                    Array.Copy(commandLineArguments, combinedArguments, commandLineArguments.Length);
                    Array.Copy(startupArguments, 0, combinedArguments, commandLineArguments.Length, startupArguments.Length);
                }
                else
                {
                    combinedArguments = commandLineArguments;
                }

            
                OnStart?.Invoke(this, new StringsEventArgs(combinedArguments));
            }
            catch (Exception ex)
            {
                _logger.Fatal("Service start failure: "+ ex.ToString());
                throw;
            }

            


        }

        public void Stop()
        {
            _logger.Info("Windows service stopping");
            stopRequestedByWindows = true;
            OnStop?.Invoke(this, EventArgs.Empty);
        }
    }
}