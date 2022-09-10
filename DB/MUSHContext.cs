using Microsoft.EntityFrameworkCore;
using SharpMUSH.DB.Object;
using SharpMUSH.DB.ObjectAttribute;
using SharpMUSH.DB.ObjectPerm;

namespace SharpMUSH.DB
{
    public class MUSHContext : DbContext
    {
        public string DbPath { get; }
        public DbSet<Thing> Objects { get; set; }
        public DbSet<Player> Players { get; set; }
        public DbSet<Thing> Things { get; set; }
        public DbSet<Room> Rooms { get; set; }
        public DbSet<Exit> Exits { get; set; }
        public DbSet<Attrib> Attributes { get; set; }
        public DbSet<Command> Commands { get; set; }
        public DbSet<Flag> Flags { get; set; }

        public MUSHContext()
        {
            var folder = Environment.CurrentDirectory;


            DbPath = System.IO.Path.Join(folder, "mush.db");
        }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite($"Data Source={DbPath}");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Thing>()
                .HasDiscriminator<string>("type")
                .HasValue<Player>("player")
                .HasValue<Thing>("thing")
                .HasValue<Room>("room")
                .HasValue<Exit>("exit");
            modelBuilder.Entity<Attrib>()
                .HasDiscriminator<string>("type")
                .HasValue<Attrib>("attrib")
                .HasValue<Command>("command");

            // set a unique key on MUSHObj
            modelBuilder.Entity<Thing>()
                .HasIndex(u => u.Name)
                .IsUnique();

            // set a unique key on FlagType
            modelBuilder.Entity<Flag>()
                .HasIndex(u => u.Name)
                .IsUnique();


            // set unique key on ObjectPerm
            modelBuilder.Entity<Permission>()
                .HasIndex(p => p.Name)
                .IsUnique();

            /*----------------------------------------------------
             * RELATIONSHIPS
             -----------------------------------------------------*/


            // Many to Many relationship for MUSHObj and ObjectPerm
            modelBuilder.Entity<Thing>()
                .HasMany(m => m.Permissions)
                .WithMany(p => p.Things);

            // Many to Many relationship for ObjectPerm and FlagType
            modelBuilder.Entity<Permission>()
                .HasMany(p => p.Flags)
                .WithMany(f => f.Permissions);
            // Many to Many relationship for ObjectAttribute and MUSHObj
            modelBuilder.Entity<Attrib>()
            .HasDiscriminator<string>("type")
            .HasValue<Attrib>("attribute")
            .HasValue<Command>("command");


            // Many to many relationship for Flags and Objects
            modelBuilder
                .Entity<Thing>()
                .HasMany(t => t.Flags)
                .WithMany(f => f.Things)
                .UsingEntity(j => j.ToTable("ThingFlagsLink"));

            // Many to many relationship for Objects and Parents
            modelBuilder
                .Entity<Thing>()
                .HasMany(t => t.Children)
                .WithMany(f => f.Parents)
                .UsingEntity(j => j.ToTable("AncestorLink"));


            // Many to many relationship for Commands and Flags
            modelBuilder
                .Entity<Command>()
                .HasMany(t => t.Flags)
                .WithMany(f => f.Commands)
                .UsingEntity(j => j.ToTable("CommandFlagsLink"));



            // One to Many relationship for BaseObject fields location and contents
            modelBuilder.Entity<Thing>()
                .HasOne(t => t.Location)
                .WithMany(t => t.Contents);
            // One to Many relationship for BaseObject fields owner and owned
            modelBuilder.Entity<Player>()
                .HasMany(t => t.Owned)
                .WithOne(t => t.Owner);

        }
    }
}