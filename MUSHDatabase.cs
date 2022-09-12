using Microsoft.EntityFrameworkCore;
using SharpMUSH.DB;
using SharpMUSH.DB.Object;
using SharpMUSH.DB.ObjectAttribute;
using SharpMUSH.DB.ObjectPerm;
using System.Security.Cryptography;
using System.Text;

namespace SharpMUSH
{

    public class MUSHDatabase
    {
        #region Constructor
        MUSHContext Context;
        // ctor
        public MUSHDatabase()
        {
            // Create the database if it does not exist
            using (var db = new MUSHContext())
            {
                db.Database.EnsureCreated();

            }
            Context = new MUSHContext();
        }
        #endregion


        #region Utility
        public static string SecureHash(string password, string salt)
        {
            var sha256 = SHA256.Create();
            var saltedPassword = password + salt;
            var saltedPasswordBytes = Encoding.UTF8.GetBytes(saltedPassword);
            var saltedPasswordHash = sha256.ComputeHash(saltedPasswordBytes);
            var saltedPasswordHashString = Convert.ToBase64String(saltedPasswordHash);
            return saltedPasswordHashString;
        }

        public void ClearConnectedUsers()
        {

            foreach (var user in Context.Players)
            {
                user.Connected = false;
                user.LastOn = DateTime.Now;

                Context.Update(user);
            }

            Context.SaveChanges();

        }

        public int AuthenticatePlayer(string PlayerName, string Password, Guid guid)
        {
            // Authenticate Player.name and password and return Id


            // Get the player
            var player = Context.Players.FirstOrDefault(p => p.Name == PlayerName);
            // User player password and salt to generate hash
            if (player != null)
            {
                var hash = SecureHash(Password, player.Salt);
                // Compare hash to stored hash
                if (hash == player.Password)
                {
                    Context.Update(player);
                    Context.SaveChanges();
                    return player.Id;
                }
            }
            return 0;

        }
        #endregion


        #region Command
        public bool SetCommand(int Id, string name, string value)
        {
            // Set the command for the thing
            var command = Context.Attributes.FirstOrDefault(c => c.Thing.Id == Id && c.Name == name);
            var thing = Context.Things.FirstOrDefault(t => t.Id == Id);
            if (thing != null)
            {
                if (command == null)
                {
                    command = new Attrib();
                    command.Thing = thing;
                    command.Name = name;
                    command.Value = value;
                    Context.Attributes.Add(command);

                }
                else
                {
                    command.Value = value;
                    Context.Update(command);
                }
                Context.SaveChanges();
                return true;
            }
            else
            {
                return false;
            }
        }

        internal void UpdateAttribute(Attrib attribute)
        {
            Context.Update(attribute);
            Context.SaveChanges();
        }



        #endregion

        internal void SetPlayerEditMode(int Id, bool edit)
        {
            var player = Context.Players.Find(Id);
            if (player != null)
            {
                player.EditMode = edit;
                Context.Update(player);
                Context.SaveChanges();
            }
        }






        public Attrib? GetScript(string name, int Id) => Context
            .Attributes
            .FirstOrDefault(c => c.Name == name && c.Thing.Id == Id && c.IsCMD);

        public bool SetUserGuid(int Id, Guid guid)
        {

            var user = Context.Players.Find(Id);
            if (user != null)
            {
                user.Connected = true;
                Context.Update(user);
                Context.SaveChanges();
                return true;
            }

            return false;
        }

        internal Attrib GetAttribute(int Id, string attributeName)
        {

            var attrib = Context.Attributes.FirstOrDefault(t => t.Thing.Id == Id && t.Name == attributeName);
            if (attrib != null)
            {
                return attrib;

            }


            return null;
        }

        internal void UpdateAttribute(int Id, string attributeName, string attributeValue)
        {
            Attrib attrib = null;
            try
            {
                attrib = Context.Attributes.First(t => t.Thing.Id == Id && t.Name == attributeName);
            }
            catch
            {

            }
            if (attrib != null)
            {

                attrib.Value = attributeValue;
                Context.Update(attrib);
                Context.SaveChanges();
                return;
            }
            else
            {

                var newAttribute = new Attrib()
                {
                    Name = attributeName,
                    Value = attributeValue,
                    Thing = Context.Things.Find(Id)
                };

                Context.Add(newAttribute);
                Context.SaveChanges();
            }

        }







        public Thing CreateThing(string name, int executor)
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


