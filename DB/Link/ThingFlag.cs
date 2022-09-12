using SharpMUSH.DB.Object;
using SharpMUSH.DB.ObjectPerm;

namespace SharpMUSH.DB.Link
{
    public class ThingFlag
    {
        public int Id { get; set; }
        public int ThingId { get; set; }
        public int FlagId { get; set; }
        public Thing Thing { get; set; }
        public Flag Flag { get; set; }
    }
}
