using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Configuration;

/// <summary>
/// The options naming objects in the world (the ancestors, <c>package_manager</c>, <c>http_handler</c>,
/// <c>event_handler</c>) that the last imported <c>mush.cnf</c> gave a value, and the value it gave. Kept as
/// expanded server data beside the options, so a PennMUSH database imported afterwards can tell the source
/// game's own handler — <c>event_handler 9</c> in its <c>mush.cnf</c> — from SharpMUSH's seeded #9 that the
/// default names: the first is the imported object at #9 and stays, the second is removed and unset.
/// </summary>
public sealed class MushCnfObjectReferences
{
	/// <summary>The <see cref="DatabaseOptions"/> properties that name an object.</summary>
	public static readonly IReadOnlyList<string> Properties =
	[
		nameof(DatabaseOptions.AncestorRoom),
		nameof(DatabaseOptions.AncestorExit),
		nameof(DatabaseOptions.AncestorThing),
		nameof(DatabaseOptions.AncestorPlayer),
		nameof(DatabaseOptions.PackageManager),
		nameof(DatabaseOptions.HttpHandler),
		nameof(DatabaseOptions.EventHandler)
	];

	/// <summary>Configuration name (<c>event_handler</c>) to the value the file gave it; null is "no object".</summary>
	public Dictionary<string, uint?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Whether the file named <paramref name="property"/> (a <see cref="DatabaseOptions"/> property) as <paramref name="value"/>.</summary>
	public bool Named(string property, uint? value)
		=> Values.TryGetValue(ConfigMetadata.PropertyToAttributeName[property], out var named) && named == value;

	/// <summary>What <paramref name="import"/>'s file named, read from the options it produced.</summary>
	public static MushCnfObjectReferences From(PennMushConfigImport import)
	{
		var database = import.Options.Database;
		var references = new MushCnfObjectReferences();
		foreach (var property in Properties)
		{
			var name = ConfigMetadata.PropertyToAttributeName[property];
			if (import.Sets(name))
			{
				references.Values[name] = Value(database, property);
			}
		}

		return references;
	}

	/// <summary>The value of one of <see cref="Properties"/> in <paramref name="database"/>.</summary>
	public static uint? Value(DatabaseOptions database, string property) => property switch
	{
		nameof(DatabaseOptions.AncestorRoom) => database.AncestorRoom,
		nameof(DatabaseOptions.AncestorExit) => database.AncestorExit,
		nameof(DatabaseOptions.AncestorThing) => database.AncestorThing,
		nameof(DatabaseOptions.AncestorPlayer) => database.AncestorPlayer,
		nameof(DatabaseOptions.PackageManager) => database.PackageManager,
		nameof(DatabaseOptions.HttpHandler) => database.HttpHandler,
		nameof(DatabaseOptions.EventHandler) => database.EventHandler,
		_ => throw new ArgumentOutOfRangeException(nameof(property), property, "Not an object-reference option.")
	};
}
