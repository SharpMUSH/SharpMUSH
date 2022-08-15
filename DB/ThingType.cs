using System.ComponentModel.DataAnnotations;

namespace SharpMUSH.DB
{
    public class ThingType
    {
        [Key]
        public int ThingID { get; set; }

        public string Name { get; set; }

        public ICollection<Attrib> Attributes { get; set; }

        public ThingType Location { get; set; } = null;

        public ICollection<ThingType> Parents { get; set; }
        public ICollection<ThingType> Children { get; set; }
        public ICollection<FlagType> Flags { get; set; }

        public UserType Owner { get; set; } = null;
    }
}