using System.Collections.Frozen;
using System.Reflection;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Reads the bundled <c>http-hooks</c> package manifest (the single source of
/// truth for the default HTTP handler softcode) and exposes its handler
/// attributes by name. Used by <see cref="DefaultHttpHandlerBootstrapService"/>
/// and by tests that need a canonical attribute value without depending on the
/// old hardcoded C# constants.
/// </summary>
public static class BundledHttpHooks
{
	private const string ResourceName = "SharpMUSH.Server.BundledPackages.http-hooks.package.yaml";

	private static readonly Lazy<FrozenDictionary<string, string>> Lazy = new(Load);

	/// <summary>All handler attribute (name → value) pairs from the bundled manifest.</summary>
	public static FrozenDictionary<string, string> Attributes => Lazy.Value;

	/// <summary>The value of one handler attribute (e.g. <c>POST</c>, <c>GET`PROFILE`SCHEMA</c>).</summary>
	public static string Attribute(string name) => Attributes[name];

	private static FrozenDictionary<string, string> Load()
	{
		using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
			?? throw new InvalidOperationException($"Bundled resource '{ResourceName}' not found.");
		using var reader = new StreamReader(stream);
		var yaml = reader.ReadToEnd();

		var parsed = new PackageManifestService().ParseManifest(yaml);
		if (parsed.IsT1)
		{
			throw new InvalidOperationException(
				$"Bundled http-hooks manifest is invalid: {string.Join("; ", parsed.AsT1.Issues.Select(i => i.ToString()))}");
		}

		// The package has a single attach object (the handler).
		var handler = parsed.AsT0.Manifest.Objects.Single();
		return handler.Attributes.ToFrozenDictionary(
			kv => kv.Key, kv => kv.Value.Value, StringComparer.OrdinalIgnoreCase);
	}
}
