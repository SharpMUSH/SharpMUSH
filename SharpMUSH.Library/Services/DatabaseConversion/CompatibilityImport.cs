using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Softcode;

namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// The compatibility options a database import sets, read from each option's
/// <see cref="SharpConfigAttribute.SetBy"/>, so the importers, <c>@config</c> and the help say the same thing.
/// </summary>
public static class CompatibilityImport
{
	/// <summary>
	/// The options an import from <paramref name="server"/> sets, by option name: a boolean option turned on,
	/// and <c>dialect</c> set to the server's dialect. A new dictionary each call, for the import to add to.
	/// </summary>
	public static Dictionary<string, object> Options(CompatibilityServer server)
	{
		var options = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
		foreach (var property in ConfigMetadata.PropertyNames)
		{
			var metadata = ConfigMetadata.PropertyMetadata[property];
			if (!metadata.SetBy.HasFlag(server) || server == CompatibilityServer.None) continue;

			options[metadata.Name] = property == nameof(CompatibilityOptions.Dialect)
				? DialectOf(server)
				: true;
		}

		return options;
	}

	/// <summary>The <c>dialect</c> an import from <paramref name="server"/> sets.</summary>
	public static string DialectOf(CompatibilityServer server) => server switch
	{
		CompatibilityServer.TinyMUX => SoftcodeDialects.Mux.Key,
		CompatibilityServer.RhostMUSH => SoftcodeDialects.Rhost.Key,
		_ => throw new ArgumentOutOfRangeException(nameof(server), server, "Only a TinyMUX or RhostMUSH import sets a dialect.")
	};
}
