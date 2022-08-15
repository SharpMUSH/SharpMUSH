namespace SharpMUSH.DB
{
    public class UserType : MUSHObj
    {
        public bool Connected { get; set; } = false;

        public DateTime LastOn { get; set; } = DateTime.MinValue;

        public Guid Session { get; set; } = Guid.Empty;

        public string Password { get; set; }
    }
}