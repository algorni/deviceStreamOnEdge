using DeviceStreamCommon;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Samples;
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
        static private DeviceStreamModuleHandler deviceStreamModuleHandler = null;

        static private CancellationTokenSource deviceStreamCancelationTokenSource = new CancellationTokenSource();

        static void Main(string[] args)
        {
            //Initialize Application
            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
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
            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await ioTHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub Device Stream Proxy Module client initialized.");


            await ioTHubModuleClient.SetMethodHandlerAsync(DeviceStreamDirectMethods.InitiateDeviceStream, InitiateDeviceStreamMethodHandler, ioTHubModuleClient);
        }


        private static async Task<MethodResponse> InitiateDeviceStreamMethodHandler(MethodRequest methodRequest, object parameter)
        {
            return await Task<MethodResponse>.Run(() =>
            {
                ModuleClient ioTHubModuleClient = (ModuleClient)parameter;

                InitiateDeviceStreamRequest initiateDeviceStreamRequest = InitiateDeviceStreamRequest.FromJson(methodRequest.DataAsJson);
                InitiateDeviceStreamResponse initiateDeviceStreamResponse = new InitiateDeviceStreamResponse();

                bool initiate = false;

                if (deviceStreamModuleHandler == null)
                {
                    deviceStreamModuleHandler = new DeviceStreamModuleHandler(ioTHubModuleClient, initiateDeviceStreamRequest.TargetHost, initiateDeviceStreamRequest.TargetPort);

                    initiate = true;
                }
                else
                {
                    if (deviceStreamModuleHandler.ActiveSession)
                    {
                        //a session is already there...    cannot initiate a new one 
                        initiateDeviceStreamResponse.RequestAccepted = false;
                        initiateDeviceStreamResponse.Reason = "A session is already open.";
                    }
                    else
                    {
                        Console.WriteLine("Probably recovering from a bad status, just reconnecting!");

                        initiateDeviceStreamResponse.RequestAccepted = false;
                        initiateDeviceStreamResponse.Reason = "A session was alrady open but not active, reinitiating.";

                        initiate = true;
                    }
                }

                if (initiate)
                {
                    //ok initiate the session -> this is going ASYNC!!! no return....  
                    deviceStreamModuleHandler.StartDeviceStreamSession(deviceStreamCancelationTokenSource);
                }

                return new MethodResponse(initiateDeviceStreamResponse.GetJsonByte(), 200);
            });           
        }
    }
}
