using System.ComponentModel.DataAnnotations;

namespace SharpMUSH.DB
{
    public class Attrib
    {
        [Key]
        public int AttribId { get; set; }

        public string Name { get; set; }
        public string Value { get; set; }
        public bool Executable { get; set; }
        public string Command { get; set; }
        public ThingType Obj { get; set; }
    }
}