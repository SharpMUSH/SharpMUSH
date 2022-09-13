namespace SharpMUSH.Nologin
{
    internal class connect : GameService
    {

        // Constructor with Dependency Injection
        public connect(MUSHDatabase _db, MUSHServer _server, InputHandler _inputHandler, IServiceProvider _service) : base(_db, _server, _inputHandler, _service)
        {
        }
        public string CmdReply = "";
        public int? ThingID = -1;
        public string Name = "";
        public void Cmd(string[] args, Guid guid)
        {

            if (args.Length == 3)
            {

                var id = DB.AuthenticatePlayer(args[1], args[2].Replace("\n", "").Replace("\r", ""), guid);


                if (id > 0)
                {

                    ThingID = id;
                }
                else
                {
                    CmdReply = "Unable to authenticate.";
                }
            }
            else
            {
                CmdReply = "Invalid arguments.";
            }
        }
    }
}
