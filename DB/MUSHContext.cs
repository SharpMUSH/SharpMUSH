using Microsoft.EntityFrameworkCore;
using SharpMUSH.DB.Link;
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
            #region Thing
            #region Basic
            /* Thing */
            modelBuilder.Entity<Thing>()
                .HasDiscriminator<string>("type")
                .HasValue<Player>("player")
                .HasValue<Thing>("thing")
                .HasValue<Room>("room")
                .HasValue<Exit>("exit");

            // modelBuilder.Entity<Thing>().Property(t => t.Name).IsRequired(true);
            // //modelBuilder.Entity<Thing>().Property(t => t.Location).IsRequired(false);
            // //modelBuilder.Entity<Thing>().Property(t => t.Contents).IsRequired(false);
            //// modelBuilder.Entity<Thing>().Property(t => t.Parents).IsRequired(false);
            // //modelBuilder.Entity<Thing>().Property(t => t.Children).IsRequired(false);
            // modelBuilder.Entity<Thing>().Property(t => t.Flags).IsRequired(false);
            // modelBuilder.Entity<Thing>().Property(t => t.Permissions).IsRequired(false);
            // modelBuilder.Entity<Thing>().Property(t => t.Owner).IsRequired(true);
            // Relations
            modelBuilder.Entity<Thing>().HasOne(t => t.Location).WithOne().HasForeignKey<Thing>(t => t.LocationId);
            modelBuilder.Entity<Thing>().HasMany(t => t.Contents).WithOne(t => t.Location).HasForeignKey(t => t.LocationId);
            modelBuilder.Entity<Thing>().HasIndex(u => u.Name).IsUnique(false);
            modelBuilder
                .Entity<Thing>()
                .HasMany(t => t.Children)
                .WithMany(f => f.Parents)
                .UsingEntity(j => j.ToTable("AncestorLink"));
            modelBuilder.Entity<Thing>()
                .HasMany(m => m.Permissions)
                .WithMany(p => p.Things);

            // Use ThingFlag class to create a Many to Many relationship between Flags and Permissions
            modelBuilder
                .Entity<Thing>()
                .HasMany(f => f.Flags)
                .WithMany(p => p.Things)
                .UsingEntity<ThingFlag>(
            j => j
                .HasOne(fp => fp.Flag)
                .WithMany(p => p.ThingFlags)
                .HasForeignKey(fp => fp.FlagId),
            j => j
                .HasOne(fp => fp.Thing)
                .WithMany(f => f.ThingFlags)
                .HasForeignKey(fp => fp.ThingId),
            j =>
            {
                j.Property(fp => fp.FlagId).HasColumnName("FlagId");
                j.Property(fp => fp.ThingId).HasColumnName("ThingId");
            });


            #endregion Basic

            #region Player
            /* Player */
            modelBuilder.Entity<Player>().Property(p => p.Name).IsRequired(true);
            modelBuilder.Entity<Player>().Property(p => p.Password).IsRequired(true);
            modelBuilder.Entity<Player>().Property(p => p.Salt).IsRequired(true);
            modelBuilder.Entity<Player>().Property(p => p.LastOn).IsRequired(false);
            modelBuilder.Entity<Player>().Property(p => p.LastHost).IsRequired(false);

            modelBuilder.Entity<Player>().HasMany(p => p.Owned).WithOne(t => t.Owner).HasForeignKey(t => t.OwnerId);
            modelBuilder.Entity<Player>().HasIndex(u => u.Name).IsUnique();
            modelBuilder.Entity<Player>().HasOne(e => e.Editing).WithOne().HasForeignKey<Player>(e => e.EditingId);
            #endregion Player

            #region Room
            /* Room */
            modelBuilder.Entity<Room>().Property(r => r.Name).IsRequired(true);

            //modelBuilder.Entity<Room>().HasMany(r => r.Exits).WithOne(e => e.Location).HasForeignKey(e => e.LocationId);
            modelBuilder.Entity<Room>().HasMany(r => r.Entrances).WithOne(e => e.Destination).HasForeignKey(e => e.DestinationId);
            #endregion Room

            #region Exit
            /* Exit */
            modelBuilder.Entity<Exit>().Property(e => e.Name).IsRequired(true);
            //modelBuilder.Entity<Exit>().Property(e => e.Destination).IsRequired(false);
            modelBuilder.Entity<Exit>().Property(e => e.DestinationId).IsRequired(false);
            #endregion Exit


            #endregion Thing

            #region Attribute
            modelBuilder.Entity<Attrib>().Property(a => a.Name).IsRequired(true);
            modelBuilder.Entity<Attrib>().Property(a => a.Value).IsRequired(true);
            modelBuilder.Entity<Attrib>().Property(a => a.Thing).IsRequired(false);
            modelBuilder.Entity<Attrib>().Property(a => a.ThingId).IsRequired(false);
            // Unique key for Attrib on Name and ThingId Columns
            modelBuilder.Entity<Attrib>().HasIndex(a => new { a.Name, a.ThingId }).IsUnique();
            // Many to Many Flag relationship
            modelBuilder
                .Entity<Attrib>()
                .HasMany(a => a.Flags)
                .WithMany(f => f.Attributes)
                .UsingEntity(j => j.ToTable("AttribFlagsLink"));
            // Many to Many Permission relationship
            modelBuilder
                .Entity<Attrib>()
                .HasMany(a => a.Permissions)
                .WithMany(p => p.Attributes).UsingEntity(j => j.ToTable("AttribPermsLink"));


            #endregion Attribute

            #region PermissionSet
            #region Flag
            modelBuilder.Entity<Flag>().Property(f => f.Name).IsRequired(true);
            // Unique key for Flag on Name Column
            modelBuilder.Entity<Flag>().HasIndex(f => f.Name).IsUnique();

            // Use FlagPermission class to create a Many to Many relationship between Flags and Permissions
            modelBuilder
                .Entity<Flag>()
                .HasMany(f => f.Permissions)
                .WithMany(p => p.Flags)
                .UsingEntity<FlagPermission>(
                    j => j
                        .HasOne(fp => fp.Permission)
                        .WithMany(p => p.FlagPermissions)
                        .HasForeignKey(fp => fp.PermissionId),
                    j => j
                        .HasOne(fp => fp.Flag)
                        .WithMany(f => f.FlagPermissions)
                        .HasForeignKey(fp => fp.FlagId),
                    j =>
                    {
                        j.Property(fp => fp.FlagId).HasColumnName("FlagId");
                        j.Property(fp => fp.PermissionId).HasColumnName("PermissionId");
                    });


            #endregion Flag

            #region Permission
            modelBuilder.Entity<Permission>().Property(p => p.Name).IsRequired(true);
            modelBuilder.Entity<Permission>().Property(p => p.Description).IsRequired(false);
            // Unique key for Permission on Name Column
            modelBuilder.Entity<Permission>().HasIndex(p => p.Name).IsUnique();
            #endregion Permission
            #endregion PermissionSet


            #region Seed
            // Seed Data

            modelBuilder.Entity<Room>().HasData(new Room { Id = 1, Name = "Welcome Room" });
            // Seed Attrib DESCRIBE for Thing 1

            modelBuilder.Entity<Attrib>().HasData(new Attrib { Id = 1, Name = "Describe", Value = "Welcome to the MUSH, there isn't much to see right now but more will be added soon!", ThingId = 1 }

            );







            var salt = MUSHDatabase.GenerateShaSalt();
            var password = MUSHDatabase.SecureHash("password", salt);
            modelBuilder.Entity<Player>().HasData(new Player { Id = 2, Name = "Admin", Password = password, Salt = salt, LocationId = 1 });
            modelBuilder.Entity<Permission>().HasData(
                new Permission { Id = 1, Name = "Default", Description = "Default Permissions" },
                new Permission { Id = 2, Name = "Admin", Description = "Admin Permissions" },
                new Permission { Id = 3, Name = "Attrib.View", Description = "See All Attributes" },
                new Permission { Id = 4, Name = "Attrib.Edit", Description = "Edit All Attributes" },
                new Permission { Id = 5, Name = "Thing.Create", Description = "Create Objects" },
                new Permission { Id = 6, Name = "Thing.View", Description = "See All Objects" },
                new Permission { Id = 7, Name = "Thing.Edit", Description = "Edit All Objects" },
                new Permission { Id = 8, Name = "Thing.Destroy", Description = "Destroy All Objects" },
                new Permission { Id = 9, Name = "Thing.Move", Description = "Move All Objects" },
                new Permission { Id = 10, Name = "Exit.Link", Description = "Link All Objects" },
                new Permission { Id = 11, Name = "Exit.Unlink", Description = "Unlink All Objects" },
                new Permission { Id = 12, Name = "Player.Create", Description = "Create Players" },
                new Permission { Id = 13, Name = "Player.View", Description = "See All Players" },
                new Permission { Id = 14, Name = "Player.Edit", Description = "Edit All Players" },
                new Permission { Id = 15, Name = "Player.Destroy", Description = "Destroy All Players" },
                new Permission { Id = 16, Name = "Player.Move", Description = "Move All Players" },
                new Permission { Id = 17, Name = "Player.Link", Description = "Link All Players" },
                new Permission { Id = 18, Name = "Player.Unlink", Description = "Unlink All Players" },
                new Permission { Id = 19, Name = "Room.Create", Description = "Create Rooms" },
                new Permission { Id = 20, Name = "Room.View", Description = "See All Rooms" },
                new Permission { Id = 21, Name = "Room.Edit", Description = "Edit All Rooms" },
                new Permission { Id = 22, Name = "Room.Destroy", Description = "Destroy All Rooms" },
                new Permission { Id = 23, Name = "Perm.Set", Description = "Set Permissions" },
                new Permission { Id = 24, Name = "Perm.Create", Description = "Create Permissions" },
                new Permission { Id = 25, Name = "Perm.Remove", Description = "Remove Permissions" },
                new Permission { Id = 26, Name = "Flag.Set", Description = "Set Flags" },
                new Permission { Id = 27, Name = "Flag.Create", Description = "Create Flags" },
                new Permission { Id = 28, Name = "Flag.Remove", Description = "Remove Flags" }
                );
            // Seed Flags with Default
            // Seed Flags with Admin
            // Admin Flag has all Permissions
            modelBuilder.Entity<Flag>().HasData(
                new Flag { Id = 1, Name = "Default" },
                new Flag { Id = 2, Name = "Admin" }
                );
            modelBuilder.Entity<FlagPermission>().HasData(
                new FlagPermission { FlagId = 2, PermissionId = 1 },
                new FlagPermission { FlagId = 2, PermissionId = 2 },
                new FlagPermission { FlagId = 2, PermissionId = 3 },
                new FlagPermission { FlagId = 2, PermissionId = 4 },
                new FlagPermission { FlagId = 2, PermissionId = 5 },
                new FlagPermission { FlagId = 2, PermissionId = 6 },
                new FlagPermission { FlagId = 2, PermissionId = 7 },
                new FlagPermission { FlagId = 2, PermissionId = 8 },
                new FlagPermission { FlagId = 2, PermissionId = 9 },
                new FlagPermission { FlagId = 2, PermissionId = 10 },
                new FlagPermission { FlagId = 2, PermissionId = 11 },
                new FlagPermission { FlagId = 2, PermissionId = 12 },
                new FlagPermission { FlagId = 2, PermissionId = 13 },
                new FlagPermission { FlagId = 2, PermissionId = 14 },
                new FlagPermission { FlagId = 2, PermissionId = 15 },
                new FlagPermission { FlagId = 2, PermissionId = 16 },
                new FlagPermission { FlagId = 2, PermissionId = 17 },
                new FlagPermission { FlagId = 2, PermissionId = 18 },
                new FlagPermission { FlagId = 2, PermissionId = 19 },
                new FlagPermission { FlagId = 2, PermissionId = 20 },
                new FlagPermission { FlagId = 2, PermissionId = 21 },
                new FlagPermission { FlagId = 2, PermissionId = 22 },
                new FlagPermission { FlagId = 2, PermissionId = 23 },
                new FlagPermission { FlagId = 2, PermissionId = 24 },
                new FlagPermission { FlagId = 2, PermissionId = 25 },
                new FlagPermission { FlagId = 2, PermissionId = 26 },
                new FlagPermission { FlagId = 2, PermissionId = 27 },
                new FlagPermission { FlagId = 2, PermissionId = 28 }
                );

            modelBuilder.Entity<ThingFlag>().HasData(
                new ThingFlag { ThingId = 2, FlagId = 2 });








            #endregion Seed


        }
    }
}