using NetCoreServer;
using System.Net;
using System.Net.Sockets;

namespace SharpMUSH
{
    public class MUSHServer : TcpServer
    {
        
        public MUSHServer(IPAddress address, int port) : base(address, port)
        {
        }

        protected override TcpSession CreateSession()
        { return new MUSHSession(); }

        protected override void OnError(SocketError error)
        {
            Console.WriteLine($"Chat TCP server caught an error with code {error}");
        }
    }
}

namespace SharpMUSH.DB
{
}