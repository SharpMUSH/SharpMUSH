using System.ComponentModel;
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
           
            var Command = Input.Split(' ')[0].ToLower();
            var Text = Input.Substring(Command.Length + 1);

            var cmd = (baseCommand)CreateCmdInstance(Command);
            cmd.Cmd(Text, ThingID);
            

            if (cmd != null)
            {
                
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