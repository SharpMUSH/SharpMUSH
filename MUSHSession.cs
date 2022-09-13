using Microsoft.Extensions.DependencyInjection;
using NetCoreServer;
using SharpMUSH.Nologin;
using System.Net.Sockets;
using System.Text;

namespace SharpMUSH
{
    public class MUSHSession : TcpSession
    {

        private bool auth = false;
        public int ThingID = -1;
        IServiceProvider Service;

        public MUSHSession(MUSHServer _server, IServiceProvider _service) : base(_server)
        {
            Service = _service;
        }

        protected override void OnConnected()
        {
            Console.WriteLine($"Chat TCP session with Id {Id} connected!");
            MUSHDatabase DB = Service.GetService<MUSHDatabase>();
            // Get LOGON attribute from -1 thing
            var logon = DB.GetAttribute(1, "LOGON");
            if (logon != null)
            {
                // Send LOGON attribute to the connected client
                Send(logon.Value);
            }
        }

        protected override void OnDisconnected()
        {
            Console.WriteLine($"Chat TCP session with Id {Id} disconnected!");
            if (auth)
            {
                MUSHDatabase DB = Service.GetService<MUSHDatabase>();
                DB.SetUserDisconnected(ThingID);
                var user = DB.GetPlayerById(ThingID);
                Server.Multicast(user.Name + "(" + ThingID + ") has disconnected.\r\n");
            }

        }

        protected override void OnReceived(byte[] buffer, long offset, long size)
        {

            string message = Encoding.UTF8.GetString(buffer, (int)offset, (int)size);
            Console.WriteLine("Incoming: " + message);

            if (auth)
            {
                // Multicast message to all connected sessions
                string fromServer = Service.GetService<InputHandler>().FromClient(message, ThingID);

                Send(fromServer);
            }
            else
            {
                Send("Cmd received\r\n");
                var args = message.Split(' ');
                if (string.Equals(args[0], "connect", StringComparison.OrdinalIgnoreCase))
                {
                    Send("Connecting... \r\n");

                    connect c = Service.GetService<connect>();
                    c.Cmd(args, Id);
                    if (c.ThingID >= 0)
                    {
                        MUSHDatabase DB = Service.GetService<MUSHDatabase>();
                        auth = true;
                        ThingID = (int)c.ThingID;
                        var user = DB.GetPlayerById(ThingID);
                        var name = user.Name;

                        Server.Multicast(name + "(#" + ThingID + ") has connected.\r\n");

                    }
                    Send(c.CmdReply + "\r\n");
                }
            }

            // If the buffer starts with '!' the disconnect the current session
            if (message == "!")
                Disconnect();
        }

        protected override void OnError(SocketError error)
        {
            Console.WriteLine($"Chat TCP session caught an error with code {error}");
        }
    }
}