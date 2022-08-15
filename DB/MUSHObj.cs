using System.ComponentModel.DataAnnotations;

namespace SharpMUSH.DB
{
    public class MUSHObj
    {
        [Key]
        public int ThingID { get; set; }

        public string Name { get; set; }

        public ICollection<Attrib> Attributes { get; set; }

        public MUSHObj Location { get; set; } = null;

        public ICollection<MUSHObj> Parents { get; set; }
        public ICollection<MUSHObj> Children { get; set; }
        public ICollection<FlagType> Flags { get; set; }

        public UserType Owner { get; set; } = null;
    }
}