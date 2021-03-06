﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

using NetSharp.Properties;

namespace NetSharp.Raw.Datagram
{
    /// <summary>
    /// Implements a raw network writer using a datagram-based protocol.
    /// </summary>
    public sealed class RawDatagramNetworkWriter : RawNetworkWriterBase
    {
        private readonly int datagramSize;

        /// <inheritdoc />
        public RawDatagramNetworkWriter(ref Socket rawConnection, EndPoint defaultEndPoint, int datagramSize, int pooledBuffersPerBucket = 50,
            uint preallocatedStateObjects = 0) : base(ref rawConnection, defaultEndPoint, datagramSize, pooledBuffersPerBucket, preallocatedStateObjects)
        {
            if (datagramSize <= 0 || MaxDatagramSize < datagramSize)
            {
                throw new ArgumentOutOfRangeException(nameof(datagramSize), datagramSize, Resources.RawDatagramSizeError);
            }

            this.datagramSize = datagramSize;
        }

        private void CompleteReceiveFrom(SocketAsyncEventArgs args)
        {
            PacketReadToken token = (PacketReadToken) args.UserToken;

            byte[] receiveBuffer = args.Buffer;

            switch (args.SocketError)
            {
                case SocketError.Success:
                    receiveBuffer.CopyTo(token.UserBuffer);
                    token.CompletionSource.SetResult(args.BytesTransferred);
                    break;

                case SocketError.OperationAborted:
                    token.CompletionSource.SetCanceled();
                    break;

                default:
                    token.CompletionSource.SetException(new SocketException((int) args.SocketError));
                    break;
            }

            BufferPool.Return(receiveBuffer, true);
            ArgsPool.Return(args);
        }

        private void CompleteSendTo(SocketAsyncEventArgs args)
        {
            PacketWriteToken token = (PacketWriteToken) args.UserToken;

            byte[] sendBuffer = args.Buffer;

            switch (args.SocketError)
            {
                case SocketError.Success:
                    token.CompletionSource.SetResult(args.BytesTransferred);
                    break;

                case SocketError.OperationAborted:
                    token.CompletionSource.SetCanceled();
                    break;

                default:
                    token.CompletionSource.SetException(new SocketException((int) args.SocketError));
                    break;
            }

            BufferPool.Return(sendBuffer, true);
            ArgsPool.Return(args);
        }

        private void HandleIoCompleted(object sender, SocketAsyncEventArgs args)
        {
            switch (args.LastOperation)
            {
                case SocketAsyncOperation.SendTo:
                    CompleteSendTo(args);
                    break;

                case SocketAsyncOperation.ReceiveFrom:
                    CompleteReceiveFrom(args);
                    break;
            }
        }

        /// <inheritdoc />
        protected override bool CanReuseStateObject(ref SocketAsyncEventArgs instance)
        {
            return true;
        }

        /// <inheritdoc />
        protected override SocketAsyncEventArgs CreateStateObject()
        {
            SocketAsyncEventArgs instance = new SocketAsyncEventArgs();
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
        }

        /// <inheritdoc />
        public override int Read(ref EndPoint remoteEndPoint, Memory<byte> readBuffer, SocketFlags flags = SocketFlags.None)
        {
            int totalBytes = readBuffer.Length;
            if (totalBytes > datagramSize)
            {
                throw new ArgumentException(
                    string.Format(Resources.Culture, Resources.RawDatagramNetworkReaderRentedBufferSizeError, totalBytes, datagramSize),
                    nameof(readBuffer)
                );
            }

            byte[] transmissionBuffer = BufferPool.Rent(datagramSize);

            int readBytes = Connection.ReceiveFrom(transmissionBuffer, flags, ref remoteEndPoint);

            transmissionBuffer.CopyTo(readBuffer);
            BufferPool.Return(transmissionBuffer, true);

            return readBytes;
        }

