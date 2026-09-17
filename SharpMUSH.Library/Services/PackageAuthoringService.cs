using System.Text.RegularExpressions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
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
	IObjectStore database,
	IAttributeStore attributeStore,
	IPackageManifestService manifests) : IPackageAuthoringService
{
	[GeneratedRegex(@"#(?<number>\d+)(?::\d+)?")]
	private static partial Regex DbrefInCodeRegex();

	[GeneratedRegex("[^a-z0-9_]+")]
	private static partial Regex SlugCleanupRegex();

	public async Task<Result<PackageAuthoringScan>> ScanAsync(
		IReadOnlyList<string> objids, CancellationToken cancellationToken = default)
	{
		var objects = new List<AuthoringObject>();
		foreach (var objid in objids.Distinct())
		{
			switch (await ReadObjectAsync(objid, cancellationToken))
			{
				case AuthoringObject authored:
					objects.Add(authored);
					break;
				case Error<string> error:
					return error;
			}
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

	public async Task<Result<string>> ExportAsync(
		PackageAuthoringRequest request, CancellationToken cancellationToken = default)
	{
		var selections = new List<(AuthoringObjectSelection Selection, AuthoringObject Object)>();
		var tokenByNumber = new Dictionary<int, string>();
		foreach (var selection in request.Objects)
		{
			switch (await ReadObjectAsync(selection.Objid, cancellationToken))
			{
				case AuthoringObject authored:
					selections.Add((selection, authored));
					break;
				case Error<string> error:
					return error;
			}

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

		var objects = new List<PackageObjectSpec>();
		foreach (var (selection, obj) in selections)
		{
			if (!Enum.TryParse<PackageObjectType>(obj.Type, ignoreCase: true, out var type))
			{
				return new Error<string>($"'{obj.Objid}' has type '{obj.Type}', which a package cannot create.");
			}

			PackageRef? parent = null;
			if (obj.ParentObjid is not null)
			{
				var parentNumber = DbrefNumber(obj.ParentObjid);
				if (parentNumber is null || !tokenByNumber.TryGetValue(parentNumber.Value, out var parentToken))
				{
					unresolved.Add($"#{parentNumber} (parent of {{{{{selection.Ref}}}}})");
				}
				else
				{
					parent = PackageRefScanner.ParseSingle(parentToken);
				}
			}

			var excluded = selection.ExcludedAttributes.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var attributes = new Dictionary<string, PackageAttributeSpec>(StringComparer.OrdinalIgnoreCase);
			foreach (var (attrName, value) in obj.Attributes
				.Where(a => !excluded.Contains(a.Key) && !a.Key.Contains(' '))
				.OrderBy(a => a.Key, StringComparer.Ordinal))
			{
				attributes[attrName] = new PackageAttributeSpec(Tokenize(value), []);
			}

			objects.Add(new PackageObjectSpec(
				selection.Ref, type, obj.Name, null, parent, null, null,
				[], obj.Flags.Select(f => f.ToLowerInvariant()).ToList(), [],
				new Dictionary<string, string>(), attributes));
		}

		if (unresolved.Count > 0)
		{
			return new Error<string>(
				$"Unclassified dbref(s): {string.Join(", ", unresolved)}. Classify each as a well-known ref or a configure parameter — manifests never carry dbrefs.");
		}

		if (!PackageVersion.TryParse(request.Version, out var version))
		{
			return new Error<string>($"'{request.Version}' is not a valid package version.");
		}

		var manifest = new PackageManifest(
			PackageFormatVersion.Supported,
			request.PackageId,
			version,
			request.Authors,
			request.Description,
			request.License,
			null,
			[],
			null,
			null,
			null,
			[],
			[],
			usedConfigure.Values
				.OrderBy(c => c.Key, StringComparer.Ordinal)
				.ToDictionary(c => c.Key, c => new PackageConfigureSpec(c.Key, c.Label), StringComparer.Ordinal),
			objects);

		// The writer covers every field the reader reads, but the manifest is also built from live
		// game state, so parse it back: an object whose name or attribute value cannot survive the
		// schema is an export failure, not something to hand the admin.
		var document = PackageManifestWriter.Write(manifest);
		return manifests.ParseManifest(document) switch
		{
			ParsedPackageManifest => document,
			PackageManifestFailure failure => new Error<string>(
				$"Export produced an invalid manifest (bug): {string.Join("; ", failure.Errors.Select(e => e.ToString()))}")
		};
	}

	private async Task<Result<AuthoringObject>> ReadObjectAsync(
		string objid, CancellationToken cancellationToken)
	{
		if (HelperFunctions.ParseDbRef(objid) is not DBRef dbref)
		{
			return new Error<string>($"'{objid}' is not a valid objid.");
		}

		if (await database.GetObjectNodeAsync(dbref, cancellationToken) is not AnySharpObject known)
		{
			return new Error<string>($"Object {objid} does not exist.");
		}

		var sharpObject = known.Object();

		var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		await foreach (var attribute in attributeStore.GetAttributesAsync(dbref, "*", cancellationToken))
		{
			// The PM` tree is engine-managed ref indirection (decision 20.21) —
			// the apply engine recreates it; exports must never carry it.
			if (PackageRefIndirection.IsReservedAttribute(attribute.Name))
			{
				continue;
			}

			attributes[attribute.Name] = attribute.Value.ToPlainText();
		}

		var flags = new List<string>();
		await foreach (var flag in sharpObject.Flags.Value.WithCancellation(cancellationToken))
		{
			flags.Add(flag.Name);
		}

		var parentObjid = await sharpObject.Parent.WithCancellation(cancellationToken) is AnySharpObject parent
			? parent.Object().DBRef.ToString()
			: null;

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
		=> HelperFunctions.ParseDbRef(objidOrDbref) is DBRef dbref ? dbref.Number : null;

	private static string Slugify(string name)
	{
		var slug = SlugCleanupRegex().Replace(name.ToLowerInvariant(), "_").Trim('_');
		return slug.Length == 0 ? "object" : char.IsLetter(slug[0]) || slug[0] == '_' ? slug : $"_{slug}";
	}

}
