using Microsoft.EntityFrameworkCore;

namespace SharpMUSH.DB
{
    public class MUSHContext : DbContext
    {
        public string DbPath { get; }
        public DbSet<UserType> Users { get; set; }
        public DbSet<ThingType> Things { get; set; }
        public DbSet<RoomType> Rooms { get; set; }
        public DbSet<ExitType> Exits { get; set; }
        public DbSet<Attrib> Attributes { get; set; }
        public DbSet<FlagType> Flags { get; set; }

        public MUSHContext()
        {
            var folder = Environment.CurrentDirectory;
            

            DbPath = System.IO.Path.Join(folder, "mush.db");
        }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite($"Data Source={DbPath}");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MUSHObj>()
                .HasDiscriminator<string>("type")
                .HasValue<MUSHObj>("object")
                .HasValue<UserType>("player")
                .HasValue<ThingType>("thing")
                .HasValue<RoomType>("room")
                .HasValue<ExitType>("exit");

            modelBuilder
                .Entity<MUSHObj>()
                .HasMany(t => t.Flags)
                .WithMany(f => f.Things)
                .UsingEntity(j => j.ToTable("ThingFlagsLink"));
            modelBuilder
                .Entity<MUSHObj>()
                .HasMany(t => t.Children)
                .WithMany(f => f.Parents)
                .UsingEntity(j => j.ToTable("AncestorLink"));

            modelBuilder.Entity<MUSHObj>()
                .HasOne(l => l.Location);

            modelBuilder.Entity<MUSHObj>()
                .HasOne(o => o.Owner);
        }
    }
}