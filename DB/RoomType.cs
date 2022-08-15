namespace SharpMUSH.DB
{
    public class RoomType : MUSHObj
    {
        public UserType Owner { get; set; }

        public ThingType Location { get; set; } = null;
    }
}