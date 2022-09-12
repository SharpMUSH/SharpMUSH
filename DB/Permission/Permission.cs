using SharpMUSH.DB.Link;
using SharpMUSH.DB.Object;
using SharpMUSH.DB.ObjectAttribute;

namespace SharpMUSH.DB.ObjectPerm
{
    public class Permission
    {
        // efcore model for ObjectPerm

        public int Id { get; set; }
        public string Name { get; set; }

        public string Description { get; set; }

        public IList<Flag> Flags { get; set; }

        public IList<FlagPermission> FlagPermissions { get; set; }
        public IList<Thing> Things { get; set; }
        public IList<Attrib> Attributes { get; set; }

    }
}
