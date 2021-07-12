// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using DeviceStreamCommon;
using Microsoft.Azure.Devices.Client;
using System;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DeviceStreamCommon
{
    public class DeviceStreamDeviceHelper
    {
        //static instance of this handler class
        private static DeviceStreamDeviceHelper deviceStreamDeviceHandler = null;
        
        private DeviceStreamClientWrapper _streamClient;
                
        public String TargetHost { get; set; }
        public int TargetPort { get; set; }

        /// <summary>
        /// an active session is out there?
        /// </summary>
        public bool ActiveSession { get; set; }


        /// <summary>
        /// ctor 
        /// </summary>
        /// <param name="moduleClient"></param>
        /// <param name="host"></param>
        /// <param name="port"></param>
        public DeviceStreamDeviceHelper(DeviceStreamClientWrapper streamClient)
        {
            _streamClient = streamClient;
           
            ActiveSession = false;
        }
        


        private static async Task HandleIncomingDataAsync(NetworkStream localStream, ClientWebSocket remoteStream, CancellationToken cancellationToken)
        {
            byte[] bufferObj = new byte[10240];
            ArraySegment<byte> buffer = new ArraySegment<byte>(bufferObj, 0,bufferObj.Length);

            while (remoteStream.State == WebSocketState.Open)
            {
                var receiveResult = await remoteStream.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

                await localStream.WriteAsync(buffer.Array, 0, receiveResult.Count).ConfigureAwait(false);
            }

            Console.WriteLine("Exit HandleIncomingDataAsync");
        }

        private static async Task HandleOutgoingDataAsync(NetworkStream localStream, ClientWebSocket remoteStream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[10240];

            while (localStream.CanRead)
            {
                int receiveCount = await localStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    
                await remoteStream.SendAsync(new ArraySegment<byte>(buffer, 0, receiveCount), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
            }

            Console.WriteLine("Exit HandleOutgoingDataAsync");
        }



        //static instance of this handler class
        private static DeviceStreamDeviceHelper deviceStreamModuleHandler = null;

        /// <summary>
        /// The name of the Direct Method to initiate to listen for a Device Stream request from the device side.
        /// </summary>
        public const string InitiateDeviceStream = "InitiateDeviceStream";

        /// <summary>
        /// Direct Method delegate handler....
        /// </summary>
        /// <param name="methodRequest"></param>
        /// <param name="parameter"></param>
        /// <returns></returns>
        public static async Task<MethodResponse> InitiateDeviceStreamMethodHandler(MethodRequest methodRequest, object parameter)
        {
            Console.WriteLine("InitiateDeviceStreamMethod was invoked remotely to enable DeviceStream!");

            //i'm expecting a Device Client and the Cancelation Token Source as parameter...
            Tuple<DeviceStreamClientWrapper, CancellationTokenSource> parameters = (Tuple<DeviceStreamClientWrapper, CancellationTokenSource>)parameter;

            Console.WriteLine("DirectMethodParameters deboxed.");

            DeviceStreamClientWrapper deviceStreamClientWrapper = parameters.Item1;
            CancellationTokenSource cancellationTokenSource = parameters.Item2;

            var directMethodRequestJson = methodRequest.DataAsJson;

            Console.WriteLine($"Deserializing DirectMethod request: {directMethodRequestJson}");

            InitiateDeviceStreamRequest initiateDeviceStreamRequest = InitiateDeviceStreamRequest.FromJson(directMethodRequestJson);
            InitiateDeviceStreamResponse initiateDeviceStreamResponse = new InitiateDeviceStreamResponse();

            Console.WriteLine("Deserialization done.");

            bool initiate = false;

            if (!string.IsNullOrEmpty(initiateDeviceStreamRequest.UseDeviceConnectionString))
            {
                //ok the direct method we got also a connection string for an alternative device identity to use to listen for device stream request
                //this could be used as alternative ways on IoT Edge Modules

                Console.WriteLine("Using an alternative DeviceClient to establish the Device Stream Connection");

                //in this case use an alternative Device Client 
                DeviceClient deviceClient = DeviceClient.CreateFromConnectionString(initiateDeviceStreamRequest.UseDeviceConnectionString);

                //and update the "wrapper" client.
                deviceStreamClientWrapper = new DeviceStreamClientWrapper(deviceClient);
            }

            if (deviceStreamModuleHandler == null)
            {
                deviceStreamModuleHandler = new DeviceStreamDeviceHelper(deviceStreamClientWrapper);

                //set the target host and port
                deviceStreamModuleHandler.TargetHost = initiateDeviceStreamRequest.TargetHost;
                deviceStreamModuleHandler.TargetPort = initiateDeviceStreamRequest.TargetPort;

                initiateDeviceStreamResponse.RequestAccepted = true;
                initiateDeviceStreamResponse.Reason = "This is a brand new session!";

                initiate = true;

                Console.WriteLine(initiateDeviceStreamResponse.Reason);
            }
            else
            {
                if (deviceStreamModuleHandler.ActiveSession)
                {
                    //a session is already there...    cannot initiate a new one / don't want to terminate an existing session
                    initiateDeviceStreamResponse.RequestAccepted = false;
                    initiateDeviceStreamResponse.Reason = "A session is already open.";

                    Console.WriteLine(initiateDeviceStreamResponse.Reason);
                }
                else
                {
                    Console.WriteLine("Probably recovering from a bad status, just reconnecting!");

                    initiateDeviceStreamResponse.RequestAccepted = true;
                    initiateDeviceStreamResponse.Reason = "A session was alrady open but not active, reinitiating.";

                    deviceStreamModuleHandler.TargetHost = initiateDeviceStreamRequest.TargetHost;
                    deviceStreamModuleHandler.TargetPort = initiateDeviceStreamRequest.TargetPort;

                    Console.WriteLine(initiateDeviceStreamResponse.Reason);

                    initiate = true;
                }
            }

            if (initiate)
            {
                Console.WriteLine("Starting Device Stream");

                //start a background task opening the Device Stream session and waiting for connection from the service side                
                await Task.Factory.StartNew(() => deviceStreamModuleHandler.StartDeviceStreamSession(cancellationTokenSource));
            }

            Console.WriteLine($"Returning the Direct Method response: {initiateDeviceStreamResponse.RequestAccepted} - {initiateDeviceStreamResponse.Reason}");

            return new MethodResponse(initiateDeviceStreamResponse.GetJsonByte(), 200);
        }



        /// <summary>
        /// Start the Device Stream Session   ---> this is async....  and go as simple as that....  
        /// </summary>
        /// <param name="acceptDeviceStreamingRequest"></param>
        /// <param name="cancellationTokenSource"></param>
        /// <returns></returns>
        public async void StartDeviceStreamSession(CancellationTokenSource cancellationTokenSource)
        {
            Console.WriteLine("Entering StartDeviceStreamSession");

            try
            {   
                //wait for a device stream request from the service side....
                DeviceStreamRequest streamRequest = await _streamClient.WaitForDeviceStreamRequestAsync(cancellationTokenSource.Token).ConfigureAwait(false);

                Console.WriteLine("Stream Request received from IoT Hub");

                if (streamRequest != null)
                {
                    Console.WriteLine("Now accepting Stream Request received from IoT Hub");

                    await _streamClient.AcceptDeviceStreamRequestAsync(streamRequest, cancellationTokenSource.Token).ConfigureAwait(false);

                    Console.WriteLine("Now Opening WebSocket stream to IoT Hub");

                    //get the websocket to the cloud service connection open & authenticated using the token 
                    using (ClientWebSocket webSocket = await DeviceStreamCommon.StreamingClientHelper.GetStreamingClientAsync(streamRequest.Uri, streamRequest.AuthorizationToken, cancellationTokenSource.Token).ConfigureAwait(false))
                    {
                        Console.WriteLine($"Now Opening local stream to target {TargetHost}:{TargetPort}");

                        //now open the connection to the target host
                        using (TcpClient tcpClient = new TcpClient())
                        {
                            //open a TCP connection to a local endpoint
                            await tcpClient.ConnectAsync(TargetHost, TargetPort).ConfigureAwait(false);

                            //get the streams local and remote
                            using (NetworkStream localStream = tcpClient.GetStream())
                            {
                                Console.WriteLine("Starting Proxying streams");

                                ActiveSession = true;

                                //start the bidirectional communication
                                await Task.WhenAny(
                                    HandleIncomingDataAsync(localStream, webSocket, cancellationTokenSource.Token),
                                    HandleOutgoingDataAsync(localStream, webSocket, cancellationTokenSource.Token)).ConfigureAwait(false);

                                //when one of the two connection has terminated....  done.....                                                                
                                localStream.Close();

                                Console.WriteLine("Session closed.");                                
                            }
                        }

                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, String.Empty, cancellationTokenSource.Token).ConfigureAwait(false);

                        ActiveSession = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Something went wrong with the streaming session.\n{ex.ToString()}");
                ActiveSession = false;
            }

            Console.WriteLine("Waiting for another DirectMethod call to enable Device Stream");
        }
    }
}
