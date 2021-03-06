// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


using Microsoft.Azure.Devices;
using System;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DeviceStreamCommon
{
    public class DeviceStreamClientHelper
    {
        private CancellationTokenSource _cancellationTokenSource;
        private ServiceClient _serviceClient;
        private string _deviceId;
        private string _moduleId;
        private int _localPort;
        private string _streamName;



        /// <summary>
        /// ctor for device stream
        /// </summary>
        /// <param name="serviceClient"></param>
        /// <param name="deviceId"></param>
        /// <param name="localPort"></param>
        /// <param name="streamName"></param>
        public DeviceStreamClientHelper(ServiceClient serviceClient, string deviceId, int localPort, string streamName, CancellationTokenSource cancellationTokenSource)
        {
            _serviceClient = serviceClient;
            _deviceId = deviceId;
            _localPort = localPort;
            _streamName = streamName;
            _cancellationTokenSource = cancellationTokenSource;
        }


        /// <summary>
        /// ctor for module stream
        /// </summary>
        /// <param name="serviceClient"></param>
        /// <param name="deviceId"></param>
        /// <param name="moduleId"></param>
        /// <param name="localPort"></param>
        /// <param name="streamName"></param>
        /// <param name="cancellationTokenSource"></param>
        public DeviceStreamClientHelper(ServiceClient serviceClient, string deviceId, string moduleId, int localPort, string streamName, CancellationTokenSource cancellationTokenSource)
        {
            _serviceClient = serviceClient;
            _deviceId = deviceId;
            _localPort = localPort;
            _streamName = streamName;
            _cancellationTokenSource = cancellationTokenSource;
        }



        private static async Task HandleIncomingDataAsync(NetworkStream localStream, ClientWebSocket remoteStream, CancellationToken cancellationToken)
        {         
            byte[] bufferObj = new byte[10240];
            ArraySegment<byte> receiveBuffer = new ArraySegment<byte>(bufferObj, 0, bufferObj.Length);

            while (localStream.CanRead)
            {
                var receiveResult = await remoteStream.ReceiveAsync(receiveBuffer, cancellationToken).ConfigureAwait(false);

                await localStream.WriteAsync(receiveBuffer.Array, 0, receiveResult.Count).ConfigureAwait(false);
            }

            Console.WriteLine("Exit HandleIncomingDataAsync");
        }

        private static async Task HandleOutgoingDataAsync(NetworkStream localStream, ClientWebSocket remoteStream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[10240];

            while (remoteStream.State == WebSocketState.Open)
            {
                int receiveCount = await localStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);

                await remoteStream.SendAsync(new ArraySegment<byte>(buffer, 0, receiveCount), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
            }

            Console.WriteLine("Exit HandleOutgoingDataAsync");
        }


        private static async void HandleIncomingConnectionsAndStartStreamSession(string deviceId, string moduleId, ServiceClient serviceClient, TcpClient tcpClient, string streamName, CancellationTokenSource cancellationTokenSource)
        {
            var deviceStreamRequest = new Microsoft.Azure.Devices.DeviceStreamRequest(streamName);

            using (var localStream = tcpClient.GetStream())
            {
                DeviceStreamResponse deviceStreamResponse = null;

                if (string.IsNullOrEmpty(moduleId))
                {
                    deviceStreamResponse = await serviceClient.CreateStreamAsync(deviceId, deviceStreamRequest, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    deviceStreamResponse = await serviceClient.CreateStreamAsync(deviceId, moduleId, deviceStreamRequest, CancellationToken.None).ConfigureAwait(false);
                }                

                Console.WriteLine($"Stream response received: Name={deviceStreamRequest.StreamName} IsAccepted={deviceStreamResponse.IsAccepted}");

                if (deviceStreamResponse.IsAccepted)
                {
                    try
                    {
                        using (var remoteStream = await DeviceStreamCommon.StreamingClientHelper.GetStreamingClientAsync(deviceStreamResponse.Uri, deviceStreamResponse.AuthorizationToken, cancellationTokenSource.Token).ConfigureAwait(false))
                        {
                            Console.WriteLine("Starting streaming");

                            await Task.WhenAny(
                                HandleIncomingDataAsync(localStream, remoteStream, cancellationTokenSource.Token),
                                HandleOutgoingDataAsync(localStream, remoteStream, cancellationTokenSource.Token)).ConfigureAwait(false);
                        }

                        Console.WriteLine("Done with streaming, the session was terminated.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Got an exception: {0}", ex);
                    }                                        
                }
            }

            tcpClient.Close();

            cancellationTokenSource.Cancel();
        }

       

        /// <summary>
        /// Start Device Stream Session
        /// </summary>
        /// <param name="cancellationTokenSource"></param>
        /// <returns></returns>
        public async Task StartListeningLocalPort()
        {            
            Console.WriteLine($"Start listening on port {_localPort}");

            //start listening on the localhost at the specific port
            var tcpListener = new TcpListener(IPAddress.Loopback, _localPort);
            tcpListener.Start();

            var tcpClient = await tcpListener.AcceptTcpClientAsync().ConfigureAwait(false);

            Console.WriteLine($"Incoming connectionon port {_localPort} start handling...");

            await Task.Factory.StartNew(() => HandleIncomingConnectionsAndStartStreamSession(_deviceId, _moduleId, _serviceClient, tcpClient, _streamName, _cancellationTokenSource) );                  
        }
    }
}
