using SharpMUSH.Interfaces;

namespace SharpMUSH
{
    public class GameService : IGameService
    {
        // Constructor with Dependency Injection
        public GameService(MUSHDatabase _db, MUSHServer _server, InputHandler _inputHandler, IServiceProvider _service)
        {
            DB = _db;
            Server = _server;
            InputHandle = _inputHandler;
            Service = _service;
        }

        public MUSHServer Server { get; private set; }

        public MUSHDatabase DB { get; private set; }

        public InputHandler InputHandle { get; private set; }
        public IServiceProvider Service { get; private set; }
    }
}
