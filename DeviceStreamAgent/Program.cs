using DeviceStreamCommon;
using Microsoft.Azure.Devices.Client;
using Microsoft.Extensions.Configuration;
using System;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

namespace DeviceStreamAgent
{
    class Program
    {
        private static IConfiguration configuration;

        private static string deviceConnectionString;
     
        private static TransportType transportType = TransportType.Amqp;
        
        private static DeviceClient deviceClient;

        private static CancellationTokenSource cts;

        static void Main(string[] args)
        {
            Console.WriteLine("Hello DeviceStreamAgent");
            Console.WriteLine("-----------------------");

            configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

            // Wait until the app unloads or is cancelled
            cts = new CancellationTokenSource();

            //Initialize Application
            Init().Wait();

            // Wait until the app unloads or is cancelled
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();

            Console.WriteLine("Done.");
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the DeviceClient and sets up the callback to receive
        /// direct method callback to enable or disable the DeviceStream functionality
        /// </summary>
        static async Task Init()
        {
            //checking configuration requirements
            deviceConnectionString = configuration.GetValue<string>("DEVICE_CONNECTIONSTRING");

            if (string.IsNullOrEmpty(deviceConnectionString))
            {
                Console.WriteLine("This tool requires a <DEVICE_CONNECTIONSTRING> parameter to connect to IoT Hub as Device.");
                return;
            }
            
            //create the Device Client
            deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, transportType);

            Console.WriteLine("Device Client connected");

            Tuple<DeviceClient, CancellationTokenSource> methodHandlerParams = 
                new Tuple<DeviceClient, CancellationTokenSource>(deviceClient, cts);
          
            await deviceClient.SetMethodHandlerAsync(DeviceStreamDirectMethods.InitiateDeviceStream, 
                DeviceStreamDeviceHandler.InitiateDeviceStreamMethodHandler, methodHandlerParams);

            Console.WriteLine("Waiting for initial DirectMethod call to enable Device Stream");
        }
    }
}
