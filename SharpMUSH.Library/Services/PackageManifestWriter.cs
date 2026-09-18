using System.Buffers;
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
/// The output is shaped to look like the hand-written manifests it sits beside: plain scalars
/// wherever one reads back as the same string, single quotes only where it would not, and literal
/// block scalars for attribute values so MUSHcode stays diffable and cannot be re-typed by the
/// YAML parser.
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
			// Attribute names come off live objects and the store does not constrain them, so the
			// reader accepts any non-whitespace name. A bare key that leads with a YAML indicator,
			// or carries a colon, is not the name that went in.
			yaml.Append("      ").Append(Scalar(name)).AppendLine(":");
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
	/// Writes an attribute value. MUSHcode rides in a literal block scalar so diffs stay readable and
	/// nothing inside it can be re-typed by the YAML parser — a block scalar is always a string, so
	/// <c>12345</c> and <c>true</c> come back as text without any quoting of their own.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A block scalar cannot carry a value that is empty or whose first or last character is
	/// whitespace: YamlDotNet has no non-blank line to anchor the indentation on and rejects the
	/// manifest outright ("extra spaces in first line"), and leading whitespace would be silently
	/// lost.
	/// </para>
	/// <para>
	/// Those fall back on quoting, and which quote matters. A single-quoted scalar preserves
	/// whitespace but FOLDS physical line breaks into spaces, so a padded multi-line value written
	/// that way parses cleanly and comes back as different MUSHcode. Only a double-quoted scalar,
	/// where the break is an explicit <c>\n</c> escape, carries both.
	/// </para>
	/// </remarks>
	private static void AppendValue(StringBuilder yaml, string value, string indent)
	{
		var normalized = value.Replace("\r\n", "\n");
		var blockScalarFits = normalized.Length > 0
			&& !char.IsWhiteSpace(normalized[0])
			&& !char.IsWhiteSpace(normalized[^1]);

		if (!blockScalarFits)
		{
			yaml.Append(indent).Append("value: ")
				.AppendLine(normalized.Contains('\n') ? DoubleQuoted(normalized) : Quoted(normalized));
			return;
		}

		yaml.Append(indent).AppendLine("value: |-");
		var text = normalized.AsSpan();
		foreach (var line in text.Split('\n'))
		{
			yaml.Append(indent).Append("  ").Append(text[line]).AppendLine();
		}
	}

	/// <summary>
	/// A YAML scalar, quoted only when a plain one would not come back as the same string.
	/// Manifests are read and hand-edited by admins, and every hand-written manifest in the repo
	/// uses plain scalars, so an exporter that quotes indiscriminately produces something that does
	/// not look like the thing it is a copy of.
	/// </summary>
	private static string Scalar(string value) => NeedsQuoting(value) ? Quoted(value) : value;

	private static string Quoted(string value) => $"'{value.Replace("'", "''")}'";

	/// <summary>
	/// A double-quoted YAML scalar. The only quoting that survives a line break, which is why it
	/// exists here at all — see <see cref="AppendValue"/>.
	/// </summary>
	private static string DoubleQuoted(string value)
	{
		var quoted = new StringBuilder(value.Length + 2).Append('"');
		foreach (var character in value)
		{
			switch (character)
			{
				case '\\': quoted.Append("\\\\"); break;
				case '"': quoted.Append("\\\""); break;
				case '\n': quoted.Append("\\n"); break;
				case '\r': quoted.Append("\\r"); break;
				case '\t': quoted.Append("\\t"); break;
				default:
					if (char.IsControl(character))
					{
						quoted.Append("\\x").Append(((int)character).ToString("x2", CultureInfo.InvariantCulture));
					}
					else
					{
						quoted.Append(character);
					}

					break;
			}
		}

		return quoted.Append('"').ToString();
	}

	/// <summary>Characters that make a plain scalar ambiguous wherever they appear in it.</summary>
	private static readonly SearchValues<char> Hazards = SearchValues.Create("\n\r\t#:");

	/// <summary>
	/// Characters that mean something other than themselves at the start of a plain scalar. The
	/// last two are not YAML indicators but number leads — <c>+1</c> resolves to an int and
	/// <c>.inf</c> to a double, neither of which leads with a digit.
	/// </summary>
	private static readonly SearchValues<char> LeadingIndicators = SearchValues.Create("-?:,[]{}#&*!|>'\"%@`+.");

	/// <summary>The words YAML reads as a bool or a null rather than as text.</summary>
	private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
	{
		"true", "false", "yes", "no", "on", "off", "y", "n", "null", "~"
	};

	private static bool NeedsQuoting(string value)
		=> value.Length == 0
			|| char.IsWhiteSpace(value[0])
			|| char.IsWhiteSpace(value[^1])
			|| value.AsSpan().ContainsAny(Hazards)
			|| LeadingIndicators.Contains(value[0])
			// A leading digit covers every number, version and date shape in one rule. It is broader
			// than YAML strictly needs and deliberately so: the alternative is re-deciding, per field,
			// whether the reader will hand back a string or a double.
			|| char.IsAsciiDigit(value[0])
			|| Keywords.Contains(value);

	/// <summary>
	/// A flow sequence. Flow context gives <c>,</c>, <c>[</c> and <c>]</c> meaning that block context
	/// does not, so those force a quote here on top of the usual rules.
	/// </summary>
	private static string Flow(IEnumerable<string> values)
		=> $"[{string.Join(", ", values.Select(FlowScalar))}]";

	private static string FlowScalar(string value)
		=> NeedsQuoting(value) || value.AsSpan().ContainsAny(FlowHazards) ? Quoted(value) : value;

	private static readonly SearchValues<char> FlowHazards = SearchValues.Create(",[]{}");
}
