using System.Net;

namespace SharpMUSH.DB.Object
{
    public class Player : Thing
    {
        public bool Connected { get; set; }

        public DateTime? LastOn { get; set; }
        public DateTime CreationTime { get; set; }

        public bool EditMode { get; set; }
        public Thing? Editing { get; set; }
        public int? EditingId { get; set; }
        public string? EditBuffer { get; set; }
        public string? EditAttrib { get; set; }

        public string Password { get; set; }
        public string Salt { get; set; }
        public IList<Thing>? Owned { get; set; }
        public IPAddress? LastIP { get; set; }
        public string? LastHost { get; set; }

        public Player()
        {

            EditMode = false;
            EditBuffer = "";
            EditAttrib = "";
            Connected = false;
            LastOn = DateTime.MinValue;
            CreationTime = DateTime.Now;

        }
    }

}