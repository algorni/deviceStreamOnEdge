using DeviceStreamCommon;
using Microsoft.Azure.Devices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

namespace DeviceStreamUtil
{
    class Program
    {
        // The IoT Hub connection string. This is available under the "Shared access policies" in the Azure portal.
        private static IConfiguration configuration;
      

        private static string ioTHubconnectionString = null; //IOTHUB_CONNECTIONSTRING
        private static string deviceId = null;  //DEVICE_ID
        private static string moduleName = null;  //MODULE_NAME (optional)
        private static int localPort; //LOCAL_PORT
        private static string streamName = null; //STREAM_NAME
        private static string remoteHost = null;  //REMOTE_HOST
        private static int remotePort; //REMOTE_PORT

        // Select one of the following transports used by ServiceClient to connect to IoT Hub.
        private static TransportType transportType = TransportType.Amqp;
        //private static TransportType s_transportType = TransportType.Amqp_WebSocket_Only;


        static async Task Main(string[] args)
        {
            configuration = new ConfigurationBuilder()
             .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
             .AddEnvironmentVariables()
             .AddCommandLine(args)
             .Build();

            Console.WriteLine("Hellp DeviceStreamUtil!");
            Console.WriteLine("-----------------------");


            //checking configuration requirements
            ioTHubconnectionString = configuration.GetValue<string>("IOTHUB_CONNECTIONSTRING");

            if (string.IsNullOrEmpty(ioTHubconnectionString))
            {
                Console.WriteLine("This tool requires a <IOTHUB_CONNECTIONSTRING> parameter to connect to IoT Hub to initiate the Device Stream connection.");
                return;
            }

            deviceId = configuration.GetValue<string>("DEVICE_ID");

            if (string.IsNullOrEmpty(deviceId))
            {
                Console.WriteLine("This tool requires a <DEVICE_ID> parameter as target to connect with IoT Hub Device Stream.");
                return;
            }

            //module name can be optional
            moduleName = configuration.GetValue<string>("MODULE_NAME",null);

            //if (string.IsNullOrEmpty(moduleName))
            //{
            //    Console.WriteLine("This tool requires a <MODULE_NAME> parameter as target to connect with IoT Hub Device Stream.");
            //    return;
            //}            

            localPort = configuration.GetValue<int>("LOCAL_PORT",0);

            if (localPort==0)
            {
                Console.WriteLine("This tool requires a <LOCAL_PORT> parameter as local port to which listen for incoming connection requests for Device Stream.");
                return;
            }

            streamName = configuration.GetValue<string>("STREAM_NAME");

            if (string.IsNullOrEmpty(streamName))
            {
                Console.WriteLine("This tool requires a <STREAM_NAME> parameter as name of the IoT Hub Device Stream session name.");
                return;
            }

            remotePort = configuration.GetValue<int>("REMOTE_PORT",0);

            if (remotePort == 0)
            {
                Console.WriteLine("This tool requires a <REMOTE_PORT> parameter as remote port to terminate the incoming connection requests into.");
                return;
            }

            remoteHost = configuration.GetValue<string>("REMOTE_HOST");

            if (string.IsNullOrEmpty(remoteHost))
            {
                Console.WriteLine("This tool requires a <REMOTE_HOST> parameter as target to connect with IoT Hub Device Stream.");
                return;
            }

            var cts = new CancellationTokenSource();

            //initiate a client to IoT Hub 
            ServiceClient serviceClient = ServiceClient.CreateFromConnectionString(ioTHubconnectionString, transportType);
            
            Console.WriteLine("Service Client connected");

            var methodRequest = new CloudToDeviceMethod(DeviceStreamDirectMethods.InitiateDeviceStream);

            InitiateDeviceStreamRequest requestPayload = new InitiateDeviceStreamRequest() { TargetHost = remoteHost, TargetPort = remotePort };

            methodRequest.SetPayloadJson(requestPayload.ToJson());

            //perform a Direct Method to the remote device to initiate the device stream!
            CloudToDeviceMethodResult response = null;

            if (!string.IsNullOrEmpty(moduleName))
            { 
                Console.WriteLine($"Performing remote Module DirectMethod call to enable Device Stream on remote host: {remoteHost}:{remotePort}");

                response = await serviceClient.InvokeDeviceMethodAsync(deviceId, moduleName, methodRequest);
            }
            else
            {
                Console.WriteLine($"Performing remote Device DirectMethod call to enable Device Stream on remote host: {remoteHost}:{remotePort}");

                response = await serviceClient.InvokeDeviceMethodAsync(deviceId, methodRequest);
            }                

            if (response.Status == 200)
            {
                InitiateDeviceStreamResponse responseBody = InitiateDeviceStreamResponse.FromJson(response.GetPayloadAsJson());

                if (responseBody.RequestAccepted)
                {
                    Console.WriteLine($"Device Stream request accepted: {responseBody.Reason}");

                    var deviceStreamClientHandler = new DeviceStreamClientHandler(serviceClient, deviceId, localPort, streamName);

                    //start streaming session when an incoming request will happens
                    await deviceStreamClientHandler.StartDeviceStreamSession();

                    Console.WriteLine($"Streaming session initialized. Main Thread sleeping.");
                }
                else
                {
                    Console.WriteLine($"Error while initiating the Device Stream: {responseBody.Reason}");
                }
            }
            else
            {
                Console.WriteLine("Error while calling remote method to initiate the Device Stream");
            }
            

            // Wait until the app unloads or is cancelled            
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();

            WhenCancelled(cts.Token).Wait();

            Console.WriteLine("Done Closing the app.");
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
       
    }
}
