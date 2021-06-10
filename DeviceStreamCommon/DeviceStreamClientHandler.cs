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
    public class DeviceStreamClientHandler
    {
        private ServiceClient _serviceClient;
        private String _deviceId;
        private int _localPort;
        private string _streamName;

        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="deviceClient"></param>
        /// <param name="deviceId"></param>
        /// <param name="localPort"></param>
        /// <param name="streamName"></param>
        public DeviceStreamClientHandler(ServiceClient deviceClient, String deviceId, int localPort, string streamName)
        {
            _serviceClient = deviceClient;
            _deviceId = deviceId;
            _localPort = localPort;
            _streamName = streamName;
        }

        private static async Task HandleIncomingDataAsync(NetworkStream localStream, ClientWebSocket remoteStream, CancellationToken cancellationToken)
        {
            //byte[] receiveBuffer = new byte[10240];
            ArraySegment<byte> receiveBuffer = new ArraySegment<byte>();


            while (localStream.CanRead)
            {
                var receiveResult = await remoteStream.ReceiveAsync(receiveBuffer, cancellationToken).ConfigureAwait(false);

                await localStream.WriteAsync(receiveBuffer.Array, 0, receiveResult.Count).ConfigureAwait(false);
            }

            Console.WriteLine("Local Stream closed.");
        }

        private static async Task HandleOutgoingDataAsync(NetworkStream localStream, ClientWebSocket remoteStream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[10240];

            while (remoteStream.State == WebSocketState.Open)
            {
                int receiveCount = await localStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);

                await remoteStream.SendAsync(new ArraySegment<byte>(buffer, 0, receiveCount), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
            }

            Console.WriteLine("Remote Stream closed.");
        }


        private static async void HandleIncomingConnectionsAndCreateStreams(string deviceId, ServiceClient serviceClient, TcpClient tcpClient, string streamName)
        {
            var deviceStreamRequest = new Microsoft.Azure.Devices.DeviceStreamRequest(streamName);

            using (var localStream = tcpClient.GetStream())
            {
                var result = await serviceClient.CreateStreamAsync(deviceId, deviceStreamRequest, CancellationToken.None).ConfigureAwait(false);

                Console.WriteLine($"Stream response received: Name={deviceStreamRequest.StreamName} IsAccepted={result.IsAccepted}");

                if (result.IsAccepted)
                {
                    try
                    {
                        using (var cancellationTokenSource = new CancellationTokenSource())
                        using (var remoteStream = await DeviceStreamCommon.StreamingClientHelper.GetStreamingClientAsync(result.Uri, result.AuthorizationToken, cancellationTokenSource.Token).ConfigureAwait(false))
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
        }


        /// <summary>
        /// Start Device Stream Session
        /// </summary>
        /// <param name="cancellationTokenSource"></param>
        /// <returns></returns>
        public async Task StartDeviceStreamSession()
        {
            //eventually do a loop here???

            Console.WriteLine($"Start listening on port {_localPort}");

            //start listening on the localhost at the specific port
            var tcpListener = new TcpListener(IPAddress.Loopback, _localPort);
            tcpListener.Start();
           
            var tcpClient = await tcpListener.AcceptTcpClientAsync().ConfigureAwait(false);

            Console.WriteLine($"Incoming connectionon port {_localPort} start handling...");

            HandleIncomingConnectionsAndCreateStreams(_deviceId, _serviceClient, tcpClient, _streamName);           
        }
    }
}
