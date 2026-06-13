using System.Collections.Frozen;
using System.Reflection;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Reads the bundled default-handler package manifests (the single source of
/// truth for the default HTTP handler softcode) and exposes their handler
/// attributes by name. Used by <see cref="DefaultHttpHandlerBootstrapService"/>
/// and by tests that need a canonical attribute value without depending on the
/// old hardcoded C# constants.
/// </summary>
public static class BundledHttpHooks
{
	/// <summary>Bundled package ids in dependency order (base first).</summary>
	public static readonly IReadOnlyList<string> PackageIds = ["http-handler", "profile-handler"];

	private static readonly Lazy<FrozenDictionary<string, string>> Lazy = new(LoadAll);

	/// <summary>All default-handler attribute (name → value) pairs, merged across the bundled packages.</summary>
	public static FrozenDictionary<string, string> Attributes => Lazy.Value;

	/// <summary>The value of one handler attribute (e.g. <c>POST</c>, <c>GET`PROFILE`SCHEMA</c>).</summary>
	public static string Attribute(string name) => Attributes[name];

	/// <summary>The raw YAML of one bundled package manifest.</summary>
	public static string ManifestYaml(string packageId)
	{
		var resource = $"SharpMUSH.Server.BundledPackages.{packageId}.package.yaml";
		using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
			?? throw new InvalidOperationException($"Bundled resource '{resource}' not found.");
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}

	private static FrozenDictionary<string, string> LoadAll()
	{
		var service = new PackageManifestService();
		var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		foreach (var packageId in PackageIds)
		{
			var parsed = service.ParseManifest(ManifestYaml(packageId));
			if (parsed.IsT1)
			{
				throw new InvalidOperationException(
					$"Bundled {packageId} manifest is invalid: {string.Join("; ", parsed.AsT1.Issues.Select(i => i.ToString()))}");
			}

			// Each default package has a single attach object (the handler).
			foreach (var (name, attr) in parsed.AsT0.Manifest.Objects.Single().Attributes)
			{
				merged[name] = attr.Value;
			}
		}

		return merged.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
	}
}
