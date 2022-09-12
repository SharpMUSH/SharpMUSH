using System.Net;

namespace SharpMUSH
{

    public static class Game
    {

        public static MUSHServer Server { get; private set; }
        public static InputHandler InputHandle { get; private set; }

        public static void Start()
        {
            // Start the server
            const int port = 1701;
            InputHandle = new InputHandler();
            Server = new MUSHServer(IPAddress.Any, port);
            Server.Start();
            Console.Write("Directory:" + Environment.CurrentDirectory);

            for (; ; )
            {
                string line = Console.ReadLine();
                //if (string.IsNullOrEmpty(line))
                //    break;

                // Restart the server
                if (line == "!")
                {
                    Console.Write("Server restarting...");
                    Server.Restart();
                    Console.WriteLine("Done!");
                    continue;
                }

                // Multicast admin message to all sessions
                line = "(admin) " + line;
                Server.Multicast(line);
            }

        }
    }
}