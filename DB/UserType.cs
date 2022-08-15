namespace SharpMUSH.DB
{
    public class UserType : ThingType
    {
        public bool Connected { get; set; } = false;

        public DateTime LastOn { get; set; } = DateTime.MinValue;

        public Guid Session { get; set; } = Guid.Empty;

        public string Password { get; set; }
    }
}