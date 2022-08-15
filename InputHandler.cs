using System.ComponentModel;
using System.Net.Mime;
using SharpMUSH.Cmd;
using System.Reflection;

namespace SharpMUSH
{
    public class InputHandler
    {
        MUSHSingleton Game = MUSHSingleton.Instance;

        public InputHandler()
        {
            
        }

        
        public string FromClient(string Input, int ThingID)
        {
            Input = Input.Replace("\n", "").Replace("\r", "");
           
            var Command = Input.Split(' ');
            var Text = "";

            if (Command.Length > 1)
            {
                Text = Input.Substring(Command.Length + 1);
            }



            var cmd = (baseCommand)CreateCmdInstance(Command[0]);
            

            
            

            if (cmd != null)
            {
                cmd.Cmd(Text, ThingID);
                return cmd.CmdReply;
            }
            else
            {
                return "404 Command Not Found.\r\n";
            }
        }

        private object? CreateCmdInstance(string className)
        {
            try
            {
                return Activator.CreateInstance(Type.GetType("SharpMUSH.Cmd." + className));
                
            }
            catch (Exception ex)
            {
                return null;
            }
        }


        private IEnumerable<Type> GetDerivedTypesFor(Type baseCommand)
        {
            var assembly = Assembly.GetExecutingAssembly();

            return assembly.GetTypes()

                .Where(baseCommand.IsAssignableFrom)
                .Where(t => baseCommand != t);
        }
    }
}