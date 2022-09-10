using SharpMUSH.DB.Object;
using SharpMUSH.DB.ObjectAttribute;

namespace SharpMUSH.DB.ObjectPerm
{
    public class Flag
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public ICollection<Thing> Things { get; set; }
        public ICollection<Command> Commands { get; set; }
        public ICollection<Permission> Permissions { get; set; }

    }
}