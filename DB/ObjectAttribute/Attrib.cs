using SharpMUSH.DB.Object;
using SharpMUSH.DB.ObjectPerm;

namespace SharpMUSH.DB.ObjectAttribute
{
    public class Attrib
    {

        public int Id { get; set; }

        public string Name { get; set; }
        public string Value { get; set; }
        public Thing? Thing { get; set; }
        public int? ThingId { get; set; }
        public string? Command { get; set; }
        public bool IsFUN { get; set; } = false;
        public bool IsCMD { get; set; } = false;
        public bool IsGlobal { get; set; } = false;
        public IList<Flag>? Flags { get; set; }
        public IList<Permission>? Permissions { get; set; }

    }
}