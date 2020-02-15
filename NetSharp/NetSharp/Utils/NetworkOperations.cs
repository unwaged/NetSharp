﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NetSharp.Packets;
using NetSharp.Utils.Conversion;

namespace NetSharp.Utils
{
    /// <summary>
    /// Helper class for asynchronously performing common network operations, for both the UDP and TCP protocols.
    /// </summary>
    public static class NetworkOperations
    {
        /// <summary>
        /// Reads the specified amount of data asynchronously from the network, via the given socket.
        /// The given <see cref="SocketFlags"/> are associated with the read, and the given <see cref="CancellationToken"/>
        /// is used to allow for asynchronous task cancellation.
        /// </summary>
        /// <param name="socket">The socket which should read data from the network.</param>
        /// <param name="count">The number of bytes to read from the network.</param>
        /// <param name="socketFlags">The socket flags associated with the receive operation.</param>
        /// <param name="cancellationToken">The cancellation token to use for asynchronous cancellation.</param>
        /// <returns>The result of the receive operation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Task<TransmissionResult> ReadAsync(Socket socket, int count, SocketFlags socketFlags,
            CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(() =>
            {
                byte[] byteBuffer = new byte[count];
                int receivedBytesCount = 0;

                while (count > receivedBytesCount)
                {
                    Span<byte> receivedBytes = new Span<byte>(byteBuffer, receivedBytesCount, count - receivedBytesCount);

                    receivedBytesCount += socket.Receive(receivedBytes, socketFlags);
                }

                return new TransmissionResult(byteBuffer, receivedBytesCount, socket.RemoteEndPoint);
            }, cancellationToken);
        }

