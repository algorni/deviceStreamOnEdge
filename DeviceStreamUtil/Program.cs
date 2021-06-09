using DeviceStreamCommon;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Samples;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace DeviceStreamUtil
{
    class Program
    {
        // The IoT Hub connection string. This is available under the "Shared access policies" in the Azure portal.
        private static IConfiguration configuration;
        private static ILogger logger;


        private static string ioTHubconnectionString = null; //IOTHUB_CONNECTIONSTRING
        private static string deviceId = null;  //DEVICE_ID
        private static string moduleName = null;  //MODULE_NAME
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

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("DeviceStreamUtil.Program", LogLevel.Debug)
                    .AddConsole();
            });

            logger = loggerFactory.CreateLogger<Program>();

            logger.LogInformation("DeviceStreamUtil!");


            //checking configuration requirements
            ioTHubconnectionString = configuration.GetValue<string>("IOTHUB_CONNECTIONSTRING");

            if (string.IsNullOrEmpty(ioTHubconnectionString))
            {
                logger.LogError("This tool requires a <IOTHUB_CONNECTIONSTRING> parameter to connect to IoT Hub to initiate the Device Stream connection.");
                return;
            }

            deviceId = configuration.GetValue<string>("DEVICE_ID");

            if (string.IsNullOrEmpty(deviceId))
            {
                logger.LogError("This tool requires a <DEVICE_ID> parameter as target to connect with IoT Hub Device Stream.");
                return;
            }


            moduleName = configuration.GetValue<string>("MODULE_NAME");

            if (string.IsNullOrEmpty(moduleName))
            {
                logger.LogError("This tool requires a <MODULE_NAME> parameter as target to connect with IoT Hub Device Stream.");
                return;
            }            

            localPort = configuration.GetValue<int>("LOCAL_PORT",0);

            if (localPort==0)
            {
                logger.LogError("This tool requires a <LOCAL_PORT> parameter as local port to which listen for incoming connection requests for Device Stream.");
                return;
            }

            streamName = configuration.GetValue<string>("STREAM_NAME");

            if (string.IsNullOrEmpty(streamName))
            {
                logger.LogError("This tool requires a <STREAM_NAME> parameter as name of the IoT Hub Device Stream session name.");
                return;
            }

            remotePort = configuration.GetValue<int>("REMOTE_PORT",0);

            if (remotePort == 0)
            {
                logger.LogError("This tool requires a <REMOTE_PORT> parameter as remote port to terminate the incoming connection requests into.");
                return;
            }

            remoteHost = configuration.GetValue<string>("REMOTE_HOST");

            if (string.IsNullOrEmpty(remoteHost))
            {
                logger.LogError("This tool requires a <REMOTE_HOST> parameter as target to connect with IoT Hub Device Stream.");
                return;
            }


            //initiate a client to IoT Hub 
            using (ServiceClient serviceClient = ServiceClient.CreateFromConnectionString(ioTHubconnectionString, transportType))
            {
                var methodRequest = new CloudToDeviceMethod(DeviceStreamDirectMethods.InitiateDeviceStream);

                InitiateDeviceStreamRequest requestPayload = new InitiateDeviceStreamRequest() { TargetHost = remoteHost, TargetPort = remotePort };

                methodRequest.SetPayloadJson(requestPayload.ToJson());

                //perform a Direct Method to the remote module to initiate the device stream!
                var response = await serviceClient.InvokeDeviceMethodAsync(deviceId, moduleName, methodRequest);

                if (response.Status == 200)
                {
                    InitiateDeviceStreamResponse responseBody = InitiateDeviceStreamResponse.FromJson(response.GetPayloadAsJson());

                    if (responseBody.RequestAccepted)
                    {
                        logger.LogInformation($"Device Stream request accepted: {responseBody.Reason}");

                        var sample = new DeviceStreamClientHandler(serviceClient, deviceId, localPort, streamName);
                        sample.StartDeviceStreamSession().GetAwaiter().GetResult();
                    }
                    else
                    {
                        logger.LogError($"Error while initiating the Device Stream - {responseBody.Reason}");
                    }
                }
                else
                {
                    logger.LogError("Error while calling remote method to initiate the Device Stream");
                }
            }

            logger.LogInformation("Done.");
        }
    }
}
