namespace SharpMUSH.Python
{
    public class Notify
    {
        MUSHSingleton Game = MUSHSingleton.Instance;

        public void ToPlayer(int playerId, string message)
        {
            // Get the player from the database
            var player = MUSHDB.GetPlayerById(playerId);

            // Get the session from the player
            var session = Game.Server.FindSession(player.Session);
            if (session.IsConnected)
            {
                session.Send(message);
            }
        }

        public void ToRoom(int roomId, string message)
        {


            // Get the contents of the room
            var contents = MUSHDB.GetPlayersInLocationById(roomId);

            // Send the message to each player in the room
            foreach (var player in contents)
            {
                ToPlayer(player.Id, message);
            }
        }

        public void ToRoomExcept(int roomId, int playerId, string message)
        {
            // Get the contents of the room
            var contents = MUSHDB.GetPlayersInLocationById(roomId);

            // Send the message to each player in the room except the player

            foreach (var player in contents)
                if (player.Id != playerId)
                    ToPlayer(player.Id, message);
        }
    }

}