        /// <summary>
        /// Reads a datagram asynchronously from the given remote endpoint, via the given socket.
        /// The given <see cref="SocketFlags"/> are associated with the read, and the given <see cref="CancellationToken"/>
        /// is used to allow for asynchronous task cancellation.
        /// </summary>
        /// <param name="socket">The socket which should read data from the network.</param>
        /// <param name="remoteEndPoint">The remote endpoint from which data should be read.</param>
        /// <param name="socketFlags">The socket flags associated with the receive operation.</param>
        /// <param name="cancellationToken">The cancellation token to use for asynchronous cancellation.</param>
        /// <returns>The result of the receive operation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Task<TransmissionResult> ReadFromAsync(Socket socket, EndPoint remoteEndPoint,
            SocketFlags socketFlags, CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(() =>
            {
                byte[] byteBuffer = new byte[Constants.UdpMaxBufferSize];

                EndPoint actualRemoteEndPoint = remoteEndPoint;

                int receivedByteCount = socket.ReceiveFrom(byteBuffer, socketFlags, ref actualRemoteEndPoint);

                return new TransmissionResult(new Memory<byte>(byteBuffer, 0, receivedByteCount), receivedByteCount,
                    actualRemoteEndPoint);
            }, cancellationToken);
        }

        /// <summary>
        /// Reads a packet asynchronously from network, via the given socket. The given <see cref="SocketFlags"/> are
        /// associated with the read, and the given <see cref="CancellationToken"/> is used to allow for asynchronous
        /// task cancellation.
        /// </summary>
        /// <param name="socket">The socket which should read the packet from the network.</param>
        /// <param name="socketFlags">The socket flags associated with the receive operation.</param>
        /// <param name="cancellationToken">The cancellation token to use for asynchronous cancellation.</param>
        /// <returns>The read packet.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static async Task<Packet> ReadPacketAsync(Socket socket, SocketFlags socketFlags, CancellationToken cancellationToken)
        {
            TransmissionResult packetHeaderResult = await ReadAsync(socket, Packet.HeaderSize, socketFlags, cancellationToken);

            int packetSize = EndianAwareBitConverter.ToInt32(packetHeaderResult.Buffer.Span.Slice(0, sizeof(int)));

            if (packetSize == 0)
            {
                return Packet.Deserialise(packetHeaderResult.Buffer);
            }

            TransmissionResult packetDataResult = await ReadAsync(socket, packetSize, socketFlags, cancellationToken);

            byte[] serialisedPacket = new byte[Packet.HeaderSize + packetSize];

            Memory<byte> serialisedPacketHeader = new Memory<byte>(serialisedPacket, 0, Packet.HeaderSize);
            packetHeaderResult.Buffer.CopyTo(serialisedPacketHeader);

            Memory<byte> serialisedPacketData = new Memory<byte>(serialisedPacket, Packet.HeaderSize, packetSize);
            packetDataResult.Buffer.CopyTo(serialisedPacketData);

            return Packet.Deserialise(serialisedPacket);
        }

        /// <summary>
        /// Reads a packet asynchronously from the given remote endpoint, via the given socket. The given
        /// <see cref="SocketFlags"/> are associated with the read, and the given <see cref="CancellationToken"/> is
        /// used to allow for asynchronous task cancellation.
        /// </summary>
        /// <param name="socket">The socket which should read the packet from the network.</param>
        /// <param name="remoteEndPoint">The remote endpoint from which a packet should be read.</param>
        /// <param name="socketFlags">The socket flags associated with the receive operation.</param>
        /// <param name="cancellationToken">The cancellation token to use for asynchronous cancellation.</param>
        /// <returns>The read packet and associated transmission results.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static async Task<(Packet packet, TransmissionResult packetResult)> ReadPacketFromAsync(
            Socket socket, EndPoint remoteEndPoint, SocketFlags socketFlags, CancellationToken cancellationToken)
        {
            TransmissionResult packetResult =
                await ReadFromAsync(socket, remoteEndPoint, socketFlags, cancellationToken);

            return (Packet.Deserialise(packetResult.Buffer), packetResult);
        }

        /// <summary>
        /// Writes the given buffer asynchronously to the network, via the given socket.
        /// The given <see cref="SocketFlags"/> are associated with the write, and the given <see cref="CancellationToken"/>
        /// is used to allow for asynchronous task cancellation.
        /// </summary>
        /// <param name="socket">The socket which should write data to the network.</param>
        /// <param name="buffer">The buffer that should be written to the network.</param>
        /// <param name="socketFlags">The socket flags associated with the send operation.</param>
        /// <param name="cancellationToken">The cancellation token to use for asynchronous cancellation.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Task WriteAsync(Socket socket, ReadOnlyMemory<byte> buffer, SocketFlags socketFlags,
            CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(() =>
            {
                int bytesToSend = buffer.Length;
                int sentBytesCount = 0;

                while (bytesToSend > sentBytesCount)
                {
                    ReadOnlySpan<byte> bufferSegment = buffer.Span.Slice(sentBytesCount, bytesToSend - sentBytesCount);

                    sentBytesCount += socket.Send(bufferSegment, socketFlags);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Writes the given packet asynchronously to the network, via the given socket. The given <see cref="SocketFlags"/>
        /// are associated with the write, and the given <see cref="CancellationToken"/> is used to allow for asynchronous
        /// task cancellation.
        /// </summary>
        /// <param name="socket">The socket which should write data to the network.</param>
        /// <param name="packet">The packet that should be written to the network.</param>
        /// <param name="socketFlags">The socket flags associated with the send operation.</param>
        /// <param name="cancellationToken">The cancellation token to use for asynchronous cancellation.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Task WritePacketAsync(Socket socket, Packet packet, SocketFlags socketFlags,
            CancellationToken cancellationToken)
            => WriteAsync(socket, Packet.Serialise(packet), socketFlags, cancellationToken);

        /// <summary>
        /// Writes the given packet asynchronously to the given remote endpoint, via the given socket.
        /// The given <see cref="SocketFlags"/> are associated with the write, and the given <see cref="CancellationToken"/>
        /// is used to allow for asynchronous task cancellation.
        /// </summary>
        /// <param name="socket">The socket which should write data to the network.</param>
        /// <param name="remoteEndPoint">The remote endpoint to which data should be written.</param>
        /// <param name="packet">The packet that should be written to the remote endpoint.</param>
        /// <param name="socketFlags">The socket flags associated with the send operation.</param>
        /// <param name="cancellationToken">The cancellation token to use for asynchronous cancellation.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Task WritePacketToAsync(Socket socket, EndPoint remoteEndPoint, Packet packet,
            SocketFlags socketFlags, CancellationToken cancellationToken)
            => WriteToAsync(socket, remoteEndPoint, Packet.Serialise(packet), socketFlags, cancellationToken);

        /// <summary>
        /// Writes the given buffer asynchronously to the given remote endpoint, via the given socket.
        /// The given <see cref="SocketFlags"/> are associated with the write, and the given <see cref="CancellationToken"/>
        /// is used to allow for asynchronous task cancellation.
        /// </summary>
        /// <param name="socket">The socket which should write data to the network.</param>
        /// <param name="remoteEndPoint">The remote endpoint to which data should be written.</param>
        /// <param name="buffer">The buffer that should be written to the network.</param>
        /// <param name="socketFlags">The socket flags associated with the send operation.</param>
        /// <param name="cancellationToken">The cancellation token to use for asynchronous cancellation.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Task WriteToAsync(Socket socket, EndPoint remoteEndPoint, ReadOnlyMemory<byte> buffer,
            SocketFlags socketFlags, CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(() =>
            {
                int bytesToSend = buffer.Length;
                int sentBytesCount = 0;

                while (bytesToSend > sentBytesCount)
                {
                    ReadOnlySpan<byte> bufferSegment = buffer.Span.Slice(sentBytesCount, bytesToSend - sentBytesCount);

                    sentBytesCount += socket.SendTo(bufferSegment.ToArray(), socketFlags, remoteEndPoint);
                }
            }, cancellationToken);
        }
    }
}