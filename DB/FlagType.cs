using System.ComponentModel.DataAnnotations;

namespace SharpMUSH.DB
{
    public class FlagType
    {
        [Key]
        public int FlagID { get; set; }

        public string Name { get; set; }

        public ICollection<MUSHObj> Things { get; set; }
    }
}