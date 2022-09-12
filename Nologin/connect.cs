namespace SharpMUSH.Nologin
{
    public class connect
    {
        MUSHDatabase DB = new MUSHDatabase();

        public string CmdReply = "";
        public int? ThingID = -1;
        public string Name = "";
        public connect(string[] args, Guid guid)
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
