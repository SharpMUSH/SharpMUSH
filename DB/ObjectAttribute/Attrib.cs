using SharpMUSH.DB.Object;
using SharpMUSH.DB.ObjectPerm;

namespace SharpMUSH.DB.ObjectAttribute
{
    public class Attrib
    {

        public int Id { get; set; }

        public string Name { get; set; }
        public string Value { get; set; }
        public Thing Thing { get; set; }

    }
    public class Command : Attrib
    {


        public string Cmd { get; set; }
        public IList<Argument> Arguments { get; set; }
        public bool Executable { get; set; } = false;
        public bool Callable { get; set; } = false;

        public IList<Flag> Flags { get; set; }
    }
    public class Argument
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Command Cmd { get; set; }
    }
}