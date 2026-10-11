using System.Security.Cryptography;
using System.Text.Json;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Generated;

namespace SharpMUSH.CodeAnalysis.Catalog;

/// <summary>Generated offline metadata only: no world startup, registry enumeration or evaluation.</summary>
public static class BuiltInAnalysisCatalog
{
	public static AnalysisCatalogSnapshot CreateSnapshot()
	{
		var aliases = AliasOptions.Default;
		var entries = new List<AnalysisCatalogEntry>();
		foreach (var (name, attribute) in FunctionLibrary.CreateMetadata())
			entries.Add(new AnalysisCatalogEntry(name, "function", "built-in", attribute.MinArgs,
				attribute.MaxArgs == int.MaxValue ? null : attribute.MaxArgs,
				aliases.FunctionAliases.GetValueOrDefault(name) ?? [], attribute.ParameterNames,
				[], attribute.Flags.ToString(), attribute.Restrict));
		foreach (var (name, attribute) in CommandLibrary.CreateMetadata())
			entries.Add(new AnalysisCatalogEntry(name, "command", "built-in", attribute.MinArgs,
				attribute.MaxArgs == int.MaxValue ? null : attribute.MaxArgs,
				aliases.CommandAliases.GetValueOrDefault(name) ?? [], attribute.ParameterNames,
				attribute.Switches ?? [], attribute.Behavior.ToString(),
				string.IsNullOrEmpty(attribute.CommandLock) ? [] : [attribute.CommandLock]));
		var ordered = entries.OrderBy(entry => entry.Kind, StringComparer.Ordinal)
			.ThenBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
		// Content addressing avoids reusing a revision when a generated name, alias or flag changes.
		var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(ordered)));
		return new AnalysisCatalogSnapshot("generated-built-ins-v1", revision, "offline", ordered);
	}
}
