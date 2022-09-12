using SharpMUSH.DB.Object;
using SharpMUSH.DB.ObjectAttribute;

namespace SharpMUSH.Python
{
    public class ScriptDB
    {
        private MUSHDatabase DB = new MUSHDatabase();


        public int Executor { get; protected set; }
        public int Caller { get; protected set; }

        public ScriptDB(int executor, int caller)
        {
            Executor = executor;
            Caller = caller;
        }

        // GetAttribute(Id, AttributeName)
        // Get the value of an attribute
        public string GetAttribute(int Id, string attributeName)
        {
            // Check if the Executor is the owner of the thing
            // If not check if the Executor has the permission to read the attribute
            if (DB.IsOwner(Executor, Id) || DB.HasPermission(Executor, "ReadAllAttribute"))
            {
                // Get the attribute from the database
                var attribute = DB.GetAttribute(Id, attributeName);
                if (attribute != null)
                {
                    return attribute.Value;
                }
                else
                {
                    return "#-1 ATTRIBUTE NOT FOUND";
                }
            }
            else
            {
                return "#-1 PERMISSION DENIED";
            }
        }

        // SetPlayerEditObject(MUSH.Caller, CmdObj, CmdName, CmdCommand)

        public void SetPlayerEditObject(int playerId, int Id, string name, string command)
        {
            DB.UpdateAttribute(playerId, "Editing", Id.ToString());
            DB.UpdateAttribute(playerId, "EditingName", name);
            DB.UpdateAttribute(playerId, "EditingCommand", command);
        }


        // GetCommand(Id, CommandName)
        // get a Command
        public Attrib? GetCommand(int Id, string Name)
        {
            // Check if the Executor is the owner of the thing
            // If not check if the Executor has the permission to read the attribute
            if (DB.IsOwner(Executor, Id) || DB.HasPermission(Executor, "ReadAllAttribute"))
            {
                // Get the attribute from the database
                var attribute = DB.GetAttribute(Id, Name);
                if (attribute != null)
                {
                    return attribute;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        // SetCommand(Id, CommandName, Value)
        // Set a command

        public bool SetCommand(int Id, string Name, string Value, string command)
        {
            // Check if the Executor is the owner of the thing
            // If not check if the Executor has the permission to read the attribute
            if (DB.IsOwner(Executor, Id) || DB.HasPermission(Executor, "WriteAllAttribute"))
            {
                // Get the attribute from the database
                var attribute = DB.GetAttribute(Id, Name);
                if (attribute != null)
                {
                    attribute.Command = command;
                    attribute.IsCMD = true;
                    attribute.Value = Value;
                    DB.UpdateAttribute(attribute);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

        }


        public void SetPlayerEditMode(int Id, bool edit)
        {
            DB.SetPlayerEditMode(Id, edit);

        }

        // SetAttribute(Id, AttributeName, AttributeValue)
        // Set the value of an attribute
        public bool SetAttribute(int Id, string attributeName, string attributeValue)
        {
            // Check if the Executor is the owner of the thing
            // If not check if the Executor has the permission to write the attribute
            if (DB.IsOwner(Executor, Id) || DB.HasPermission(Executor, "WriteAllAttribute"))
            {
                // Update the attribute

                DB.UpdateAttribute(Id, attributeName, attributeValue);

                return true;
            }
            else
            {
                return false;
            }
        }
        // GetObject(Id)
        // Get the object from the database

        public string GetName(int Id)
        {
            // Check if the Executor is the owner of the thing
            // If not check if the Executor has the permission to read the object
            if (DB.IsOwner(Executor, Id) || DB.HasPermission(Executor, "ReadAllObject") || DB.GetLocationById(Id) == DB.GetLocationById(Executor))
            {
                // Get the object from the database
                var thing = DB.GetThingById(Id);
                if (thing != null)
                {
                    return thing.Name;
                }
                else
                {
                    return "#-1 OBJECT NOT FOUND";
                }
            }
            else
            {
                return "#-1 OBJECT NOT FOUND";
            }
        }


        // CreateObject(Name)
        // Create a new object in the database
        public string CreateObject(string name)
        {
            // Check if the Executor has the permission to create an object
            if (DB.HasPermission(Executor, "CreateObject"))
            {
                // Create the object
                var thing = DB.CreateThing(name, Executor);

                return "CREATED: " + thing.Id + " with name: " + thing.Name;
            }
            else
            {
                return "#-1 PERMISSION DENIED";
            }
        }

        // CreatePlayer(Name, Password)
        // Create a new Player

        public string CreatePlayer(string name, string password)
        {
            // Check if the Executor has the permission to create a player
            if (DB.HasPermission(Executor, "CreatePlayer"))
            {
                // Create the player
                var thing = DB.CreatePlayer(name, password, Executor);

                return "CREATED: " + thing.Id + " with name: " + thing.Name;
            }
            else
            {
                return "#-1 PERMISSION DENIED";
            }
        }

        // ChangePassword(Id, Password, OldPassword)
        // Change the password of a player

        public string ChangePlayerPassword(int Id, string Password, string OldPassword)
        {
            if (DB.HasPermission(Executor, "Admin") || Id == Caller)
            {
                if (DB.HasPermission(Id, "God") && !DB.HasPermission(Caller, "God"))
                {
                    return "#-1 PERMISSION DENIED";
                }
                else
                {
                    if (DB.UpdatePassword(Id, Password, OldPassword))
                    {
                        return "Password Updated.";
                    }
                    else
                    {
                        return "#-1 Password Update Failure.";
                    }

                }
            }
            else
            {
                return "#-1 PERMISSION DENIED";
            }
        }

        // lcon(Id)
        // Returns a list of Things

        public List<Thing> Contents(int Id)
        {
            // Get contents of Id
            if (DB.HasPermission(Executor, "ReadAllObject") || DB.IsOwner(Executor, Id) || DB.GetLocationById(Id) == DB.GetLocationById(Executor)
                || DB.GetLocationById(Executor).Id == Id)
            {
                return DB.GetContentsById(Id);
            }
            else
            {
                return new List<Thing>();
            }
        }

        // lwho()
        // Returns a list of all online players

        public List<Player> Who()
        {
            if (DB.HasPermission(Executor, "SeeDark"))
            {
                return DB.Priv_GetOnlinePlayers();
            }
            else
            {
                return DB.GetOnlinePlayers();
            }

        }

        // lowned(Id)
        // Returns a list of all owned objects

        public List<Thing> Owned(int Id)
        {
            if (DB.HasPermission(Executor, "ReadAllObject") || DB.IsOwner(Executor, Id))
            {
                return DB.GetOwnedById(Id);
            }
            else
            {
                return new List<Thing>();
            }
        }
        public Thing? GetPlayerLocation(int Id)
        {
            var thing = DB.GetLocationById(Id);

            return thing;

        }
    }


}