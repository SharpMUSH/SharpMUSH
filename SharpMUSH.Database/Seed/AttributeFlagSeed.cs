namespace SharpMUSH.Database.Seed;

/// <summary>
/// The built-in attribute flags, shared verbatim across all database providers. Copied from
/// <c>SharpMUSH.Database.SurrealDB/SurrealDatabase.Migration.cs</c> (<c>CreateInitialAttributeFlags</c>).
/// </summary>
public static class AttributeFlagSeed
{
	public static readonly (string Name, string Symbol, bool Inheritable)[] Flags =
	[
		("no_command", "$", true),
		("no_inherit", "i", true),
		("no_clone", "c", true),
		("mortal_dark", "m", true),
		("wizard", "w", true),
		("veiled", "V", true),
		("nearby", "n", true),
		("locked", "+", true),
		("safe", "S", true),
		("visual", "v", false),
		("public", "p", false),
		("debug", "b", true),
		("no_debug", "B", true),
		("regexp", "R", false),
		("case", "C", false),
		("nospace", "s", true),
		("noname", "N", true),
		("aahear", "A", false),
		("amhear", "M", false),
		("quiet", "Q", false),
		("branch", "`", false),
		("prefixmatch", "", false),
		("cmdsyntax", "x", true),
		("funsyntax", "f", true),
		("internal", "", true),
		("nodump", "", true),
	];
}
