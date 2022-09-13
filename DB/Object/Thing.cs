using SharpMUSH.DB.Link;
using SharpMUSH.DB.ObjectAttribute;
using SharpMUSH.DB.ObjectPerm;
using System.ComponentModel.DataAnnotations;

namespace SharpMUSH.DB.Object
{
    public class Thing
    {
        [Key, Required]
        public int Id { get; set; }

        public string Name { get; set; }


        public IList<Attrib>? Attributes { get; set; }

        public Thing? Location { get; set; } = null;
        public int? LocationId { get; set; }


        public IList<Thing> Parents { get; set; } = null;
        public IList<Thing> Children { get; set; } = null;
        public bool Global { get; set; } = false;
        public IList<Flag>? Flags { get; set; } = null;
        public IList<Permission>? Permissions { get; set; } = null;
        public IList<ThingFlag>? ThingFlags { get; set; } = null;
        public int OwnerId { get; set; }

    }
}