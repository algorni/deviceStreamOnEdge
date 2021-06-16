using DeviceStreamCommon;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Transport.Mqtt;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DeviceStreamProxyModule
{
    class Program
    {
        static private CancellationTokenSource cts = new CancellationTokenSource();

        private static ModuleClient moduleClient;

        static void Main(string[] args)
        {
            Console.WriteLine("Hello DeviceStreamModule");
            Console.WriteLine("-----------------------");

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
        /// Initializes the ModuleClient and sets up the callback to receive
        /// direct method callback to enable or disable the DeviceStream functionality
        /// </summary>
        static async Task Init()
        {
            
            //var trasnportSetting = new Http1TransportSettings();
            var trasnportSetting = new AmqpTransportSettings(TransportType.Amqp_Tcp_Only);
            //var trasnportSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            ITransportSettings[] settings = { trasnportSetting };

            Console.WriteLine($"Transport Type {settings[0].GetTransportType()}");

            // Open a connection to the Edge runtime
            moduleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);            
            await moduleClient.OpenAsync();
            
            Console.WriteLine("IoT Hub Device Stream Proxy Module client initialized.");

            Tuple<ModuleClient, CancellationTokenSource> methodHandlerParams = 
                new Tuple<ModuleClient, CancellationTokenSource>(moduleClient, cts);

            await moduleClient.SetMethodHandlerAsync(DeviceStreamDirectMethods.InitiateDeviceStream, 
                DeviceStreamModuleHandler.InitiateDeviceStreamMethodHandler, methodHandlerParams);

            Console.WriteLine("Waiting for initial DirectMethod call to enable Device Stream");
        }

    }
}
