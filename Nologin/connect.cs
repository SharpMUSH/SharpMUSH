using Microsoft.EntityFrameworkCore;
using SharpMUSH.Cmd;

namespace SharpMUSH.Nologin
{
    public class connect : baseCommand
    {
        MUSHSingleton Game = MUSHSingleton.Instance;
        public string CmdReply = "";
        public int? ThingID = -1;
        public string Name = "";
        public connect(string[] args, Guid guid)
        {
           var id = MUSHDB.AuthUserByNameAsync(args[1], args[2].Replace("\n", "").Replace("\r",""), guid).Result;
           

           if (id >= 0)
           {
               
               ThingID = id;
           }
           else
           {
               CmdReply = "No such user.";
           }
        }
    }
}