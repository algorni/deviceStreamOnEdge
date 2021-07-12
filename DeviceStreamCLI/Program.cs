using DeviceStreamCommon;
using Microsoft.Azure.Devices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

namespace DeviceStreamCLI
{
    class Program
    {
        // The IoT Hub connection string. This is available under the "Shared access policies" in the Azure portal.
        private static IConfiguration configuration;
      

        private static string ioTHubConnectionString = null; //IOTHUB_CONNECTIONSTRING
        private static string deviceId = null;  //DEVICE_ID
        private static string moduleName = null;  //MODULE_NAME (optional)

        //if provided this information ---> on the end side will use an additional device client to establish the connnection (even if running within a module)
        private static string alternativeDeviceId = null;  // (optional)
        private static string alternativeDeviceConnectionString = null;  //(optional)

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

            Console.WriteLine("Hello DeviceStreamCLI!");
            Console.WriteLine("-----------------------");


            //checking configuration requirements
            ioTHubConnectionString = configuration.GetValue<string>("IOTHUB_CONNECTIONSTRING");

            if (string.IsNullOrEmpty(ioTHubConnectionString))
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

            //can be optional
            alternativeDeviceId = configuration.GetValue<string>("ALTENRATIVE_DEVICE_ID", null);

            //can be optional
            alternativeDeviceConnectionString = configuration.GetValue<string>("ALTENRATIVE_DEVICE_CONNECTIONSTRING", null);



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
            ServiceClient serviceClient = ServiceClient.CreateFromConnectionString(ioTHubConnectionString, transportType);
            
            Console.WriteLine("Service Client connected");

            //initiating the Direct Method to start the stream on the other end...
            var methodRequest = new CloudToDeviceMethod(                
                DeviceStreamDeviceHelper.InitiateDeviceStream //constant with the Direct Method name
                );

            InitiateDeviceStreamRequest requestPayload = new InitiateDeviceStreamRequest() { 
                TargetHost = remoteHost, 
                TargetPort = remotePort, 
                //the use device connection string could be also null....  if it has a valid connection string then the end device will open the device stream as another client
                UseDeviceConnectionString = alternativeDeviceConnectionString };

            methodRequest.SetPayloadJson(requestPayload.ToJson());

            //perform a Direct Method to the remote device to initiate the device stream!
            CloudToDeviceMethodResult response = null;

            if (!string.IsNullOrEmpty(moduleName)) 
            {        
                if (string.IsNullOrEmpty(alternativeDeviceConnectionString))
                {
                    Console.WriteLine("--------------------------\nNOTICE: for Module initiated Device Stream unfortunately you can't rely onthe Module Identity to initiate the Device Stream process since actually is not supported.\n--------------------------");
                }

                return;

                //Console.WriteLine($"Performing remote Module DirectMethod call to Device: {deviceId} Module: {moduleName} to enable Device Stream on remote host: {remoteHost}:{remotePort}");

                //response = await serviceClient.InvokeDeviceMethodAsync(deviceId, moduleName, methodRequest);
            }
            else
            {
                Console.WriteLine($"Performing remote Device DirectMethod call to Device: {deviceId} to enable Device Stream on remote host: {remoteHost}:{remotePort}");

                response = await serviceClient.InvokeDeviceMethodAsync(deviceId, methodRequest);
            }                

            if (response.Status == 200)
            {
                InitiateDeviceStreamResponse responseBody = InitiateDeviceStreamResponse.FromJson(response.GetPayloadAsJson());

                if (responseBody.RequestAccepted)
                {
                    Console.WriteLine($"Device Stream request accepted: {responseBody.Reason}");

                    DeviceStreamClientHelper deviceStreamClientHandler = null;

                    if (string.IsNullOrEmpty(alternativeDeviceId))
                    {
                        //check if module is null then initiate a device stream to a device client
                        if (string.IsNullOrEmpty(moduleName))
                        {
                            deviceStreamClientHandler = new DeviceStreamClientHelper(serviceClient, deviceId, localPort, streamName, cts);
                        }
                        else
                        {
                            deviceStreamClientHandler = new DeviceStreamClientHelper(serviceClient, deviceId, moduleName, localPort, streamName, cts);
                        }
                    }
                    else
                    {
                        //use an alternative device id (device side the direct method call will initialize a new DeviceClient for this device id and initiate the device stream via this alternative device identity)
                        //this is a workaround for running as Module since actually unsupported
                        deviceStreamClientHandler = new DeviceStreamClientHelper(serviceClient, alternativeDeviceId, localPort, streamName, cts);
                    }
                    
                    //start streaming session when an incoming request will happens
                    await deviceStreamClientHandler.StartListeningLocalPort();

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
