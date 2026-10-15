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
	/// The game's chatdb, when one was given alongside the dump; empty otherwise.
	/// </summary>
	public PennMUSHChatDatabase Chat { get; set; } = new();

	/// <summary>
	/// The server the database came from. A reader for another server builds this same model, and the
	/// dialect decides which softcode notes the import gives and how its messages name the source.
	/// </summary>
	public DatabaseDialect Dialect { get; set; } = DatabaseDialect.PennMUSH;

	/// <summary>The name of the server that wrote the source, for the import's messages.</summary>
	public string SourceName => Dialect.DisplayName();

	/// <summary>
	/// What the reader decided, or could not carry, while turning the source into this model: names it
	/// renamed, values it could not carry, choices it made, one line each. The import reports them ahead of
	/// its own warnings.
	/// </summary>
	public List<string> ImportNotes { get; set; } = [];

	/// <summary>
	/// Every attribute whose softcode the reader rewrote so SharpMUSH reads it as the source server did, with
	/// its text before and after. The import reports them in a list of their own.
	/// </summary>
	public List<SoftcodeRewrite> SoftcodeRewrites { get; set; } = [];

	/// <summary>
	/// Configuration options, by name, that the source's softcode needs, which the import sets once the
	/// world is written. A <see cref="bool"/> is set as given; a <see cref="uint"/> is a minimum, raised to
	/// and never lowered. PennMUSH softcode writes literal parentheses unescaped and reads @http's status
	/// from q-registers.
	/// </summary>
	public Dictionary<string, object> SourceOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
	{
		["paren_groups"] = true,
		["http_qregisters"] = true
	};

	/// <summary>
	/// Whether the source reads <c>%|</c> and <c>;|</c> as command piping and <c>%m</c> and <c>%=&lt;</c>
	/// as the running command and an attribute or argument, as SharpMUSH does. PennMUSH does not, and the
	/// import reports softcode that uses them.
	/// </summary>
	public bool SharesPipingSubstitutions { get; set; }

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
