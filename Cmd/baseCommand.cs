namespace SharpMUSH.Cmd
{
    public class baseCommand
    {
        public string CmdReply = "";

        public virtual async void Cmd(string Text, int ThingID)
        {

        }
    }
}