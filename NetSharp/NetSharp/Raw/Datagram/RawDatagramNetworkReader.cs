﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace NetSharp.Raw.Datagram
{
    public delegate bool RawDatagramRequestHandler(EndPoint remoteEndPoint, in ReadOnlyMemory<byte> requestBuffer, int receivedRequestBytes,
        in Memory<byte> responseBuffer);

    public sealed class RawDatagramNetworkReader : RawNetworkReaderBase
    {
        private const int MaxDatagramSize = ushort.MaxValue - 28;

        private readonly int messageSize;

        private readonly RawDatagramRequestHandler requestHandler;

        /// <inheritdoc />
        public RawDatagramNetworkReader(ref Socket rawConnection, RawDatagramRequestHandler? requestHandler, EndPoint defaultEndPoint, int messageSize,
            int pooledBuffersPerBucket = 50, uint preallocatedStateObjects = 0) : base(ref rawConnection, defaultEndPoint, messageSize,
            pooledBuffersPerBucket, preallocatedStateObjects)
        {
            if (messageSize <= 0 || MaxDatagramSize < messageSize)
            {
                throw new ArgumentOutOfRangeException(nameof(messageSize), messageSize,
                    $"The datagram size must be greater than 0 and less than {MaxDatagramSize}");
            }

            this.messageSize = messageSize;

            this.requestHandler = requestHandler ?? DefaultRequestHandler;
        }

        private static bool DefaultRequestHandler(EndPoint remoteEndPoint, in ReadOnlyMemory<byte> requestBuffer, int receivedRequestBytes,
            in Memory<byte> responseBuffer)
        {
            return requestBuffer.TryCopyTo(responseBuffer);
        }

        private void CompleteReceiveFrom(SocketAsyncEventArgs args)
        {
            byte[] receiveBuffer = args.Buffer;

            switch (args.SocketError)
            {
                case SocketError.Success:
                    byte[] responseBuffer = BufferPool.Rent(messageSize);

                    bool responseExists = requestHandler(args.RemoteEndPoint, receiveBuffer, args.BytesTransferred, responseBuffer);
                    BufferPool.Return(receiveBuffer, true);

                    if (responseExists)
                    {
                        args.SetBuffer(responseBuffer, 0, messageSize);

                        SendTo(args);
                        return;
                    }

                    BufferPool.Return(responseBuffer, true);
                    break;

                default:
                    BufferPool.Return(receiveBuffer, true);
                    ArgsPool.Return(args);
                    break;
            }
        }

        private void CompleteSendTo(SocketAsyncEventArgs args)
        {
            byte[] sendBuffer = args.Buffer;
            BufferPool.Return(sendBuffer, true);

            ArgsPool.Return(args);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ConfigureReceiveFrom(SocketAsyncEventArgs args)
        {
            byte[] receiveBuffer = BufferPool.Rent(messageSize);
            args.SetBuffer(receiveBuffer, 0, messageSize);
        }

        private void HandleIoCompleted(object sender, SocketAsyncEventArgs args)
        {
            switch (args.LastOperation)
            {
                case SocketAsyncOperation.ReceiveFrom:
                    StartDefaultReceiveFrom();

                    CompleteReceiveFrom(args);
                    break;

                case SocketAsyncOperation.SendTo:
                    CompleteSendTo(args);
                    break;
            }
        }

        private void ReceiveFrom(SocketAsyncEventArgs args)
        {
            if (ShutdownToken.IsCancellationRequested)
            {
                ArgsPool.Return(args);
                return;
            }

            if (Connection.ReceiveFromAsync(args)) return;

            StartDefaultReceiveFrom();
            CompleteReceiveFrom(args);
        }

        private void SendTo(SocketAsyncEventArgs args)
        {
            if (ShutdownToken.IsCancellationRequested)
            {
                byte[] sendBuffer = args.Buffer;
                BufferPool.Return(sendBuffer, true);

                ArgsPool.Return(args);

                return;
            }

            if (Connection.SendToAsync(args)) return;

            CompleteSendTo(args);
        }

        private void StartDefaultReceiveFrom()
        {
            if (ShutdownToken.IsCancellationRequested)
            {
                return;
            }

            SocketAsyncEventArgs args = ArgsPool.Rent();

            ConfigureReceiveFrom(args);

            ReceiveFrom(args);
        }

        /// <inheritdoc />
        protected override bool CanReuseStateObject(ref SocketAsyncEventArgs instance)
        {
            return true;
        }

        /// <inheritdoc />
        protected override SocketAsyncEventArgs CreateStateObject()
        {
            SocketAsyncEventArgs instance = new SocketAsyncEventArgs { RemoteEndPoint = DefaultEndPoint };
            instance.Completed += HandleIoCompleted;

            return instance;
        }

        /// <inheritdoc />
        protected override void DestroyStateObject(SocketAsyncEventArgs instance)
        {
            instance.Completed -= HandleIoCompleted;
            instance.Dispose();
        }

        /// <inheritdoc />
        protected override void ResetStateObject(ref SocketAsyncEventArgs instance)
        {
            instance.RemoteEndPoint = DefaultEndPoint;
        }

        /// <inheritdoc />
        public override void Start(ushort concurrentReadTasks)
        {
            for (ushort i = 0; i < concurrentReadTasks; i++)
            {
                StartDefaultReceiveFrom();
            }
        }
    }
}