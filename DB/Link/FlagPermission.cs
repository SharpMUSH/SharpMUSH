using SharpMUSH.DB.ObjectPerm;

namespace SharpMUSH.DB.Link
{
    public class FlagPermission
    {
        public int Id { get; set; }
        public int FlagId { get; set; }
        public int PermissionId { get; set; }
        public Flag Flag { get; set; }
        public Permission Permission { get; set; }
    }
}
