using SharpMUSH.DB.Object;

namespace SharpMUSH.Python
{
    public class ScriptDB
    {
        public int Executor { get; protected set; }
        public int Caller { get; protected set; }

        public ScriptDB(int executor, int caller)
        {
            Executor = executor;
            Caller = caller;
        }

        // GetAttribute(ThingID, AttributeName)
        // Get the value of an attribute
        public string GetAttribute(int thingID, string attributeName)
        {
            // Check if the Executor is the owner of the thing
            // If not check if the Executor has the permission to read the attribute
            if (MUSHDB.IsOwner(Executor, thingID) || MUSHDB.HasPermission(Executor, "ReadAllAttribute"))
            {
                // Get the attribute from the database
                var attribute = MUSHDB.GetAttribute(thingID, attributeName);
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
        // SetAttribute(ThingID, AttributeName, AttributeValue)
        // Set the value of an attribute
        public string SetAttribute(int thingID, string attributeName, string attributeValue)
        {
            // Check if the Executor is the owner of the thing
            // If not check if the Executor has the permission to write the attribute
            if (MUSHDB.IsOwner(Executor, thingID) || MUSHDB.HasPermission(Executor, "WriteAllAttribute"))
            {
                // Update the attribute

                MUSHDB.UpdateOrCreateAttribute(thingID, attributeName, attributeValue);

                return "SET: Attribute: " + attributeName + " on object: " + thingID;
            }
            else
            {
                return "#-1 PERMISSION DENIED";
            }
        }
        // GetObject(ThingID)
        // Get the object from the database

        public string GetName(int thingID)
        {
            // Check if the Executor is the owner of the thing
            // If not check if the Executor has the permission to read the object
            if (MUSHDB.IsOwner(Executor, thingID) || MUSHDB.HasPermission(Executor, "ReadAllObject") || MUSHDB.GetLocationByThingId(thingID) == MUSHDB.GetLocationByThingId(Executor))
            {
                // Get the object from the database
                var thing = MUSHDB.GetThingByThingID(thingID);
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
            if (MUSHDB.HasPermission(Executor, "CreateObject"))
            {
                // Create the object
                var thing = MUSHDB.CreateThing(name, Executor);

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
            if (MUSHDB.HasPermission(Executor, "CreatePlayer"))
            {
                // Create the player
                var thing = MUSHDB.CreatePlayer(name, password, Executor);

                return "CREATED: " + thing.Id + " with name: " + thing.Name;
            }
            else
            {
                return "#-1 PERMISSION DENIED";
            }
        }

        // ChangePassword(ThingID, Password, OldPassword)
        // Change the password of a player

        public string ChangePlayerPassword(int ThingID, string Password, string OldPassword)
        {
            if (MUSHDB.HasPermission(Executor, "Admin") || ThingID == Caller)
            {
                if (MUSHDB.HasPermission(ThingID, "God") && !MUSHDB.HasPermission(Caller, "God"))
                {
                    return "#-1 PERMISSION DENIED";
                }
                else
                {
                    if (MUSHDB.UpdatePassword(ThingID, Password, OldPassword))
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

        // lcon(ThingId)
        // Returns a list of Things

        public List<Thing> Contents(int ThingId)
        {
            // Get contents of ThingId
            if (MUSHDB.HasPermission(Executor, "ReadAllObject") || MUSHDB.IsOwner(Executor, ThingId) || MUSHDB.GetLocationByThingId(ThingId) == MUSHDB.GetLocationByThingId(Executor)
                || MUSHDB.GetLocationByThingId(Executor).Id == ThingId)
            {
                return MUSHDB.GetContentsByThingId(ThingId);
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
            if (MUSHDB.HasPermission(Executor, "SeeDark"))
            {
                return MUSHDB.Priv_GetOnlinePlayers();
            }
            else
            {
                return MUSHDB.GetOnlinePlayers();
            }

        }

        // lowned(ThingId)
        // Returns a list of all owned objects

        public List<Thing> Owned(int ThingId)
        {
            if (MUSHDB.HasPermission(Executor, "ReadAllObject") || MUSHDB.IsOwner(Executor, ThingId))
            {
                return MUSHDB.GetOwnedByThingId(ThingId);
            }
            else
            {
                return new List<Thing>();
            }
        }
    }
}