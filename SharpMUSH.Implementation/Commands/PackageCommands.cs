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

	/// <summary>
	/// @PACKAGE — the in-game shortcut to the softcode package authoring service
	/// (the same engine behind /admin/packages/author). Wraps scan + export so a
	/// wizard can turn a cluster of live objects into a package.yaml manifest
	/// without leaving the game.
	///
	/// <para>
	/// <c>@package/scan obj1 obj2 ...</c> — read-only report: shows the suggested
	/// ref for each object and any dbrefs referenced outside the selection.
	/// </para>
	/// <para>
	/// <c>@package obj1 obj2 ...=&lt;id&gt;[,&lt;version&gt;[,&lt;description&gt;]]</c> — exports the
	/// selection as a manifest, pemitted back to you. In-selection dbrefs become
	/// <c>{{ref}}</c> tokens automatically. This single-step path only succeeds when
	/// the selection is self-contained (every dbref in their attributes points at
	/// another selected object); anything referencing the outside world needs the
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
			await NotifyService!.Notify(executor, "PACKAGE: You must name at least one object to package.");
			return new CallState(string.Empty);
		}

		// Resolve every named object to a stable objid the authoring service understands.
		var objids = new List<string>();
		foreach (var token in tokens)
		{
			var locate = await LocateService!.LocateAndNotifyIfInvalid(parser, enactor, executor, token, LocateFlags.All);
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
				await NotifyService!.Notify(executor, $"PACKAGE: You can't examine {known.Object().Name}.");
				return new CallState(string.Empty);
			}

			objids.Add(known.Object().DBRef.ToString());
		}

		var authoring = parser.ServiceProvider.GetRequiredService<IPackageAuthoringService>();

		var scan = await authoring.ScanAsync(objids.Distinct().ToList());
		if (scan.IsT1)
		{
			await NotifyService!.Notify(executor, $"PACKAGE: {scan.AsT1.Value}");
			return new CallState(string.Empty);
		}

		var scanResult = scan.AsT0;

		// /SCAN: read-only report only — never produces a manifest.
		if (switches.Contains("SCAN"))
		{
			var report = new StringBuilder();
			report.AppendLine($"PACKAGE SCAN: {scanResult.Objects.Count} object(s) selected.");
			foreach (var obj in scanResult.Objects)
			{
				report.AppendLine(
					$"  {obj.Name} ({obj.Type}, {obj.Objid}) -> ref '{obj.SuggestedRef}'; {obj.Attributes.Count} attr(s), {obj.Flags.Count} flag(s).");
			}

			if (scanResult.ExternalDbrefs.Count == 0)
			{
				report.AppendLine("Self-contained: no external dbrefs. Ready to package with:");
				report.Append($"  @package {objectList}=<package-id>");
			}
			else
			{
				report.AppendLine(
					$"External references ({scanResult.ExternalDbrefs.Count}) must be classified in the web authoring panel:");
				foreach (var ext in scanResult.ExternalDbrefs)
				{
					report.AppendLine($"  {ext.Dbref}  ({ext.Occurrences} occurrence(s), e.g. {ext.ExampleAttribute})");
				}

				report.Append("Finish at: /admin/packages/author");
			}

			await NotifyService!.Notify(executor, report.ToString());
			return new CallState(string.Empty);
		}

		// Export path needs a package id on the right-hand side.
		if (!args.TryGetValue("1", out var idArg) || string.IsNullOrWhiteSpace(idArg.Message?.ToPlainText()))
		{
			await NotifyService!.Notify(executor,
				"PACKAGE: Usage: @package <objects>=<package-id>[,<version>[,<description>]]  (or @package/scan <objects>)");
			return new CallState(string.Empty);
		}

		var packageId = idArg.Message!.ToPlainText().Trim();
		if (!PackageIdRegex().IsMatch(packageId))
		{
			await NotifyService!.Notify(executor,
				$"PACKAGE: '{packageId}' is not a valid package id (lowercase letters, digits and hyphens; must start with a letter).");
			return new CallState(string.Empty);
		}

		// Self-contained only: any dbref pointing outside the selection needs the
		// well-known/configure classification the web authoring panel provides.
		if (scanResult.ExternalDbrefs.Count > 0)
		{
			var blocked = new StringBuilder();
			blocked.AppendLine(
				$"PACKAGE: These objects reference {scanResult.ExternalDbrefs.Count} dbref(s) outside your selection, so they aren't self-contained:");
			foreach (var ext in scanResult.ExternalDbrefs)
			{
				blocked.AppendLine($"  {ext.Dbref}  ({ext.Occurrences} occurrence(s), e.g. {ext.ExampleAttribute})");
			}

			blocked.AppendLine(
				"Each must be classified as a well-known object or a configure parameter — finish this package at:");
			blocked.Append("  /admin/packages/author");
			await NotifyService!.Notify(executor, blocked.ToString());
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

		// Give every object a unique manifest ref derived from its suggested slug.
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

			selections.Add(new AuthoringObjectSelection(obj.Objid, refName, []));
		}

		var export = await authoring.ExportAsync(new PackageAuthoringRequest(
			packageId, version, description, null, [executor.Object().Name],
			selections,
			new Dictionary<string, string>(),
			new Dictionary<string, AuthoringConfigureClassification>()));

		if (export.IsT1)
		{
			await NotifyService!.Notify(executor, $"PACKAGE: {export.AsT1.Value}");
			return new CallState(string.Empty);
		}

		var output = new StringBuilder();
		output.AppendLine($"PACKAGE: Generated manifest for '{packageId}' v{version} ({selections.Count} object(s)).");
		output.AppendLine("Copy everything between the markers into a package.yaml:");
		output.AppendLine("----- BEGIN package.yaml -----");
		output.AppendLine(export.AsT0.TrimEnd());
		output.Append("----- END package.yaml -----");
		await NotifyService!.Notify(executor, output.ToString());
		return new CallState(string.Empty);
	}
}
