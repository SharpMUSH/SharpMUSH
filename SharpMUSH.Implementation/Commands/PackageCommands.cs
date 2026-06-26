using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using OneOf.Types;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[GeneratedRegex("^[a-z][a-z0-9-]*$")]
	private static partial Regex PackageIdRegex();

	[GeneratedRegex(@"#(?<number>\d+)(?::\d+)?")]
	private static partial Regex PackageDbrefRegex();

	private const string VeiledAttributeFlag = "VEILED";

	/// <summary>
	/// @PACKAGE — the in-game face of the softcode package authoring service
	/// (the same engine behind /admin/packages/author). Wraps scan + export so a
	/// wizard can turn a cluster of live objects into a package.yaml manifest
	/// without leaving the game.
	///
	/// <para>
	/// Visibility matches @decompile: an object must pass <c>CanExamine</c>, and only
	/// the attributes the executor can see (per <c>GetVisibleAttributesAsync</c>, with
	/// VEILED attributes skipped) are ever scanned or exported.
	/// </para>
	/// <para>
	/// <c>@package/scan obj1 obj2 ...</c> — read-only report: shows the suggested
	/// ref for each object and any dbrefs referenced outside the selection.
	/// </para>
	/// <para>
	/// <c>@package obj1 obj2 ...=&lt;id&gt;[,&lt;version&gt;[,&lt;description&gt;]]</c> — exports the
	/// selection as a manifest, pemitted back to you. In-selection dbrefs become
	/// <c>{{ref}}</c> tokens automatically. This single-step path only succeeds when
	/// the selection is self-contained (every dbref in the visible attributes points
	/// at another selected object); anything referencing the outside world needs the
	/// well-known / configure classification step in the web authoring panel.
	/// </para>
	/// </summary>
	[SharpCommand(Name = "@PACKAGE", Switches = ["SCAN"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		CommandLock = "FLAG^WIZARD", MinArgs = 1, MaxArgs = 4,
		ParameterNames = ["objects", "package", "version", "description"])]
	public static async ValueTask<Option<CallState>> Package(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var objectList = args["0"].Message?.ToPlainText() ?? string.Empty;
		var tokens = objectList.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (tokens.Length == 0)
		{
			await NotifyService!.Notify(executor, "PACKAGE: You must name at least one object to package.", executor);
			return new CallState(string.Empty);
		}

		// Resolve every named object to a stable objid the authoring service understands.
		// Object visibility matches @decompile: each must pass CanExamine.
		var objids = new List<string>();
		var knownByObjid = new Dictionary<string, AnySharpObject>(StringComparer.Ordinal);
		foreach (var token in tokens)
		{
			var locate = await LocateService!.LocateAndNotifyIfInvalid(parser, executor, executor, token, LocateFlags.All);
			if (!locate.IsValid())
			{
				return new None();
			}

			var found = locate.WithoutError();
			if (found.IsNone())
			{
				return new None();
			}

			var known = found.Known();
			if (!await PermissionService!.CanExamine(executor, known))
			{
				await NotifyService!.Notify(executor, $"PACKAGE: You can't examine {known.Object().Name}.", executor);
				return new CallState(string.Empty);
			}

			var objid = known.Object().DBRef.ToString();
			objids.Add(objid);
			knownByObjid[objid] = known;
		}

		var authoring = parser.ServiceProvider.GetRequiredService<IPackageAuthoringService>();

		var scan = await authoring.ScanAsync(objids.Distinct().ToList());
		if (scan.IsT1)
		{
			await NotifyService!.Notify(executor, $"PACKAGE: {scan.AsT1.Value}", executor);
			return new CallState(string.Empty);
		}

		var scanResult = scan.AsT0;

		// Attribute visibility matches @decompile: keep only the attributes the
		// executor may see (GetVisibleAttributesAsync), minus VEILED. Everything else
		// is excluded from the export and ignored when judging self-containment.
		var visibleByObjid = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
		foreach (var obj in scanResult.Objects)
		{
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (knownByObjid.TryGetValue(obj.Objid, out var known))
			{
				var visible = await AttributeService!.GetVisibleAttributesAsync(executor, known);
				if (visible.IsAttribute)
				{
					foreach (var attr in visible.AsAttributes)
					{
						if (!attr.Flags.Any(f => f.Name.Equals(VeiledAttributeFlag, StringComparison.OrdinalIgnoreCase)))
						{
							names.Add(attr.Name);
						}
					}
				}
			}

			visibleByObjid[obj.Objid] = names;
		}

		var selectedNumbers = knownByObjid.Values.Select(k => k.Object().DBRef.Number).ToHashSet();

		// /SCAN: read-only report only — never produces a manifest. External dbrefs are
		// computed over the visible attributes so the report agrees with what export does.
		if (switches.Contains("SCAN"))
		{
			var external = ExternalDbrefsOverVisible(scanResult, visibleByObjid, selectedNumbers);

			var report = new StringBuilder();
			report.AppendLine($"PACKAGE SCAN: {scanResult.Objects.Count} object(s) selected.");
			foreach (var obj in scanResult.Objects)
			{
				var visibleCount = visibleByObjid[obj.Objid].Count;
				var hidden = obj.Attributes.Count - visibleCount;
				var hiddenNote = hidden > 0 ? $", {hidden} hidden" : string.Empty;
				report.AppendLine(
					$"  {obj.Name} ({obj.Type}, {obj.Objid}) -> ref '{obj.SuggestedRef}'; {visibleCount} visible attr(s){hiddenNote}, {obj.Flags.Count} flag(s).");
			}

			if (external.Count == 0)
			{
				report.AppendLine("Self-contained: no external dbrefs. Ready to package with:");
				report.Append($"  @package {objectList}=<package-id>");
			}
			else
			{
				report.AppendLine(
					$"External references ({external.Count}) must be classified in the web authoring panel:");
				foreach (var (dbref, count, example) in external)
				{
					report.AppendLine($"  {dbref}  ({count} occurrence(s), e.g. {example})");
				}

				report.Append("Finish at: /admin/packages/author");
			}

			await NotifyService!.Notify(executor, report.ToString(), executor);
			return new CallState(string.Empty);
		}

		if (!args.TryGetValue("1", out var idArg) || string.IsNullOrWhiteSpace(idArg.Message?.ToPlainText()))
		{
			await NotifyService!.Notify(executor,
				"PACKAGE: Usage: @package <objects>=<package-id>[,<version>[,<description>]]  (or @package/scan <objects>)",
				executor);
			return new CallState(string.Empty);
		}

		var packageId = idArg.Message!.ToPlainText().Trim();
		if (!PackageIdRegex().IsMatch(packageId))
		{
			await NotifyService!.Notify(executor,
				$"PACKAGE: '{packageId}' is not a valid package id (lowercase letters, digits and hyphens; must start with a letter).",
				executor);
			return new CallState(string.Empty);
		}

		var version = args.TryGetValue("2", out var versionArg)
			? versionArg.Message?.ToPlainText().Trim() ?? string.Empty
			: string.Empty;
		if (string.IsNullOrEmpty(version))
		{
			version = "1.0.0";
		}

		var description = args.TryGetValue("3", out var descriptionArg)
			? descriptionArg.Message?.ToPlainText().Trim() ?? string.Empty
			: string.Empty;
		if (string.IsNullOrEmpty(description))
		{
			description = $"Exported from {executor.Object().Name} via @package.";
		}

		// Give every object a unique manifest ref derived from its suggested slug, and
		// exclude every attribute the executor cannot see (parity with @decompile). The
		// authoring exporter only inspects included attributes when resolving dbrefs, so
		// self-containment is judged over exactly the visible set.
		var usedRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var selections = new List<AuthoringObjectSelection>();
		foreach (var obj in scanResult.Objects)
		{
			var refName = obj.SuggestedRef;
			if (!usedRefs.Add(refName))
			{
				var n = 2;
				string candidate;
				do
				{
					candidate = $"{refName}_{n}";
					n++;
				} while (!usedRefs.Add(candidate));

				refName = candidate;
			}

			var visible = visibleByObjid[obj.Objid];
			var excluded = obj.Attributes.Keys.Where(k => !visible.Contains(k)).ToList();
			selections.Add(new AuthoringObjectSelection(obj.Objid, refName, excluded));
		}

		var export = await authoring.ExportAsync(new PackageAuthoringRequest(
			packageId, version, description, null, [executor.Object().Name],
			selections,
			new Dictionary<string, string>(),
			new Dictionary<string, AuthoringConfigureClassification>()));

		if (export.IsT1)
		{
			var error = export.AsT1.Value;
			// Unclassified dbrefs mean the selection isn't self-contained — point the
			// user at the web panel where they can classify them.
			var hint = error.StartsWith("Unclassified", StringComparison.Ordinal)
				? "\nThese objects reference the outside world — finish this package at: /admin/packages/author"
				: string.Empty;
			await NotifyService!.Notify(executor, $"PACKAGE: {error}{hint}", executor);
			return new CallState(string.Empty);
		}

		var output = new StringBuilder();
		output.AppendLine($"PACKAGE: Generated manifest for '{packageId}' v{version} ({selections.Count} object(s)).");
		output.AppendLine("Copy everything between the markers into a package.yaml:");
		output.AppendLine("----- BEGIN package.yaml -----");
		output.AppendLine(export.AsT0.TrimEnd());
		output.Append("----- END package.yaml -----");
		await NotifyService!.Notify(executor, output.ToString(), executor);
		return new CallState(string.Empty);
	}

	/// <summary>
	/// Finds dbrefs referenced in the <em>visible</em> attribute values that are not
	/// themselves in the selection. Mirrors the authoring service's scan, but scoped to
	/// the attributes the executor may see so the scan report matches the export.
	/// </summary>
	private static List<(string Dbref, int Count, string Example)> ExternalDbrefsOverVisible(
		PackageAuthoringScan scanResult,
		IReadOnlyDictionary<string, HashSet<string>> visibleByObjid,
		IReadOnlySet<int> selectedNumbers)
	{
		var external = new Dictionary<int, (int Count, string Example)>();
		foreach (var obj in scanResult.Objects)
		{
			var visible = visibleByObjid[obj.Objid];
			foreach (var (attrName, value) in obj.Attributes)
			{
				if (!visible.Contains(attrName))
				{
					continue;
				}

				foreach (Match match in PackageDbrefRegex().Matches(value))
				{
					var number = int.Parse(match.Groups["number"].Value);
					if (selectedNumbers.Contains(number))
					{
						continue;
					}

					external[number] = external.TryGetValue(number, out var existing)
						? (existing.Count + 1, existing.Example)
						: (1, $"{obj.Objid}/{attrName}");
				}
			}
		}

		return external
			.OrderByDescending(kv => kv.Value.Count)
			.Select(kv => ($"#{kv.Key}", kv.Value.Count, kv.Value.Example))
			.ToList();
	}
}
