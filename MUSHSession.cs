using NetCoreServer;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using SharpMUSH.Cmd;
using SharpMUSH.Nologin;

namespace SharpMUSH
{
    internal class MUSHSession : TcpSession
    {
        private readonly MUSHSingleton _mushSingleton;
        private bool auth = false;
        private int ThingID = -1;

        public MUSHSession() : base (MUSHSingleton.Instance.Server)
        {
            _mushSingleton = MUSHSingleton.Instance;
        }

        protected override void OnConnected()
        {
            Console.WriteLine($"Chat TCP session with Id {Id} connected!");

            // Send invite message
            string message = "Hello from TCP chat! Please send a message or '!' to disconnect the client!";
            SendAsync(message);
        }

        protected override void OnDisconnected()
        {
            Console.WriteLine($"Chat TCP session with Id {Id} disconnected!");
            if (auth)
            {
                MUSHDB.SetUserDisconnected(ThingID);
                var user = MUSHDB.GetUserByIdAsync(ThingID).Result;
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
                Send(_mushSingleton.InputHandle.FromClient(message, ThingID));
            }
            else
            {
                Send("Cmd received\r\n");
                var args = message.Split(' ');
                if (args[0].ToLower() == "connect")
                {
                    Send("Connecting... \r\n");
                    connect c = new connect(args, Id);
                    if (c.ThingID >= 0)
                    {
                        auth = true;
                        ThingID = (int)c.ThingID;
                        var user = MUSHDB.GetUserByIdAsync(ThingID).Result;
                        var name = user.Name;

                        look cmd = new look();
                        cmd.Cmd("here",ThingID);

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