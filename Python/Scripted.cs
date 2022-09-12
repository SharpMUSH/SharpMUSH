namespace SharpMUSH.Python
{
    public class Scripted
    {
        private MUSHDatabase DB = new MUSHDatabase();

        public int Caller { get; protected set; }
        public int Executor { get; protected set; }

        private readonly Notify notify = new Notify();
        public ScriptDB Data;

        public Scripted(int caller, int executor)
        {
            Caller = caller;
            Executor = executor;
            Data = new ScriptDB(Executor, Caller);

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
                Game.Server.Multicast(message);
            }
        }
    }
}
