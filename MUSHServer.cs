using Microsoft.Extensions.DependencyInjection;
using NetCoreServer;
using System.Net.Sockets;

namespace SharpMUSH
{
    public class MUSHServer : TcpServer
    {
        IServiceProvider Service;
        public MUSHServer(IServiceProvider _services) : base(System.Net.IPAddress.Any, 1701)
        {
            Service = _services;
        }

        protected override TcpSession CreateSession()
        { return Service.GetService<MUSHSession>(); }

        protected override void OnError(SocketError error)
        {
            Console.WriteLine($"Chat TCP server caught an error with code {error}");

        }

        public void Start()
        {
            Console.WriteLine("Chat TCP server listening on port 1701...");
            base.Start();
        }


        // FindSessionByThingID
        public List<MUSHSession>? FindSessionByThingId(int Id)
        {
            var sessions = Sessions.Values.Cast<MUSHSession>();
            return sessions.Where(s => s.ThingID == Id).ToList();

        }

        // FindSessionByPlayerName





    }
}

