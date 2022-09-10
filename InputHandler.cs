
using SharpMUSH.Python;

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

            Microsoft.Scripting.Hosting.ScriptEngine pyEngine = IronPython.Hosting.Python.CreateEngine();
            Microsoft.Scripting.Hosting.ScriptScope pyScope = pyEngine.CreateScope();

            // Parse Input
            var args = Input.Split(' ');
            // Check if arg[0] contains a switch delimited by a forward-slash
            // Store command and switch in a variable for later use
            var cmd = args[0];
            var sw = "";

            if (cmd.Contains("/"))
            {
                var cmdSplit = cmd.Split('/');
                cmd = cmdSplit[0];
                sw = cmdSplit[1];
            }

            // remove cmd from args list
            args = args.Skip(1).ToArray();


            // Check if command exists
            // We check for the command on a Command attribute in the following order:
            // Command with NULL Thing > Player > Player Ancestors > Player Location > Player Location Ancestors >
            // Player Contents > Player Contents Ancestors > Player Location Contents > Player Location Contents Ancestors


            // If we don't find a Command we return an error

            // If we don't find a Command we return an Error message

            // Check for NULL Thing Command
            var cmds = MUSHDB.GetCommandsByThingId(0);
            if (cmds != null)
            {
                // Iterate through the commands and check for a match
                foreach (var c in cmds)
                {
                    if (c.Name.ToLower() == cmd.ToLower())
                    {
                        var scripted = new Scripted(ThingID, 0, cmd, sw, args);
                        pyScope.SetVariable("MUSH", scripted);
                        // Execute the Python code
                        var pyCode = pyEngine.CreateScriptSourceFromString(c.Value);
                        var pyResult = pyCode.Execute(pyScope);
                        // Return the result
                        return pyResult.ToString();
                    }
                }
            }
            // Check for Player Command
            cmds = MUSHDB.GetCommandsByThingId(ThingID);
            if (cmds != null)
            {
                // Iterate through the commands and check for a match
                foreach (var c in cmds)
                {
                    if (c.Name.ToLower() == cmd.ToLower())
                    {
                        var scripted = new Scripted(ThingID, ThingID, cmd, sw, args);
                        pyScope.SetVariable("MUSH", scripted);
                        // Execute the Python code
                        var pyCode = pyEngine.CreateScriptSourceFromString(c.Value);
                        var pyResult = pyCode.Execute(pyScope);
                        // Return the result
                        return pyResult.ToString();
                    }
                }
            }
            // Check for Player Ancestors Command
            var playerAncestors = MUSHDB.GetParentsByThingId(ThingID);
            if (playerAncestors != null)
            {
                foreach (var a in playerAncestors)
                {
                    cmds = MUSHDB.GetCommandsByThingId(a.Id);
                    if (cmds != null)
                    {
                        // Iterate through the commands and check for a match
                        foreach (var c in cmds)
                        {
                            if (c.Name.ToLower() == cmd.ToLower())
                            {
                                var scripted = new Scripted(ThingID, ThingID, cmd, sw, args);
                                pyScope.SetVariable("MUSH", scripted);
                                // Execute the Python code
                                var pyCode = pyEngine.CreateScriptSourceFromString(c.Value);
                                var pyResult = pyCode.Execute(pyScope);
                                // Return the result
                                return pyResult.ToString();
                            }
                        }
                    }
                }
            }
            // Check for Player Location Command
            var playerLocation = MUSHDB.GetLocationByThingId(ThingID);
            if (playerLocation != null)
            {
                cmds = MUSHDB.GetCommandsByThingId(playerLocation.Id);
                if (cmds != null)
                {
                    // Iterate through the commands and check for a match
                    foreach (var c in cmds)
                    {
                        if (c.Name.ToLower() == cmd.ToLower())
                        {
                            var scripted = new Scripted(ThingID, playerLocation.Id, cmd, sw, args);
                            pyScope.SetVariable("MUSH", scripted);
                            // Execute the Python code
                            var pyCode = pyEngine.CreateScriptSourceFromString(c.Value);
                            var pyResult = pyCode.Execute(pyScope);
                            // Return the result
                            return pyResult.ToString();
                        }
                    }
                }
                // Check for Player Location Ancestors Command

                foreach (var a in playerLocation.Parents)
                {
                    cmds = MUSHDB.GetCommandsByThingId(a.Id);
                    if (cmds != null)
                    {
                        // Iterate through the commands and check for a match
                        foreach (var c in cmds)
                        {
                            if (c.Name.ToLower() == cmd.ToLower())
                            {
                                var scripted = new Scripted(ThingID, playerLocation.Id, cmd, sw, args);
                                pyScope.SetVariable("MUSH", scripted);
                                // Execute the Python code
                                var pyCode = pyEngine.CreateScriptSourceFromString(c.Value);
                                var pyResult = pyCode.Execute(pyScope);
                                // Return the result
                                return pyResult.ToString();
                            }
                        }
                    }
                }


            }
            // Check for Player Contents Command
            var playerContents = MUSHDB.GetContentsByThingId(ThingID);
            if (playerContents != null)
            {
                foreach (var c in playerContents)
                {
                    cmds = MUSHDB.GetCommandsByThingId(c.Id);
                    if (cmds != null)
                    {
                        // Iterate through the commands and check for a match
                        foreach (var cmd1 in cmds)
                        {
                            if (cmd1.Name.ToLower() == cmd.ToLower())
                            {
                                var scripted = new Scripted(ThingID, c.Id, cmd, sw, args);
                                pyScope.SetVariable("MUSH", scripted);
                                // Execute the Python code
                                var pyCode = pyEngine.CreateScriptSourceFromString(cmd1.Value);
                                var pyResult = pyCode.Execute(pyScope);
                                // Return the result
                                return pyResult.ToString();
                            }
                        }
                    }
                }
            }
            // Check for Player Contents Ancestors Command
            if (playerContents != null)
            {
                foreach (var c in playerContents)
                {
                    var playerContentsAncestors = MUSHDB.GetParentsByThingId(c.Id);
                    if (playerContentsAncestors != null)
                    {
                        foreach (var a in playerContentsAncestors)
                        {
                            cmds = MUSHDB.GetCommandsByThingId(a.Id);
                            if (cmds != null)
                            {
                                // Iterate through the commands and check for a match
                                for (var i = 0; i < cmds.Count; i++)
                                {

                                    var cmd1 = cmds[i];
                                    if (string.Equals(cmd1.Name, cmd, StringComparison.OrdinalIgnoreCase))
                                    {
                                        var scripted = new Scripted(ThingID, c.Id, cmd, sw, args);
                                        pyScope.SetVariable("MUSH", scripted);
                                        // Execute the Python code
                                        var pyCode = pyEngine.CreateScriptSourceFromString(cmd1.Value);
                                        var pyResult = pyCode.Execute(pyScope);
                                        // Return the result
                                        return pyResult.ToString();
                                    }
                                }
                            }
                        }
                    }
                }
            }
            // Check for Player Location Contents Command
            var playerLocationContents = MUSHDB.GetContentsByThingId(playerLocation.Id);
            if (playerLocationContents != null)
            {
                foreach (var c in playerLocationContents)
                {
                    cmds = MUSHDB.GetCommandsByThingId(c.Id);
                    if (cmds != null)
                    {
                        // Iterate through the commands and check for a match
                        foreach (var cmd1 in cmds)
                        {
                            if (cmd1.Name.ToLower() == cmd.ToLower())
                            {
                                var scripted = new Scripted(ThingID, c.Id, cmd, sw, args);
                                pyScope.SetVariable("MUSH", scripted);
                                // Execute the Python code
                                var pyCode = pyEngine.CreateScriptSourceFromString(cmd1.Value);
                                var pyResult = pyCode.Execute(pyScope);
                                // Return the result
                                return pyResult.ToString();
                            }
                        }
                    }
                }
            }
            // Check for Player Location Contents Ancestors Command
            if (playerLocationContents != null)
            {
                foreach (var c in playerLocationContents)
                {
                    var playerLocationContentsAncestors = MUSHDB.GetParentsByThingId(c.Id);
                    if (playerLocationContentsAncestors != null)
                    {
                        for (var i = 0; i < playerLocationContentsAncestors.Count; i++)
                        {
                            var a = playerLocationContentsAncestors[i];
                            cmds = MUSHDB.GetCommandsByThingId(a.Id);
                            if (cmds != null)
                            {
                                var scripted = new Scripted(ThingID, c.Id, cmd, sw, args);
                                pyScope.SetVariable("MUSH", scripted);
                                // Iterate through the commands and check for a match
                                foreach (var cmd1 in cmds)
                                {
                                    if (string.Equals(cmd1.Name, cmd, StringComparison.OrdinalIgnoreCase))
                                    {
                                        // Execute the Python code
                                        var pyCode = pyEngine.CreateScriptSourceFromString(cmd1.Value);
                                        var pyResult = pyCode.Execute(pyScope);
                                        // Return the result
                                        return pyResult.ToString();
                                    }
                                }
                            }
                        }
                    }
                }
            }
            // We found nothing return an error message
            return "Command not found. Type 'help' for a list of commands.";





        }


    }
}