        /// <inheritdoc />
        public override ValueTask<int> ReadAsync(EndPoint remoteEndPoint, Memory<byte> readBuffer, SocketFlags flags = SocketFlags.None)
        {
            int totalBytes = readBuffer.Length;
            if (totalBytes > datagramSize)
            {
                throw new ArgumentException(
                    string.Format(Resources.Culture, Resources.RawDatagramNetworkReaderRentedBufferSizeError, totalBytes, datagramSize),
                    nameof(readBuffer)
                );
            }

            TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();
            SocketAsyncEventArgs args = ArgsPool.Rent();

            byte[] transmissionBuffer = BufferPool.Rent(datagramSize);

            args.SetBuffer(transmissionBuffer, 0, datagramSize);

            args.RemoteEndPoint = remoteEndPoint;
            args.SocketFlags = flags;

            PacketReadToken token = new PacketReadToken(tcs, in readBuffer);
            args.UserToken = token;

            if (Connection.ReceiveFromAsync(args))
            {
                return new ValueTask<int>(tcs.Task);
            }

            // inlining CompleteReceiveFrom(SocketAsyncEventArgs) for performance
            int result = args.BytesTransferred;

            transmissionBuffer.CopyTo(readBuffer);

            CleanupTransmissionBufferAndState(args);  // transmissionBuffer was assigned to args.Buffer earlier, so this call is safe

            return new ValueTask<int>(result);
        }

        /// <inheritdoc />
        public override int Write(EndPoint remoteEndPoint, ReadOnlyMemory<byte> writeBuffer, SocketFlags flags = SocketFlags.None)
        {
            int totalBytes = writeBuffer.Length;
            if (totalBytes > datagramSize)
            {
                throw new ArgumentException(
                    string.Format(Resources.Culture, Resources.RawDatagramNetworkReaderRentedBufferSizeError, totalBytes, datagramSize),
                    nameof(writeBuffer)
                );
            }

            byte[] transmissionBuffer = BufferPool.Rent(datagramSize);
            writeBuffer.CopyTo(transmissionBuffer);

            int writtenBytes = Connection.SendTo(transmissionBuffer, flags, remoteEndPoint);

            BufferPool.Return(transmissionBuffer);

            return writtenBytes;
        }

        /// <inheritdoc />
        public override ValueTask<int> WriteAsync(EndPoint remoteEndPoint, ReadOnlyMemory<byte> writeBuffer, SocketFlags flags = SocketFlags.None)
        {
            int totalBytes = writeBuffer.Length;
            if (totalBytes > datagramSize)
            {
                throw new ArgumentException(
                    string.Format(Resources.Culture, Resources.RawDatagramNetworkReaderRentedBufferSizeError, totalBytes, datagramSize),
                    nameof(writeBuffer)
                );
            }

            TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();
            SocketAsyncEventArgs args = ArgsPool.Rent();

            byte[] transmissionBuffer = BufferPool.Rent(datagramSize);
            writeBuffer.CopyTo(transmissionBuffer);

            args.SetBuffer(transmissionBuffer, 0, datagramSize);

            args.RemoteEndPoint = remoteEndPoint;
            args.SocketFlags = flags;

            PacketWriteToken token = new PacketWriteToken(tcs);
            args.UserToken = token;

            if (Connection.SendToAsync(args))
            {
                return new ValueTask<int>(tcs.Task);
            }

            // inlining CompleteSendTo(SocketAsyncEventArgs) for performance
            int result = args.BytesTransferred;

            CleanupTransmissionBufferAndState(args);  // transmissionBuffer was assigned to args.Buffer earlier, so this call is safe

            return new ValueTask<int>(result);
        }

        private readonly struct PacketReadToken
        {
            public readonly TaskCompletionSource<int> CompletionSource;
            public readonly Memory<byte> UserBuffer;

            public PacketReadToken(TaskCompletionSource<int> completionSource, in Memory<byte> userBuffer)
            {
                CompletionSource = completionSource;

                UserBuffer = userBuffer;
            }
        }

        private readonly struct PacketWriteToken
        {
            public readonly TaskCompletionSource<int> CompletionSource;

            public PacketWriteToken(TaskCompletionSource<int> completionSource)
            {
                CompletionSource = completionSource;
            }
        }
    }
}