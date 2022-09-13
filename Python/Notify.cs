namespace SharpMUSH.Python
{
    public class Notify : GameService
    {
        // Constructor with Dependency Injection
        public Notify(MUSHDatabase _db, MUSHServer _server, InputHandler _inputHandler, IServiceProvider _service) : base(_db, _server, _inputHandler, _service)
        {
        }

        public void ToPlayer(int Id, string message)
        {

            var sessions = Server.FindSessionByThingId(Id);
            foreach (var session in sessions)
            {
                session.Send(message);
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
