using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Interfaces;

namespace SharpMUSH
{

    internal class Game : IGame
    {
        public MUSHServer Server { get; set; }
        public MUSHDatabase DB { get; set; }
        public InputHandler InputHandle { get; set; }
        IServiceProvider Service;

        public Game(MUSHServer _server, MUSHDatabase _db, InputHandler _inputHandler, IServiceProvider _service)
        {
            Server = _server;
            DB = _db;
            InputHandle = _inputHandler;
            Service = _service;
        }

        public void Start()
        {
            // Start the server
            const int port = 1701;

            Server = Service.GetService<MUSHServer>();
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