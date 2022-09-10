namespace SharpMUSH.Python
{
    public class Scripted
    {
        private MUSHSingleton Game = MUSHSingleton.Instance;

        public int Caller { get; protected set; }
        public int Executor { get; protected set; }
        public string Command { get; protected set; }
        public string Switch { get; protected set; }
        public string[] Args { get; protected set; }
        private readonly Notify notify = new Notify();
        private readonly ScriptDB DB;

        public Scripted(int caller, int executor, string cmd, string swc, string[] args)
        {
            Caller = caller;
            Executor = executor;
            Command = cmd;
            Switch = swc;
            Args = args;
            DB = new ScriptDB(Executor, Caller);

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
            if (MUSHDB.HasPermission(Executor, "Broadcast"))
            {
                Game.Server.Multicast(message);
            }
        }
    }
}
