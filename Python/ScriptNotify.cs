namespace SharpMUSH.Python
{
    public class ScriptNotify
    {
        private MUSHDatabase DB;

        private MUSHServer Server;
        private Notify notify;

        public int Executor { get; set; }

        public ScriptNotify(MUSHDatabase _db, MUSHServer _server, Notify _notify)
        {
            DB = _db;
            Server = _server;
            notify = _notify;
        }

        // Pemit(ThingID, Message)
        // Send message to thing
        public void Pemit(int thingID, string message)
        {
            notify.ToPlayer(thingID, message);
        }

        // Remit(RoomID, Message)
        // Send message to Contents of room
        public void Remit(int roomID, string message)
        {
            notify.ToRoom(roomID, message);
        }

        // Oemit(PlayerId, RoomId, Message)
        // Send message all players in room except for Player
        public void Oemit(int playerId, int roomId, string message)
        {
            notify.ToRoomExcept(roomId, playerId, message);
        }

        // Broadcast(Message)
        // Send message to all connected users
        public void Broadcast(string message)
        {
            // Check if Executor has Permission Broadcast
            if (DB.HasPermission(Executor, "Broadcast"))
            {
                Server.Multicast(message);
            }
        }
    }
}
