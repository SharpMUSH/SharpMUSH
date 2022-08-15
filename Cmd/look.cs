using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpMUSH.Cmd
{
    public class look :baseCommand
    {
        MUSHSingleton Game = MUSHSingleton.Instance;
        public override async void Cmd(string Text, int ThingID)
        {
            var user = await MUSHDB.GetUserByIdAsync(ThingID);
            if (user == null) return;

            var room = await MUSHDB.getRoomByIdAsync(user.Location.ThingID);

            if(room == null) return;

            var session = Game.Server.FindSession(user.Session);

            if (session == null) return;

            session.Send(room.Name + "(#" + room.ThingID + ")\r\n" +
                         room.Attributes.First(a => a.Name == "Description").Value + "\r\n");
            
            var players = await MUSHDB.GetUsersInLocationByIdAsync(room.ThingID);
            if (players != null)
            {
                session.Send("\r\nPlayers here:\r\n");
                foreach (var player in players)
                {
                    session.Send("     " + player.Name + "(#" + player.ThingID + ")" + (!player.Connected ? "(Offline)" : "") + "\r\n");
                }
            }
            var things = await MUSHDB.GetThingsInLocationByIdAsync(room.ThingID);
            if (things != null)
            {
                session.Send("\r\nThings here:\r\n");
                foreach (var thing in things)
                {
                    session.Send("     " + thing.Name + "(#" + thing.ThingID + ")" + "\r\n");
                }
            }



            base.Cmd(Text, ThingID);


        }
    }
}