        // GenerateShaSalt() - Generates a random salt for use in password hashing
        // Returns: string - The salt
        public static string GenerateShaSalt()
        {
            var rng = new RNGCryptoServiceProvider();
            var buff = new byte[32];
            rng.GetBytes(buff);
            return Convert.ToBase64String(buff);
        }
        public bool UpdatePassword(int Id, string password, string oldPassword)
        {

            var newHash = "";
            var oldHash = "";
            var newSalt = GenerateShaSalt();
            // Get the Player object
            var player = Context.Players.Find(Id);
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
            return false;
        }
        // Priviliged password update, for forcing password resets
        public bool UpdatePassword(int Id, string password)
        {
            var newHash = "";
            var newSalt = GenerateShaSalt();
            var player = Context.Players.Find(Id);

            if (player != null)
            {
                newHash = SecureHash(password, newSalt);
                player.Password = newHash;
                player.Salt = newSalt;
                Context.Update(player);
                Context.SaveChanges();
                return true;
            }
            return false;
        }




        // SecureHash(string password, string salt)
        //
        // This function takes a password and a salt and returns a SHA256 hash of the password and salt.
        // This is used to store passwords in the database.

        public Player? CreatePlayer(string name, string password, int executor)
        {

            var Creator = Context.Players.Find(executor);

            if (Creator != null)
            {
                var newPlayer = new Player()
                {
                    Name = name,
                    Password = SecureHash(password, name),
                    Connected = false,
                    Session = new[] { Guid.Empty },
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






        public void SetUserDisconnected(int Id)
        {


            var user = Context.Players.Find(Id);
            if (user != null)
            {
                user.Connected = false;
                Context.Update<Player>(user);
                Context.SaveChanges();

            }
        }



        #region ObjectComparatorHelpers
        public bool HasPermission(int executor, string v)
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

        public bool HasFlag(int executor, string v)
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

        public bool HasAttribute(int executor, string v)
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

        public bool IsOwner(int executor, int Id)
        {

            var thing = Context.Things.Find(Id);
            if (thing != null && thing.Owner.Id == executor)
            {
                return true;
            }


            return false;
        }
        #endregion

        #region Getters
        public List<Thing> GetParentsById(int Id)
        {
            using (var Context = new MUSHContext())
            {
                // Get the object from the database
                var obj = Context.Objects.Include(p => p.Parents).Where(e => e.Id == Id).FirstOrDefault();
                // Does the obj exist?
                if (obj != null)
                {
                    return obj.Parents.ToList();
                }
                return new List<Thing>();
            }
        }

        public List<Thing> GetContentsById(int Id)
        {

            // Get the object from the database
            var obj = Context.Objects.Include(c => c.Contents).Where(e => e.Id == Id).FirstOrDefault();
            // Does the obj exist?
            if (obj != null)
            {
                return obj.Contents.ToList();
            }
            return new List<Thing>();


        }

        public List<Attrib> GetCommandsById(int Id)
        {

            var commands = Context.Attributes.Where(c => c.Thing.Id == Id);
            if (commands != null)
            {
                return commands.ToList();
            }
            return new List<Attrib>();
        }

        public Player GetPlayerById(int Id)
        {
            using (var Context = new MUSHContext())
            {
                var player = Context.Players.Find(Id);
                return player;
            }
        }

        public Thing GetLocationById(int Id)
        {

            // Get the object from the database
            var obj = Context.Objects.Include(l => l.Location).Where(e => e.Id == Id).FirstOrDefault();
            if (obj != null)
            {
                return obj.Location;
            }
            return null;


        }

        public Thing GetThingById(int Id)
        {

            var thing = Context.Things.Find(Id);
            if (thing != null)
            {
                return thing;
            }


            return null;
        }

        internal IList<Thing> GetAncestors(int Id)
        {
            // Get the Thing
            var thing = Context.Things.Include(p => p.Parents).First(x => x.Id == Id);
            var Ancestors = new List<Thing>();

            // Get the parents of thing and add to the Ancestor list

            foreach (var parent in thing.Parents)
            {
                Ancestors.Add(parent);
            }

            // Recurse through the parent tree until we get to the top
            foreach (var parent in thing.Parents)
            {
                Ancestors.AddRange(GetAncestors(parent.Id));
            }

            return Ancestors;


        }

        public Thing GetThingByName(string thingName)
        {

            var thing = Context.Things.FirstOrDefault(t => t.Name == thingName);

            return thing;

        }

        public List<Player> GetOnlinePlayers()
        {

            var players = Context.Players.Where(p => p.Connected && p.Flags.Where(f => f.Name == "Dark" || f.Name == "Hide") == null).ToList();
            return players;

        }

        public List<Thing> GetOwnedById(int Id)
        {
            // Get all objects where the owner is Id

            return Context.Objects.Where(o => o.Owner.Id == Id).ToList();

        }

        public List<Player> Priv_GetOnlinePlayers()
        {

            var players = Context.Players.Where(p => p.Connected).ToList();
            return players;

        }

        public List<Player> GetPlayersInLocationById(int roomId)
        {


            var players = Context.Players.Where(p => p.Location.Id == roomId);
            if (players != null)
            {
                return players.ToList();
            }
            return new List<Player>();

        }
        #endregion
    }
}
