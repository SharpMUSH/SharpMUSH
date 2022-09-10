using Microsoft.EntityFrameworkCore;
using SharpMUSH.DB;
using SharpMUSH.DB.Object;
using SharpMUSH.DB.ObjectAttribute;
using SharpMUSH.DB.ObjectPerm;
using System.Security.Cryptography;
using System.Text;

namespace SharpMUSH
{

    public static class MUSHDB
    {


        public static void ClearConnectedUsers()
        {
            using (var Context = new MUSHContext())
            {
                foreach (var user in Context.Players)
                {
                    user.Connected = false;
                    user.Session = Guid.Empty;
                    Context.Update(user);
                }

                Context.SaveChanges();
            }
        }

        public static bool SetUserGuid(int Id, Guid guid)
        {
            try
            {
                using (var Context = new MUSHContext())
                {
                    var user = Context.Players.Find(Id);
                    if (user != null)
                    {
                        user.Session = guid;
                        user.Connected = true;
                        Context.Update(user);
                        Context.SaveChanges();
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        internal static Attrib GetAttribute(int thingID, string attributeName)
        {
            using (var Context = new MUSHContext())
            {
                var thing = Context.Things.Find(thingID);
                if (thing != null)
                {
                    foreach (var attribute in thing.Attributes)
                    {
                        if (attribute.Name == attributeName)
                        {
                            return attribute;
                        }
                    }
                }
            }

            return null;
        }

        internal static void UpdateOrCreateAttribute(int thingID, string attributeName, string attributeValue)
        {
            using (var Context = new MUSHContext())
            {
                var thing = Context.Things.Find(thingID);
                if (thing != null)
                {
                    foreach (var attribute in thing.Attributes)
                    {
                        if (attribute.Name == attributeName)
                        {
                            attribute.Value = attributeValue;
                            Context.Update(attribute);
                            Context.SaveChanges();
                            return;
                        }
                    }

                    var newAttribute = new Attrib()
                    {
                        Name = attributeName,
                        Value = attributeValue,
                        Thing = thing
                    };

                    Context.Add(newAttribute);
                    Context.SaveChanges();
                }
            }
        }

        internal static Thing GetThingByThingID(int thingID)
        {
            using (var Context = new MUSHContext())
            {
                var thing = Context.Things.Find(thingID);
                if (thing != null)
                {
                    return thing;
                }
            }

            return null;
        }

        internal static Thing CreateThing(string name, int executor)
        {
            try
            {
                using (var Context = new MUSHContext())
                {
                    var Creator = Context.Players.Find(executor);
                    if (Creator != null)
                    {
                        var newThing = new Thing()
                        {
                            Name = name,
                            Location = Creator,
                            Owner = Creator,
                            Contents = new List<Thing>(),
                            Parents = new List<Thing>(),
                            Children = new List<Thing>(),
                            Flags = new List<Flag>(),
                            Permissions = new List<Permission>(),
                            Attributes = new List<Attrib>()
                        };



                        Context.Add(newThing);
                        Context.SaveChanges();
                        return newThing;
                    }

                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        // GenerateShaSalt() - Generates a random salt for use in password hashing
        // Returns: string - The salt
        public static string GenerateShaSalt()
        {
            var rng = new RNGCryptoServiceProvider();
            var buff = new byte[32];
            rng.GetBytes(buff);
            return Convert.ToBase64String(buff);
        }
        internal static bool UpdatePassword(int thingID, string password, string oldPassword)
        {
            try
            {
                using (var Context = new MUSHContext())
                {
                    var newHash = "";
                    var oldHash = "";
                    var newSalt = GenerateShaSalt();
                    // Get the Player object
                    var player = Context.Players.Find(thingID);
                    // SecureHash new password
                    if (player != null)
                    {
                        newHash = SecureHash(password, newSalt);
                        oldHash = SecureHash(password, player.Salt);

                        if (player.Password == oldHash)
                        {
                            player.Password = newHash;
                            player.Salt = newSalt;
                            Context.Update(player);
                            Context.SaveChanges();
                            return true;
                        }

                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        internal static List<Player> GetOnlinePlayers()
        {
            using (var Context = new MUSHContext())
            {
                var players = Context.Players.Where(p => p.Connected && p.Flags.Where(f => f.Name == "Dark" || f.Name == "Hide") == null).ToList();
                return players;
            }
        }

        internal static List<Thing> GetOwnedByThingId(int thingId)
        {
            // Get all objects where the owner is thingId

            using (var Context = new MUSHContext())
            {
                return Context.Objects.Where(o => o.Owner.Id == thingId).ToList();
            }
        }

        internal static List<Player> Priv_GetOnlinePlayers()
        {
            using (var Context = new MUSHContext())
            {
                var players = Context.Players.Where(p => p.Connected).ToList();
                return players;
            }
        }



        // SecureHash(string password, string salt)
        //
        // This function takes a password and a salt and returns a SHA256 hash of the password and salt.
        // This is used to store passwords in the database.
        internal static string SecureHash(string password, string salt)
        {
            var sha256 = SHA256.Create();
            var saltedPassword = password + salt;
            var saltedPasswordBytes = Encoding.UTF8.GetBytes(saltedPassword);
            var saltedPasswordHash = sha256.ComputeHash(saltedPasswordBytes);
            var saltedPasswordHashString = Convert.ToBase64String(saltedPasswordHash);
            return saltedPasswordHashString;
        }
        internal static Player CreatePlayer(string name, string password, int executor)
        {
            try
            {
                using (var Context = new MUSHContext())
                {
                    var Creator = Context.Players.Find(executor);

                    if (Creator != null)
                    {
                        var newPlayer = new Player()
                        {
                            Name = name,
                            Password = SecureHash(password, name),
                            Connected = false,
                            Session = Guid.Empty,
                            Location = Creator,
                            Owner = Creator,
                            Contents = new List<Thing>(),
                            Parents = new List<Thing>(),
                            Children = new List<Thing>(),
                            Flags = new List<Flag>(),
                            Permissions = new List<Permission>(),
                            Attributes = new List<Attrib>()
                        };
                        Context.Players.Add(newPlayer);
                        Context.SaveChanges();
                        return newPlayer;

                    }
                    return null;

                }
            }
            catch
            {
                return null;
            }
        }


        internal static bool IsOwner(int executor, int thingID)
        {
            using (var Context = new MUSHContext())
            {
                var thing = Context.Things.Find(thingID);
                if (thing != null && thing.Owner.Id == executor)
                {
                    return true;
                }
            }

            return false;
        }

        internal static List<Player> GetPlayersInLocationById(int roomId)
        {

            using (var Context = new MUSHContext())
            {

                var players = Context.Players.Where(p => p.Location.Id == roomId);
                if (players != null)
                {
                    return players.ToList();
                }
                return new List<Player>();
            }
        }

        public static void SetUserDisconnected(int Id)
        {


            using (var Context = new MUSHContext())
            {
                var user = Context.Players.Find(Id);
                if (user != null)
                {

                    user.Session = Guid.Empty;
                    user.Connected = false;
                    Context.Update<Player>(user);
                    Context.SaveChanges();

                }
            }


        }

        internal static List<Command> GetCommandsByThingId(int ThingId)
        {
            using (var Context = new MUSHContext())
            {
                var commands = Context.Commands.Where(c => c.Thing.Id == ThingId);
                if (commands != null)
                {
                    return commands.ToList();
                }
                return new List<Command>();
            }
        }

        internal static Player GetPlayerById(int thingID)
        {
            using (var Context = new MUSHContext())
            {
                var player = Context.Players.Find(thingID);
                return player;
            }
        }

        public static bool HasPermission(int executor, string v)
        {
            using (var Context = new MUSHContext())
            {
                // Get the object from the database
                var obj = Context.Objects.Include(p => p.Permissions).Include(f => f.Flags).FirstOrDefault(e => e.Id == executor);
                // Does the obj exist?
                if (obj != null && obj.Permissions.Where(p => p.Name == v) != null)
                {
                    return true;
                }


                foreach (var flag in obj.Flags)
                {
                    if (flag.Permissions.Where(p => p.Name == v) != null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public static bool HasFlag(int executor, string v)
        {
            using (var Context = new MUSHContext())
            {
                // Get the object from the database
                var obj = Context.Objects.Include(f => f.Flags).FirstOrDefault(e => e.Id == executor);
                // Does the obj exist?
                if (obj != null && obj.Flags.Where(f => f.Name == v) != null)
                {
                    return true;
                }
                return false;
            }
        }

        public static bool HasAttribute(int executor, string v)
        {
            using (var Context = new MUSHContext())
            {
                // Get the object from the database
                var obj = Context.Objects.Include(a => a.Attributes).FirstOrDefault(e => e.Id == executor);
                // Does the obj exist?
                if (obj != null && obj.Attributes.Where(a => a.Name == v) != null)
                {
                    return true;
                }
                return false;
            }
        }

        internal static List<Thing> GetParentsByThingId(int thingID)
        {
            using (var Context = new MUSHContext())
            {
                // Get the object from the database
                var obj = Context.Objects.Include(p => p.Parents).Where(e => e.Id == thingID).FirstOrDefault();
                // Does the obj exist?
                if (obj != null)
                {
                    return obj.Parents.ToList();
                }
                return new List<Thing>();
            }
        }

        internal static List<Thing> GetContentsByThingId(int thingID)
        {
            using (var Context = new MUSHContext())
            {
                // Get the object from the database
                var obj = Context.Objects.Include(c => c.Contents).Where(e => e.Id == thingID).FirstOrDefault();
                // Does the obj exist?
                if (obj != null)
                {
                    return obj.Contents.ToList();
                }
                return new List<Thing>();
            }

        }

        internal static Thing GetLocationByThingId(int thingID)
        {
            using (var Context = new MUSHContext())
            {
                // Get the object from the database
                var obj = Context.Objects.Include(l => l.Location).Where(e => e.Id == thingID).FirstOrDefault();

                return obj.Location;

            }
        }
    }
}
