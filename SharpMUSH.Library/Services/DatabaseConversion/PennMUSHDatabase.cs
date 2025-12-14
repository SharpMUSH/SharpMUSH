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
