using System.Globalization;
using System.Text;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Serializes a <see cref="PackageManifest"/> back to <c>package.yaml</c>. The inverse of
/// <see cref="IPackageManifestService.ParseManifest"/>: every field the reader understands is
/// emitted here, and <c>PackageManifestWriterTests</c> round-trips a manifest that exercises all
/// of them.
/// </summary>
/// <remarks>
/// <para>
/// This exists so the schema is described once. Authoring used to emit the schema by hand into a
/// <c>StringBuilder</c>, and that copy knew about a strict subset of what the reader reads —
/// powers, locks, attribute flags, attach targets, locations and destinations all went in one side
/// and never came out the other. A field added to the reader is now a compile error here, not a
/// silent omission at export time.
/// </para>
/// <para>
/// The output is deliberately plain: single-quoted scalars for anything that could be re-parsed as
/// a number, bool or null, and block scalars for multi-line MUSHcode so diffs stay readable.
/// </para>
/// </remarks>
public static class PackageManifestWriter
{
	public static string Write(PackageManifest manifest)
	{
		ArgumentNullException.ThrowIfNull(manifest);

		var yaml = new StringBuilder();
		WriteHeader(yaml, manifest);
		WriteRelations(yaml, "depends", manifest.Dependencies, withSource: true);
		WriteRelations(yaml, "conflicts", manifest.Conflicts, withSource: false);
		WriteConfigure(yaml, manifest.Configure);

		switch (manifest.Kind)
		{
			case PackageKind.Application when manifest.Application is { } application:
				WriteApplication(yaml, application);
				break;
			case PackageKind.Managed when manifest.Binary is { } binary:
				WriteBinaries(yaml, binary);
				break;
			default:
				WriteObjects(yaml, manifest.Objects);
				break;
		}

		return yaml.ToString();
	}

	private static void WriteHeader(StringBuilder yaml, PackageManifest manifest)
	{
		yaml.Append("format: ").AppendLine(manifest.Format.ToString());
		yaml.Append("package: ").AppendLine(Scalar(manifest.Name));
		yaml.Append("version: ").AppendLine(Scalar(manifest.Version.ToString()));

		if (manifest.Kind != PackageKind.Softcode)
		{
			yaml.Append("kind: ").AppendLine(manifest.Kind.ToString().ToLowerInvariant());
		}

		if (manifest.Authors.Count > 0)
		{
			yaml.Append("authors: ").AppendLine(Flow(manifest.Authors));
		}

		yaml.Append("description: ").AppendLine(Scalar(manifest.Description));

		AppendOptional(yaml, "license", manifest.License);
		AppendOptional(yaml, "homepage", manifest.Homepage);

		if (manifest.Keywords.Count > 0)
		{
			yaml.Append("keywords: ").AppendLine(Flow(manifest.Keywords));
		}

		AppendOptional(yaml, "convention_prefix", manifest.ConventionPrefix);

		if (manifest.RequiresServer is { } requiresServer)
		{
			yaml.Append("requires_server: ").AppendLine(Scalar(requiresServer.ToString()));
		}

		AppendOptional(yaml, "replaces", manifest.Replaces);
	}

	private static void WriteRelations(
		StringBuilder yaml, string field, IReadOnlyList<PackageDependencySpec> relations, bool withSource)
	{
		if (relations.Count == 0)
		{
			return;
		}

		yaml.AppendLine();
		yaml.Append(field).AppendLine(":");
		foreach (var relation in relations)
		{
			var source = withSource ? relation.Source : null;

			// The bare-id shorthand only survives a round-trip when there is nothing else to say.
			if (relation.Constraint.Clauses.Count == 0 && source is null)
			{
				yaml.Append("  - ").AppendLine(Scalar(relation.PackageId));
				continue;
			}

			yaml.Append("  - package: ").AppendLine(Scalar(relation.PackageId));
			if (relation.Constraint.Clauses.Count > 0)
			{
				yaml.Append("    version: ").AppendLine(Scalar(relation.Constraint.ToString()));
			}

			if (source is null)
			{
				continue;
			}

			yaml.AppendLine("    source:");
			yaml.Append("      repo: ").AppendLine(Scalar(source.Repo));
			AppendOptional(yaml, "path", source.Path, "      ");
			AppendOptional(yaml, "branch", source.Branch, "      ");
		}
	}

	private static void WriteConfigure(StringBuilder yaml, IReadOnlyDictionary<string, PackageConfigureSpec> configure)
	{
		if (configure.Count == 0)
		{
			return;
		}

		yaml.AppendLine();
		yaml.AppendLine("configure:");
		foreach (var spec in configure.Values.OrderBy(c => c.Key, StringComparer.Ordinal))
		{
			yaml.Append("  ").Append(spec.Key).AppendLine(":");
			yaml.Append("    label: ").AppendLine(Scalar(spec.Label));
			yaml.Append("    type: ").AppendLine(spec.Type.ToString().ToLowerInvariant());
			AppendOptional(yaml, "default", spec.Default, "    ");
		}
	}

