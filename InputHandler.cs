using Microsoft.Scripting.Hosting;
using SharpMUSH.DB.Object;
using SharpMUSH.DB.ObjectAttribute;
using SharpMUSH.Python;


namespace SharpMUSH
{
    public class InputHandler
    {

        MUSHDatabase DB = new MUSHDatabase();





        // Constructor for InputHandler
        public InputHandler()
        {

        }


        public string FromClient(string Input, int ThingID)
        {

            Microsoft.Scripting.Hosting.ScriptEngine pyEngine = IronPython.Hosting.Python.CreateEngine();

            Microsoft.Scripting.Hosting.ScriptScope pyScope = pyEngine.CreateScope();

            // Get the player
            var player = DB.GetPlayerById(ThingID);

            // Parse input
            string[] InputArray = Input.Split(' ');

            // Get the first word
            string cmd = InputArray[0];

            // Does the command contain a switch forwardslash

            if (cmd.Contains("/"))
            {
                // Split the command and switch
                string[] cmdArray = cmd.Split('/');

                // Set the command
                cmd = cmdArray[0];

                // Set the switch
                string sw = cmdArray[1];

                // Set the switch in the scope
                pyScope.SetVariable("switch", sw);

            }

            // If InputArray has more than one word add elements 1 onward to Args
            string[] Args = new string[InputArray.Length - 1];
            if (InputArray.Length > 1)
            {
                for (int i = 1; i < InputArray.Length; i++)
                {
                    Args[i - 1] = InputArray[i];
                }
            }
            // Set nArgs to the number of arguments
            int nArgs = Args.Length;

            // strip any line returns and newlines from Input

            Input = Input.Replace("\r", "");
            Input = Input.Replace("\n", "");


            pyScope.SetVariable("Input", Input);
            pyScope.SetVariable("ThingID", ThingID);

            // Create a Scripted Object and set variable

            // Create a list of Things to check for commands
            var thingsToCheck = new List<Thing>();
            // Add the command to the list
            thingsToCheck.Add(DB.GetThingById(0));
            // Add the player to the list
            thingsToCheck.Add(player);
            // Add the player's ancestors to the list
            thingsToCheck.AddRange(DB.GetAncestors(player.Id));

            // Add the player's location to the list
            thingsToCheck.Add(DB.GetThingById(player.Location.Id));
            // Add the player's location's ancestors to the list
            thingsToCheck.AddRange(DB.GetAncestors(player.Location.Id));

            // Add the player's contents to the list
            thingsToCheck.AddRange(DB.GetContentsById(player.Id));
            // Add the player's contents' ancestors to the list

            foreach (var thing in DB.GetContentsById(player.Id))
            {
                thingsToCheck.AddRange(DB.GetAncestors(thing.Id));
            }

            // Add the player's location's contents to the list
            thingsToCheck.AddRange(DB.GetContentsById(player.Location.Id));
            // Add the player's location's contents' ancestors to the list
            foreach (var thing in DB.GetContentsById(player.Location.Id))
            {
                thingsToCheck.AddRange(DB.GetAncestors(thing.Id));
            }

            // Create a list of commands to check
            var commandsToCheck = new List<Attrib>();

            // Add the commands from the things in the list
            foreach (var thing in thingsToCheck)
            {
                commandsToCheck.AddRange(DB.GetCommandsById(thing.Id));
            }

            // Check if the command exists

            var foundCommand = commandsToCheck.Find(match: c => c.Command == cmd);

            if (foundCommand != null)
            {
                // Get the script for the command
                var script = DB.GetScript(foundCommand.Value, foundCommand.Id);
                if (script != null)
                {
                    try
                    {
                        var scripted = new Scripted(ThingID, foundCommand.Id);
                        pyScope.SetVariable("MUSH", scripted);
                        pyScope.SetVariable("Input", Input.Replace("\n", "").Replace("\r", ""));
                        // Execute the Python code
                        var pyCode = pyEngine.CreateScriptSourceFromString(script.Value);
                        var pyResult = pyCode.Execute(pyScope);
                        // Return the result
                        //return pyResult.ToString();
                        return "";

                    }
                    catch (Exception ex)
                    {
                        ExceptionOperations eo = pyEngine.GetService<ExceptionOperations>();
                        string error = eo.FormatException(ex);
                        return error;
                    }
                }
                return "No script found for command " + cmd + " on thing " + foundCommand.Id + ".\n";
            }
            else
            {
                return "No command found for " + cmd + ".\n";
            }
        }



    }









}