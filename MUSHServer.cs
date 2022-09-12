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


        // FindSessionByThingID
        public List<MUSHSession>? FindSessionByThingId(int Id)
        {
            var sessions = Game.Server.Sessions.Values.Cast<MUSHSession>();
            return sessions.Where(s => s.ThingID == Id).ToList();

        }

        // FindSessionByPlayerName





    }
}

