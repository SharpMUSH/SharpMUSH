using SharpMUSH.DB.ObjectAttribute;

namespace SharpMUSH.DB.Object
{
    public class Room : Thing
    {
        public IList<Exit> Exits { get; set; }
        public IList<Exit> Entrances { get; set; }

        public Room()
        {
            Location = this;
            Name = "The Void";
            // Populate Attributes with one Attrib

            Attributes = new List<Attrib>
                   {
                       new Attrib
                       {
                           Name = "DESC",
                           Value = "You are in the void. There is nothing here."
                       }
                   };


        }
    }
}