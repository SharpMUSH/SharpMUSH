namespace SharpMUSH.DB.Object
{
    public class Room : Thing
    {
        public IList<Exit> Exits { get; set; }
        public IList<Exit> Entrances { get; set; }


    }
}