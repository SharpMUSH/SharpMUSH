using System.Text;
using System.Text.RegularExpressions;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Implements authoring scan/export (Phase 7). See
/// <see cref="IPackageAuthoringService"/>. v1 scope: top-level attributes,
/// object flags, and parents; locks and attribute trees are left for the
/// authoring UI iteration.
/// </summary>
public partial class PackageAuthoringService(
	ISharpDatabase database,
	IPackageManifestService manifests) : IPackageAuthoringService
{
	[GeneratedRegex(@"#(?<number>\d+)(?::\d+)?")]
	private static partial Regex DbrefInCodeRegex();

	[GeneratedRegex("[^a-z0-9_]+")]
	private static partial Regex SlugCleanupRegex();

	public async Task<OneOf<PackageAuthoringScan, Error<string>>> ScanAsync(
		IReadOnlyList<string> objids, CancellationToken cancellationToken = default)
	{
		var objects = new List<AuthoringObject>();
		foreach (var objid in objids.Distinct())
		{
			var read = await ReadObjectAsync(objid, cancellationToken);
			if (read.IsT1)
			{
				return read.AsT1;
			}

			objects.Add(read.AsT0);
		}

		var selectedNumbers = objects
			.Select(o => DbrefNumber(o.Objid))
			.Where(n => n is not null)
			.ToHashSet();

		var external = new Dictionary<string, (int Count, string Example)>(StringComparer.Ordinal);
		foreach (var obj in objects)
		{
			foreach (var (attrName, value) in obj.Attributes)
			{
				foreach (Match match in DbrefInCodeRegex().Matches(value))
				{
					var number = int.Parse(match.Groups["number"].Value);
					if (selectedNumbers.Contains(number))
					{
						continue;
					}

					var bare = $"#{number}";
					external[bare] = external.TryGetValue(bare, out var existing)
						? (existing.Count + 1, existing.Example)
						: (1, $"{obj.Objid}/{attrName}");
				}
			}
		}

		return new PackageAuthoringScan(
			objects,
			external
				.OrderByDescending(kv => kv.Value.Count)
				.Select(kv => new AuthoringExternalDbref(kv.Key, kv.Value.Count, kv.Value.Example))
				.ToList());
	}

	public async Task<OneOf<string, Error<string>>> ExportAsync(
		PackageAuthoringRequest request, CancellationToken cancellationToken = default)
	{
		// Read every selected object and build the dbref-number → token map.
		var selections = new List<(AuthoringObjectSelection Selection, AuthoringObject Object)>();
		var tokenByNumber = new Dictionary<int, string>();
		foreach (var selection in request.Objects)
		{
			var read = await ReadObjectAsync(selection.Objid, cancellationToken);
			if (read.IsT1)
			{
				return read.AsT1;
			}

			selections.Add((selection, read.AsT0));
			var number = DbrefNumber(selection.Objid);
			if (number is not null)
			{
				tokenByNumber[number.Value] = $"{{{{{selection.Ref}}}}}";
			}
		}

		var usedConfigure = new Dictionary<string, AuthoringConfigureClassification>(StringComparer.Ordinal);
		foreach (var (dbref, wellKnown) in request.WellKnownByDbref)
		{
			var number = DbrefNumber(dbref);
			if (number is not null)
			{
				tokenByNumber[number.Value] = $"{{{{${wellKnown}}}}}";
			}
		}

		foreach (var (dbref, configure) in request.ConfigureByDbref)
		{
			var number = DbrefNumber(dbref);
			if (number is not null)
			{
				tokenByNumber[number.Value] = $"{{{{?{configure.Key}}}}}";
			}
		}

		// Substitute dbrefs in every included attribute; collect unclassified ones.
		var unresolved = new SortedSet<string>(StringComparer.Ordinal);
		string Tokenize(string value)
		{
			// Escape literal mustaches first so authored code survives round-trips.
			var escaped = value.Replace("{{", "{{{{");
			return DbrefInCodeRegex().Replace(escaped, match =>
			{
				var number = int.Parse(match.Groups["number"].Value);
				if (tokenByNumber.TryGetValue(number, out var token))
				{
					if (token.StartsWith("{{?", StringComparison.Ordinal))
					{
						var key = token[3..^2];
						usedConfigure[key] = request.ConfigureByDbref.Values.First(c => c.Key == key);
					}

					return token;
				}

				unresolved.Add($"#{number}");
				return match.Value;
			});
		}

		var yaml = new StringBuilder();
		yaml.AppendLine("format: 1");
		yaml.AppendLine($"package: {request.PackageId}");
		yaml.AppendLine($"version: \"{request.Version}\"");
		if (request.Authors.Count > 0)
		{
			yaml.AppendLine($"authors: [{string.Join(", ", request.Authors)}]");
		}

		yaml.AppendLine($"description: {QuoteYaml(request.Description)}");
		if (request.License is not null)
		{
			yaml.AppendLine($"license: {request.License}");
		}

		var body = new StringBuilder();
		body.AppendLine("objects:");
		foreach (var (selection, obj) in selections)
		{
			var excluded = selection.ExcludedAttributes.ToHashSet(StringComparer.OrdinalIgnoreCase);
			body.AppendLine($"  - ref: {selection.Ref}");
			body.AppendLine($"    type: {obj.Type.ToLowerInvariant()}");
			body.AppendLine($"    name: {QuoteYaml(obj.Name)}");

			if (obj.ParentObjid is not null)
			{
				var parentNumber = DbrefNumber(obj.ParentObjid);
				if (parentNumber is not null && tokenByNumber.TryGetValue(parentNumber.Value, out var parentToken))
				{
					body.AppendLine($"    parent: \"{parentToken}\"");
				}
				else
				{
					unresolved.Add($"#{parentNumber} (parent of {{{{{selection.Ref}}}}})");
				}
			}

			if (obj.Flags.Count > 0)
			{
				body.AppendLine($"    flags: [{string.Join(", ", obj.Flags.Select(f => f.ToLowerInvariant()))}]");
			}

			var included = obj.Attributes
				.Where(a => !excluded.Contains(a.Key) && !a.Key.Contains(' '))
				.OrderBy(a => a.Key, StringComparer.Ordinal)
				.ToList();
			if (included.Count > 0)
			{
				body.AppendLine("    attributes:");
				foreach (var (attrName, value) in included)
				{
					body.AppendLine($"      {attrName}:");
					body.AppendLine("        value: |-");
					foreach (var line in Tokenize(value).Replace("\r\n", "\n").Split('\n'))
					{
						body.AppendLine($"          {line}");
					}
				}
			}
		}

		if (unresolved.Count > 0)
		{
			return new Error<string>(
				$"Unclassified dbref(s): {string.Join(", ", unresolved)}. Classify each as a well-known ref or a configure parameter — manifests never carry dbrefs.");
		}

		if (usedConfigure.Count > 0)
		{
			yaml.AppendLine();
			yaml.AppendLine("configure:");
			foreach (var configure in usedConfigure.Values.OrderBy(c => c.Key, StringComparer.Ordinal))
			{
				yaml.AppendLine($"  {configure.Key}:");
				yaml.AppendLine($"    label: {QuoteYaml(configure.Label)}");
				yaml.AppendLine("    type: dbref");
			}
		}

		yaml.AppendLine();
		yaml.Append(body);

		// Round-trip through the parser: the exporter must never emit an invalid manifest.
		var document = yaml.ToString();
		var validation = manifests.ParseManifest(document);
		return validation.Match<OneOf<string, Error<string>>>(
			_ => document,
			failure => new Error<string>(
				$"Export produced an invalid manifest (bug): {string.Join("; ", failure.Errors.Select(e => e.ToString()))}"));
	}

	private async Task<OneOf<AuthoringObject, Error<string>>> ReadObjectAsync(
		string objid, CancellationToken cancellationToken)
	{
		var dbref = PackageInstallService.ParseObjid(objid);
		if (dbref is null)
		{
			return new Error<string>($"'{objid}' is not a valid objid.");
		}

		var node = await database.GetObjectNodeAsync(dbref.Value, cancellationToken);
		if (node.IsNone())
		{
			return new Error<string>($"Object {objid} does not exist.");
		}

		var known = node.Known();
		var sharpObject = known.Object();

		var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		await foreach (var attribute in database.GetAttributesAsync(dbref.Value, "*", cancellationToken))
		{
			attributes[attribute.Name] = attribute.Value.ToPlainText();
		}

		var flags = new List<string>();
		await foreach (var flag in sharpObject.Flags.Value.WithCancellation(cancellationToken))
		{
			flags.Add(flag.Name);
		}

		var parent = await sharpObject.Parent.WithCancellation(cancellationToken);
		var parentObjid = parent.IsNone() ? null : parent.Known().Object().DBRef.ToString();

		return new AuthoringObject(
			sharpObject.DBRef.ToString(),
			sharpObject.Name,
			sharpObject.Type,
			Slugify(sharpObject.Name),
			parentObjid,
			attributes,
			flags);
	}

	private static int? DbrefNumber(string objidOrDbref)
	{
		var dbref = PackageInstallService.ParseObjid(objidOrDbref);
		return dbref?.Number;
	}

	private static string Slugify(string name)
	{
		var slug = SlugCleanupRegex().Replace(name.ToLowerInvariant(), "_").Trim('_');
		return slug.Length == 0 ? "object" : char.IsLetter(slug[0]) || slug[0] == '_' ? slug : $"_{slug}";
	}

	private static string QuoteYaml(string text) => $"'{text.Replace("'", "''")}'";
}
