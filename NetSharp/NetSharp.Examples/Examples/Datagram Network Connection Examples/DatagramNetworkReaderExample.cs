﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

using NetSharp.Raw.Datagram;

namespace NetSharp.Examples.Examples.Datagram_Network_Connection_Examples
{
    internal class DatagramNetworkReaderExample : INetSharpExample
    {
        private const int PacketSize = 8192, ExpectedClientCount = 8;

        /// <inheritdoc />
        public string Name { get; } = "Datagram Network Reader Example";

        private static bool RequestHandler(EndPoint remoteEndPoint, in ReadOnlyMemory<byte> requestBuffer, int receivedRequestBytes,
            in Memory<byte> responseBuffer)
        {
            lock (typeof(Console))
            {
                string request = Program.Constants.ServerEncoding.GetString(requestBuffer.Span).Trim('\0');

                Console.WriteLine($"[Server] Received request \'{request}\' ({receivedRequestBytes} bytes) from {remoteEndPoint}");
                Console.WriteLine($"[Server] Sending response \'{request}\' ({receivedRequestBytes} bytes) to {remoteEndPoint}");
            }

            return requestBuffer.TryCopyTo(responseBuffer);
        }

        /// <inheritdoc />
        public Task RunAsync()
        {
            EndPoint defaultEndPoint = new IPEndPoint(IPAddress.Any, 0);

            Socket rawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            rawSocket.Bind(Program.Constants.ServerEndPoint);

            using RawDatagramNetworkReader reader = new RawDatagramNetworkReader(ref rawSocket, RequestHandler, defaultEndPoint, PacketSize, 100);
            reader.Start(ExpectedClientCount);

            Console.WriteLine($"Started datagram server at {Program.Constants.ServerEndPoint}! Enter any key to stop the server...");
            Console.ReadLine();

            reader.Shutdown();

            rawSocket.Close();
            rawSocket.Dispose();

            return Task.CompletedTask;
        }
    }
}