namespace SharpMUSH.DB.Object
{
    public class Player : Thing
    {
        public bool Connected { get; set; } = false;

        public DateTime LastOn { get; set; } = DateTime.MinValue;

        public Guid Session { get; set; } = Guid.Empty;

        public bool EditMode { get; set; } = false;

        public string Password { get; set; }
        public string Salt { get; set; }
        public IList<Thing> Owned { get; set; } = null;
    }
}