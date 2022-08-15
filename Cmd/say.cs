using SharpMUSH.DB;

namespace SharpMUSH.Cmd
{
    public class say : baseCommand
    {
        MUSHSingleton Game = MUSHSingleton.Instance;

        public override async void Cmd(string Text, int ThingID)
        {

            
            var user = await MUSHDB.GetUserByIdAsync(ThingID);
           


            if (user == null) return;


            var targets = await MUSHDB.GetUsersInLocationByIdAsync(user.Location.ThingID);
            try
            {
                var UserSession = (MUSHSession)Game.Server.FindSession(user.Session);
                UserSession.Send("You say \"" + Text + "\"\r\n");
            }
            catch
            {

            }



            if (targets != null)
            {
                foreach (UserType u in targets)
                {
                    if (u.ThingID != ThingID && u.Connected)
                    {
                        Game.Server.FindSession(u.Session).Send(user.Name + " says \"" + Text + "\"\r\n");
                    }
                }
            }

            //CmdReply = "Processed by say " + args;
            base.Cmd(Text, ThingID);
        }

        public say()
        {

        }
    }
}