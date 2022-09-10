using System.Net;

namespace SharpMUSH
{
    // ReSharper disable once InconsistentNaming
    public class MUSHSingleton
    {
        private static readonly MUSHSingleton instance = new MUSHSingleton();
        public MUSHServer Server { get; private set; }
        public InputHandler InputHandle { get; private set; }



        // Explicit static constructor to tell C# compiler
        // not to mark type as beforefieldinit
        static MUSHSingleton()
        {

        }

        public void Start()
        {
            MUSHDB.ClearConnectedUsers();
            const int port = 1701;
            InputHandle = new InputHandler();
            Server = new MUSHServer(IPAddress.Any, port);
            Server.Start();
            Console.Write("Directory:" + Environment.CurrentDirectory);
        }

        public static MUSHSingleton Instance => instance;
    }
}