	private static void WriteApplication(StringBuilder yaml, PackageApplicationSpec application)
	{
		yaml.AppendLine();
		yaml.AppendLine("application:");
		yaml.Append("  slug: ").AppendLine(Scalar(application.Slug));
		yaml.Append("  display_name: ").AppendLine(Scalar(application.DisplayName));
		AppendOptional(yaml, "icon", application.Icon, "  ");
		yaml.Append("  type: ").AppendLine(application.Kind.ToString().ToLowerInvariant());
		yaml.Append("  schema_url: ").AppendLine(Scalar(application.SchemaUrl));
		AppendOptional(yaml, "data_url", application.DataUrl, "  ");
		AppendOptional(yaml, "submit_route", application.SubmitRoute, "  ");
		yaml.Append("  minimum_role: ").AppendLine(Scalar(application.MinimumRole));
		AppendOptional(yaml, "nav_placement", application.NavPlacement, "  ");

		if (application.Zones.Count > 0)
		{
			yaml.Append("  zones: ").AppendLine(Flow(application.Zones));
		}

		yaml.Append("  order: ").AppendLine(application.Order.ToString(CultureInfo.InvariantCulture));
	}

	private static void WriteBinaries(StringBuilder yaml, PackageBinarySpec binary)
	{
		yaml.AppendLine();
		yaml.AppendLine("binaries:");
		yaml.Append("  min_server_version: ").AppendLine(Scalar(binary.MinServerVersion.ToString()));
		yaml.AppendLine("  files:");
		foreach (var file in binary.Files)
		{
			yaml.Append("    - file: ").AppendLine(Scalar(file.FileName));
			yaml.Append("      sha256: ").AppendLine(Scalar(file.Sha256));
		}
	}

	private static void WriteObjects(StringBuilder yaml, IReadOnlyList<PackageObjectSpec> objects)
	{
		yaml.AppendLine();
		yaml.AppendLine("objects:");
		foreach (var obj in objects)
		{
			yaml.Append("  - ref: ").AppendLine(Scalar(obj.Ref));

			// Attach mode carries a target and attributes only; every creation key is rejected by
			// the reader, so emitting one here would produce a manifest we cannot read back.
			if (obj.Target is { } target)
			{
				yaml.Append("    target: ").AppendLine(Scalar(target.ToString()));
				WriteAttributes(yaml, obj.Attributes);
				continue;
			}

			yaml.Append("    type: ").AppendLine(obj.Type.ToString().ToLowerInvariant());
			yaml.Append("    name: ").AppendLine(Scalar(obj.Name));
			AppendRef(yaml, "parent", obj.Parent);
			AppendRef(yaml, "location", obj.Location);
			AppendRef(yaml, "destination", obj.Destination);

			if (obj.PreviousRefs.Count > 0)
			{
				yaml.Append("    previous_refs: ").AppendLine(Flow(obj.PreviousRefs));
			}

			if (obj.Flags.Count > 0)
			{
				yaml.Append("    flags: ").AppendLine(Flow(obj.Flags));
			}

			if (obj.Powers.Count > 0)
			{
				yaml.Append("    powers: ").AppendLine(Flow(obj.Powers));
			}

			if (obj.Locks.Count > 0)
			{
				yaml.AppendLine("    locks:");
				foreach (var (type, value) in obj.Locks.OrderBy(l => l.Key, StringComparer.OrdinalIgnoreCase))
				{
					yaml.Append("      ").Append(type).Append(": ").AppendLine(Scalar(value));
				}
			}

			WriteAttributes(yaml, obj.Attributes);
		}
	}

	private static void WriteAttributes(StringBuilder yaml, IReadOnlyDictionary<string, PackageAttributeSpec> attributes)
	{
		if (attributes.Count == 0)
		{
			return;
		}

		yaml.AppendLine("    attributes:");
		foreach (var (name, spec) in attributes.OrderBy(a => a.Key, StringComparer.Ordinal))
		{
			yaml.Append("      ").Append(name).AppendLine(":");
			AppendValue(yaml, spec.Value, "        ");
			if (spec.Flags.Count > 0)
			{
				yaml.Append("        flags: ").AppendLine(Flow(spec.Flags));
			}
		}
	}

	private static void AppendRef(StringBuilder yaml, string key, PackageRef? value)
	{
		if (value is not null)
		{
			yaml.Append("    ").Append(key).Append(": ").AppendLine(Scalar(value.ToString()));
		}
	}

	private static void AppendOptional(StringBuilder yaml, string key, string? value, string indent = "")
	{
		if (value is not null)
		{
			yaml.Append(indent).Append(key).Append(": ").AppendLine(Scalar(value));
		}
	}

	/// <summary>
	/// Writes an attribute value. Multi-line MUSHcode rides in a literal block scalar so diffs stay
	/// readable; anything else is single-quoted.
	/// </summary>
	/// <remarks>
	/// A block scalar cannot carry a value that is empty or whose first or last line is blank or
	/// begins with whitespace: YamlDotNet has no blank line to anchor the indentation on and rejects
	/// the manifest outright, and leading spaces would be silently eaten. Those go single-quoted,
	/// which preserves whitespace exactly.
	/// </remarks>
	private static void AppendValue(StringBuilder yaml, string value, string indent)
	{
		var normalized = value.Replace("\r\n", "\n");
		if (!normalized.Contains('\n')
			|| normalized.Length == 0
			|| char.IsWhiteSpace(normalized[0])
			|| char.IsWhiteSpace(normalized[^1]))
		{
			yaml.Append(indent).Append("value: ").AppendLine(Scalar(normalized));
			return;
		}

		yaml.Append(indent).AppendLine("value: |-");
		var text = normalized.AsSpan();
		foreach (var line in text.Split('\n'))
		{
			yaml.Append(indent).Append("  ").Append(text[line]).AppendLine();
		}
	}

	/// <summary>A single-quoted YAML scalar — the one form that never re-parses as a number, bool or null.</summary>
	private static string Scalar(string value) => $"'{value.Replace("'", "''")}'";

	private static string Flow(IEnumerable<string> values)
		=> $"[{string.Join(", ", values.Select(Scalar))}]";
}
