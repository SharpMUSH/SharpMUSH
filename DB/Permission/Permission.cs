using SharpMUSH.DB.Object;

namespace SharpMUSH.DB.ObjectPerm
{
    public class Permission
    {
        // efcore model for ObjectPerm

        public int Id { get; set; }
        public string Name { get; set; }

        public string Description { get; set; }
        public bool IsDefault { get; set; }
        public bool IsSet { get; set; }

        public ICollection<Flag> Flags { get; set; }
        public ICollection<Thing> Things { get; set; }

    }
}
