namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// Represents a complete PennMUSH database as read from a database file.
/// </summary>
public class PennMUSHDatabase
{
	/// <summary>
	/// Database version string
	/// </summary>
	public required string Version { get; set; }

	/// <summary>
	/// Database flags and configuration
	/// </summary>
	public Dictionary<string, string> Configuration { get; set; } = [];

	/// <summary>
	/// All objects in the database
	/// </summary>
	public List<PennMUSHObject> Objects { get; set; } = [];

	/// <summary>
	/// The game's object-flag table, from the dump's <c>+FLAGS LIST</c> section.
	/// </summary>
	public List<PennMUSHFlagDefinition> FlagDefinitions { get; set; } = [];

	/// <summary>
	/// The game's power table, from the dump's <c>+POWER LIST</c> section.
	/// </summary>
	public List<PennMUSHFlagDefinition> PowerDefinitions { get; set; } = [];

	/// <summary>
	/// The game's standard-attribute table, from the dump's <c>+ATTRIBUTES LIST</c> section.
	/// </summary>
	public List<PennMUSHAttributeDefinition> AttributeDefinitions { get; set; } = [];

	/// <summary>
	/// The game's maildb, when one was given alongside the dump; empty otherwise.
	/// </summary>
	public PennMUSHMailDatabase Mail { get; set; } = new();

	/// <summary>
	/// God/Wizard player DBRef (usually #1)
	/// </summary>
	public int GodPlayer { get; set; } = 1;

	/// <summary>
	/// Number of records in database
	/// </summary>
	public int RecordCount => Objects.Count;

	/// <summary>
	/// Get an object by its DBRef
	/// </summary>
	public PennMUSHObject? GetObject(int dbref)
	{
		return Objects.FirstOrDefault(o => o.DBRef == dbref);
	}

	/// <summary>
	/// Get all objects of a specific type
	/// </summary>
	public IEnumerable<PennMUSHObject> GetObjectsByType(PennMUSHObjectType type)
	{
		return Objects.Where(o => o.Type == type);
	}
}
