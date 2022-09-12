using SharpMUSH.DB.Link;
using SharpMUSH.DB.Object;
using SharpMUSH.DB.ObjectAttribute;

namespace SharpMUSH.DB.ObjectPerm
{
    public class Flag
    {
        public int Id { get; set; }

        public string Name { get; set; }
        public string? Description { get; set; }

        public IList<Thing>? Things { get; set; }

        public IList<ThingFlag>? ThingFlags { get; set; }
        public IList<Attrib>? Attributes { get; set; }

        public IList<FlagPermission>? FlagPermissions { get; set; }
        public IList<Permission>? Permissions { get; internal set; }



        public Flag()
        {
            this.Things = new List<Thing>();
            this.ThingFlags = new List<ThingFlag>();
            this.Attributes = new List<Attrib>();
            this.FlagPermissions = new List<FlagPermission>();
            this.Permissions = new List<Permission>();
        }
    }
}