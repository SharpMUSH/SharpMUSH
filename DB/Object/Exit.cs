namespace SharpMUSH.DB.Object
{
    public class Exit : Thing
    {
        public Room? Destination { get; set; }
        public int? DestinationId { get; set; }
        // Exits can only exist in rooms
        // Override the definition
        new public Room? Location { get; set; }

    }
}