using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Configuration;

/// <summary>
/// A PennMUSH configuration read into SharpMUSH's, and the lines of it that were not carried over,
/// each with the reason: an <c>include</c> that could not be read, a restriction with no value, one a
/// later line replaced, a <c>restrict_*</c> directive SharpMUSH has nothing for, or a value outside its
/// option's declared range that was clamped to the bound (<see cref="ConfigBounds"/>).
/// </summary>
/// <param name="Named">
/// The configuration names (<c>http_handler</c>, <c>master_room</c>, ...) the file gave a value. Every other
/// option holds its default in <see cref="Options"/>, which is not the same as the file asking for it.
/// </param>
/// <param name="Secrets">
/// The secrets the file gave a value, by secret name (<c>sql.password</c> for <c>sql_password</c>). They are not
/// options: the importer stores them in the secret store, never in the configuration.
/// </param>
public record PennMushConfigImport(SharpMUSHOptions Options, IReadOnlyList<string> Skipped, IReadOnlySet<string> Named,
	IReadOnlyDictionary<string, string> Secrets)
{
	/// <summary>
	/// The <c>mush.cnf</c> directives whose value is a secret, and the secret each one becomes. Names are
	/// <c>SecretDefinition</c> names, which this project cannot reference.
	/// </summary>
	public static readonly IReadOnlyDictionary<string, string> SecretDirectives =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["sql_password"] = "sql.password" };

	/// <summary>Whether the file gave <paramref name="name"/> (a configuration name) a value.</summary>
	public bool Sets(string name) => Named.Contains(name);

	/// <summary>
	/// The imported options as they apply over a running game's <paramref name="current"/> ones: the
	/// <c>http_handler</c>, <c>event_handler</c> and <c>package_manager</c> the file does not name keep the
	/// game's values instead of taking their defaults.
	/// </summary>
	/// <remarks>
	/// Those three name objects in this world, and their defaults name the seeded #7-#9. A PennMUSH
	/// <c>mush.cnf</c> usually names none of them, so taking the defaults pointed a game imported from
	/// PennMUSH — where #8 and #9 are the source's own objects — at whatever those happen to be, and a
	/// game that had moved its handlers back at the seeded ones. Every other option keeps PennMUSH's meaning:
	/// one the file leaves out is the default.
	/// </remarks>
	public SharpMUSHOptions Over(SharpMUSHOptions current)
	{
		var database = Options.Database;
		return Options with
		{
			Database = database with
			{
				HttpHandler = Kept(nameof(DatabaseOptions.HttpHandler), database.HttpHandler, current.Database.HttpHandler),
				EventHandler = Kept(nameof(DatabaseOptions.EventHandler), database.EventHandler, current.Database.EventHandler),
				PackageManager = Kept(nameof(DatabaseOptions.PackageManager), database.PackageManager, current.Database.PackageManager)
			}
		};

		uint? Kept(string property, uint? imported, uint? running) =>
			Sets(ConfigMetadata.PropertyToAttributeName[property]) ? imported : running;
	}
}
