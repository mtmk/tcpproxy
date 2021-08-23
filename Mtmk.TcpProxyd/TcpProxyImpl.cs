using System.Buffers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;

namespace Mtmk.TcpProxyd
{
    public class TcpProxyImpl : TcpProxy.TcpProxyBase
    {
        private readonly MemoryPool<byte> _pool = MemoryPool<byte>.Shared;
        public override async Task Open(IAsyncStreamReader<Packet> requestStream, IServerStreamWriter<Packet> responseStream, ServerCallContext context)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            var token = cts.Token;

            NetworkStream networkStream = null;
            
            while (await requestStream.MoveNext(token))
            {
                var pkt = requestStream.Current;
                if (pkt.Host != string.Empty)
                {
                    var tcpClient = new TcpClient();
                    await tcpClient.ConnectAsync(pkt.Host, pkt.Port, token);
                    networkStream = tcpClient.GetStream();
                    var stream = networkStream;
                    Task.Run(async delegate
                    {
                        while (!token.IsCancellationRequested)
                        {
                            using var memoryOwner = _pool.Rent(8192);
                            await stream.ReadAsync(memoryOwner.Memory, token);
                            await responseStream.WriteAsync(new Packet
                            {
                                Payload = ByteString.CopyFrom(memoryOwner.Memory.Span)
                            });
                        }
                    }, token);
                }

                if (networkStream != null && pkt.Payload != null)
                {
                    await networkStream.WriteAsync(pkt.Payload.Memory, token);
                }
            }
        }
    }
}