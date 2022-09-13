namespace SharpMUSH.Python
{
    public class ScriptFormat : GameService
    {
        public ScriptFormat(MUSHDatabase _db, MUSHServer _server, InputHandler _inputHandler, IServiceProvider _service) : base(_db, _server, _inputHandler, _service)
        {

        }

        public string Color(string foreground, string background, string str)
        {
            return "<font color=\"" + foreground + "\" bgcolor=\"" + background + "\">" + str + "</font>";
        }

    }


}
