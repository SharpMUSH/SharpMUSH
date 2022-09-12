namespace SharpMUSH.Python
{
    public class Notify
    {

        private MUSHDatabase DB = new MUSHDatabase();

        public void ToPlayer(int playerId, string message)
        {



            // Get the player from the database
            var player = DB.GetPlayerById(playerId);

            // Get the session from the player
            if (player.Session != null)
            {
                foreach (var sess in player.Session)
                {
                    var session = Game.Server.FindSession(sess);

                    session.Send(message);

                }
            }

        }

        public void ToRoom(int roomId, string message)
        {


            // Get the contents of the room
            var contents = DB.GetPlayersInLocationById(roomId);

            // Send the message to each player in the room
            foreach (var player in contents)
            {
                ToPlayer(player.Id, message);
            }
        }

        public void ToRoomExcept(int roomId, int playerId, string message)
        {
            // Get the contents of the room
            var contents = DB.GetPlayersInLocationById(roomId);

            // Send the message to each player in the room except the player

            foreach (var player in contents)
                if (player.Id != playerId)
                    ToPlayer(player.Id, message);
        }
    }

}

