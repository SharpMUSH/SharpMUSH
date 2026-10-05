namespace SharpMUSH.Database.Seed;

/// <summary>
/// The ten initial objects (#0-#9) a new world is seeded with: the object properties plus their
/// location, home, owner, flags and roles, one record per object.
/// </summary>
public static class InitialObjectSeed
{
	/// <summary>
	/// One seeded object. <see cref="Location"/> and <see cref="Home"/> are <c>null</c> for rooms (a room
	/// has no location/home edges — it is a pure attribute holder).
	/// <see cref="Roles"/> are role slugs assigned to the object; <c>wizard</c> is what PennMUSH seeds as
	/// the WIZARD flag.
	/// </summary>
	public sealed record SeedObject(long Dbref, string Name, string Type, long? Location, long? Home, long Owner, string[] Flags, string[] Roles, long Quota);

	public static readonly SeedObject[] Objects =
	[
		new(0, "Room Zero", "ROOM", null, null, 1, [], [], 0),
		new(1, "God", "PLAYER", 0, 0, 1, [], ["wizard"], 999999),
		new(2, "Master Room", "ROOM", null, null, 1, [], [], 0),
		new(3, "Ancestor Room", "ROOM", null, null, 1, [], [], 0),
		new(4, "Ancestor Player", "THING", 2, 2, 1, [], [], 0),
		new(5, "Ancestor Exit", "THING", 2, 2, 1, [], [], 0),
		new(6, "Ancestor Thing", "THING", 2, 2, 1, [], [], 0),
		new(7, "Package Manager", "PLAYER", 0, 0, 7, [], ["wizard"], 999999),
		new(8, "HTTP Handler", "THING", 2, 2, 1, [], ["wizard"], 0),
		new(9, "Event Handler", "THING", 2, 2, 1, [], ["wizard"], 0),
	];
}
