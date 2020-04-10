﻿using Microsoft.Extensions.ObjectPool;

using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetSharp.Deprecated
{
    /// <summary>
    /// Helper class providing awaitable wrappers around asynchronous Send and SendTo operations.
    /// </summary>
    public sealed class SocketWriter
    {
        private readonly int PacketBufferLength;
        private readonly ObjectPool<SocketAsyncEventArgs> sendToAsyncEventArgsPool;
        private readonly ArrayPool<byte> sendToBufferPool;

        private void HandleIOCompleted(object? sender, SocketAsyncEventArgs args)
        {
            switch (args.LastOperation)
            {
                case SocketAsyncOperation.SendTo:
                    AsyncWriteToken asyncSendToToken = (AsyncWriteToken)args.UserToken;

                    if (asyncSendToToken.CancellationToken.IsCancellationRequested)
                    {
                        asyncSendToToken.CompletionSource.SetCanceled();
                    }
                    else
                    {
                        if (args.SocketError != SocketError.Success)
                        {
                            asyncSendToToken.CompletionSource.SetException(
                                new SocketException((int)args.SocketError));
                        }
                        else
                        {
                            asyncSendToToken.CompletionSource.SetResult(args.BytesTransferred);
                        }
                    }

                    sendToBufferPool.Return(asyncSendToToken.RentedBuffer, true);
                    sendToAsyncEventArgsPool.Return(args);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"The {nameof(SocketWriter)} class doesn't support the {args.LastOperation} operation.");
            }
        }

        private readonly struct AsyncWriteToken
        {
            public readonly CancellationToken CancellationToken;
            public readonly TaskCompletionSource<int> CompletionSource;
            public readonly byte[] RentedBuffer;

            public AsyncWriteToken(byte[] rentedBuffer, TaskCompletionSource<int> tcs,
                CancellationToken cancellationToken = default)
            {
                RentedBuffer = rentedBuffer;

                CompletionSource = tcs;
                CancellationToken = cancellationToken;
            }
        }

        internal SocketWriter(int packetBufferLength = NetworkPacket.PacketSize, int maxPooledObjects = 10,
            bool preallocateBuffers = false)
        {
            PacketBufferLength = packetBufferLength;

            sendToBufferPool = ArrayPool<byte>.Create(packetBufferLength, maxPooledObjects);

            sendToAsyncEventArgsPool =
                new DefaultObjectPool<SocketAsyncEventArgs>(new DefaultPooledObjectPolicy<SocketAsyncEventArgs>(),
                    maxPooledObjects);

            for (int i = 0; i < maxPooledObjects; i++)
            {
                SocketAsyncEventArgs sendToArgs = new SocketAsyncEventArgs();
                sendToArgs.Completed += HandleIOCompleted;
                sendToAsyncEventArgsPool.Return(sendToArgs);
            }
        }

        /// <summary>
        /// Provides an awaitable wrapper around an asynchronous socket send operation.
        /// </summary>
        /// <param name="socket">The socket which should send the data to the remote endpoint.</param>
        /// <param name="remoteEndPoint">The remote endpoint to which data should be written.</param>
        /// <param name="socketFlags">The socket flags associated with the send operation.</param>
        /// <param name="outputBuffer">The data buffer which should be sent.</param>
        /// <param name="cancellationToken">The cancellation token to observe for the operation.</param>
        /// <returns>The number of bytes of data which were written to the remote endpoint.</returns>
        public ValueTask<int> SendToAsync(Socket socket, EndPoint remoteEndPoint, SocketFlags socketFlags,
            Memory<byte> outputBuffer, CancellationToken cancellationToken = default)
        {
            TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();

            byte[] rentedSendToBuffer = sendToBufferPool.Rent(PacketBufferLength);
            Memory<byte> rentedSendToBufferMemory = new Memory<byte>(rentedSendToBuffer);

            outputBuffer.CopyTo(rentedSendToBufferMemory);

            SocketAsyncEventArgs args = sendToAsyncEventArgsPool.Get();
            args.SetBuffer(rentedSendToBufferMemory);
            args.SocketFlags = socketFlags;
            args.RemoteEndPoint = remoteEndPoint;
            args.UserToken = new AsyncWriteToken(rentedSendToBuffer, tcs, cancellationToken);

            /*
            // register cleanup action for when the cancellation token is thrown
            cancellationToken.Register(() =>
            {
                tcs.SetCanceled();

                sendBufferPool.Return(rentedSendToBuffer, true);

                //TODO this is probably a hideous solution. find a better one
                args.Completed -= HandleIOCompleted;
                args.Dispose();

                SocketAsyncEventArgs newArgs = new SocketAsyncEventArgs();
                newArgs.Completed += HandleIOCompleted;
                sendAsyncEventArgsPool.Return(newArgs);
            });
            */

            // if the send operation doesn't complete synchronously, return the awaitable task
            if (socket.SendToAsync(args)) return new ValueTask<int>(tcs.Task);

            int result = args.BytesTransferred;

            sendToBufferPool.Return(rentedSendToBuffer, true);
            sendToAsyncEventArgsPool.Return(args);

            return new ValueTask<int>(result);
        }
    }
}