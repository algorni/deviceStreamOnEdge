// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
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
    public class DeviceStreamModuleHandler
    {
        //static instance of this handler class
        private static DeviceStreamModuleHandler deviceStreamModuleHandler = null;

        private ModuleClient _moduleClient;

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
        public DeviceStreamModuleHandler(ModuleClient moduleClient)
        {
            _moduleClient = moduleClient;

            ActiveSession = false;
        }


        public static async Task<MethodResponse> InitiateDeviceStreamMethodHandler(MethodRequest methodRequest, object parameter)
        {
            Console.WriteLine("InitiateDeviceStreamMethod was invoked remoetly to enable DeviceStream!");

            //i'm expecting a Device Client and the Cancelation Token Source as parameter...
            Tuple<ModuleClient, CancellationTokenSource> parameters = (Tuple<ModuleClient, CancellationTokenSource>)parameter;

            ModuleClient moduleClient = parameters.Item1;
            CancellationTokenSource cancellationTokenSource = parameters.Item2;

            InitiateDeviceStreamRequest initiateDeviceStreamRequest = InitiateDeviceStreamRequest.FromJson(methodRequest.DataAsJson);
            InitiateDeviceStreamResponse initiateDeviceStreamResponse = new InitiateDeviceStreamResponse();

            bool initiate = false;

            if (deviceStreamModuleHandler == null)
            {
                deviceStreamModuleHandler = new DeviceStreamModuleHandler(moduleClient);
                deviceStreamModuleHandler.TargetHost = initiateDeviceStreamRequest.TargetHost;
                deviceStreamModuleHandler.TargetPort = initiateDeviceStreamRequest.TargetPort;

                initiateDeviceStreamResponse.RequestAccepted = true;
                initiateDeviceStreamResponse.Reason = "This is a brand new session!";

                initiate = true;
            }
            else
            {
                if (deviceStreamModuleHandler.ActiveSession)
                {
                    //a session is already there...    cannot initiate a new one 
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
                Task.Factory.StartNew(() => deviceStreamModuleHandler.StartDeviceStreamSession(cancellationTokenSource));
            }

            Console.WriteLine($"Returning the Direct Method response: {initiateDeviceStreamResponse.RequestAccepted} - {initiateDeviceStreamResponse.Reason}");

            return new MethodResponse(initiateDeviceStreamResponse.GetJsonByte(), 200);
        }


        private static async Task HandleIncomingDataAsync(NetworkStream localStream, ClientWebSocket remoteStream, CancellationToken cancellationToken)
        {
            byte[] bufferObj = new byte[10240];
            ArraySegment<byte> buffer = new ArraySegment<byte>(bufferObj, 0, bufferObj.Length);

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




        /// <summary>
        /// Start the Device Stream Session   ---> this is async....  and go as simple as that....  
        /// </summary>
        /// <param name="acceptDeviceStreamingRequest"></param>
        /// <param name="cancellationTokenSource"></param>
        /// <returns></returns>
        public async void StartDeviceStreamSession(CancellationTokenSource cancellationTokenSource)
        {
            try
            {
                //wait for a device stream request from the service side....
                DeviceStreamRequest streamRequest = await _moduleClient.WaitForDeviceStreamRequestAsync(cancellationTokenSource.Token).ConfigureAwait(false);

                if (streamRequest != null)
                {
                    await _moduleClient.AcceptDeviceStreamRequestAsync(streamRequest, cancellationTokenSource.Token).ConfigureAwait(false);

                    //get the websocket connection open & authenticated using the token 
                    using (ClientWebSocket webSocket = await DeviceStreamCommon.StreamingClientHelper.GetStreamingClientAsync(streamRequest.Uri, streamRequest.AuthorizationToken, cancellationTokenSource.Token).ConfigureAwait(false))
                    {
                        using (TcpClient tcpClient = new TcpClient())
                        {
                            //open a TCP connection to a local endpoint
                            await tcpClient.ConnectAsync(TargetHost, TargetPort).ConfigureAwait(false);

                            //get the streams local and remote
                            using (NetworkStream localStream = tcpClient.GetStream())
                            {
                                Console.WriteLine("Starting Proxying streams");

                                ActiveSession = true;

                                await Task.WhenAny(
                                    HandleIncomingDataAsync(localStream, webSocket, cancellationTokenSource.Token),
                                    HandleOutgoingDataAsync(localStream, webSocket, cancellationTokenSource.Token)).ConfigureAwait(false);

                                localStream.Close();

                                Console.WriteLine("Session closed.");

                                ActiveSession = false;
                            }
                        }

                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, String.Empty, cancellationTokenSource.Token).ConfigureAwait(false);
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
