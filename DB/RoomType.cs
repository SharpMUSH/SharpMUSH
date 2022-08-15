namespace SharpMUSH.DB
{
    public class RoomType : ThingType
    {
        public UserType Owner { get; set; }

        public ThingType Location { get; set; } = null;
    }
}