﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace NetSharp.Interfaces
{
    public interface INetworkWriter
    {
        public int Read(ref EndPoint remoteEndPoint, Memory<byte> readBuffer,
            SocketFlags flags = SocketFlags.None);

        public ValueTask<int> ReadAsync(EndPoint remoteEndPoint, Memory<byte> readBuffer,
            SocketFlags flags = SocketFlags.None);

        public int Write(EndPoint remoteEndPoint, ReadOnlyMemory<byte> writeBuffer,
            SocketFlags flags = SocketFlags.None);

        public ValueTask<int> WriteAsync(EndPoint remoteEndPoint, ReadOnlyMemory<byte> writeBuffer,
            SocketFlags flags = SocketFlags.None);
    }
}