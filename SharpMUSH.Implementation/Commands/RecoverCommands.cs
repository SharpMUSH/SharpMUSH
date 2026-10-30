using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using MarkupString.Layout;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

/// <summary>
/// The recover list, under RhostMUSH's names: <c>@recover</c> lists it, explains an entry and brings one back, and
/// <c>@purge</c> with an object or a bulk switch removes entries for good. A GOING object that has not been freed yet
/// is spared by <c>@recover</c> as <c>@undestroy</c> spares it.
/// </summary>
public partial class Commands
{
	private static readonly string[] RecoverListSwitches = ["LIST", "TYPE", "OWNER", "AGE", "COUNT"];
	private static readonly string[] PurgeBulkSwitches = ["ALL", "TIME", "TYPE", "OWNER"];

	/// <summary>How many entries a bulk @purge preview names before it only counts.</summary>
	private const int PurgePreviewLines = 20;

	[SharpCommand(Name = "@RECOVER", Switches = ["LIST", "TYPE", "OWNER", "AGE", "COUNT", "DETAIL", "KEEP", "UNKEEP", "EXITS"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 2, ParameterNames = ["object", "reason"])]
	public async ValueTask<Option<CallState>> Recover(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.ToArray();
		var args = parser.CurrentState.Arguments;
		var target = args.TryGetValue("0", out var first) ? first.Message!.ToPlainText().Trim() : string.Empty;
		var reason = args.TryGetValue("1", out var second) ? second.Message!.ToPlainText().Trim() : string.Empty;

		var output = switches switch
		{
			_ when switches.Contains("DETAIL") => await RecoverDetailAsync(parser, executor, target),
			_ when switches.Contains("KEEP") => await RecoverKeepAsync(executor, target, reason.Length == 0 ? "no reason given" : reason),
			_ when switches.Contains("UNKEEP") => await RecoverKeepAsync(executor, target, null),
			_ when switches.Contains("EXITS") => await RecoverExitsAsync(parser, executor, target),
			_ when target.Length == 0 || switches.Any(RecoverListSwitches.Contains) => await RecoverListAsync(parser, executor, switches, target),
			_ => await RecoverOneAsync(parser, executor, target)
		};

		if (output.Length > 0) await NotifyService.Notify(executor, output);
		return new CallState(output);
	}

	/// <summary><c>@recover &lt;object&gt;</c>: spares a GOING object, or brings one back from the recover list.</summary>
	private async ValueTask<MString> RecoverOneAsync(IMUSHCodeParser parser, AnySharpObject executor, string target)
	{
		if (DBRef.TryParse(target, out var parsed) && parsed is { } objid)
		{
			// An objid names one incarnation: a live object or an entry of another creation time is not it.
			if (await Mediator.Send(new GetObjectNodeQuery(objid)) is AnySharpObject live)
				return await SpareAsync(parser, executor, live);

			return await RecoverEntryAsync(target) switch
			{
				RecoverEntry entry => await RestoreAsync(executor, entry),
				_ => MarkupText.Plain($"Nothing destroyed is held under {target}. @recover lists what is.")
			};
		}

		var located = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, target, LocateFlags.All);
		return located is AnySharpObject found ? await SpareAsync(parser, executor, found) : MarkupText.Empty;
	}

	/// <summary>A GOING object is spared as @undestroy spares it; RhostMUSH's @recover reached the same object.</summary>
	private async ValueTask<MString> SpareAsync(IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject obj)
	{
		if (!await PermissionService.Controls(executor, obj)) return MarkupText.Plain(ErrorMessages.Notifications.PermissionDenied);
		if (!await UndestroyAsync(parser, obj.Object().DBRef, [])) return MarkupText.Plain($"{obj.Object().Name} is not destroyed.");

		await Audit.RecordAsync(executor, AuditActions.ObjectRecover, AuditTargets.Of(obj), "spared while GOING");
		return MarkupText.Plain(string.Format(ErrorMessages.Notifications.SparedFromDestructionFormat, obj.Object().Name));
	}

	private async ValueTask<MString> RestoreAsync(AnySharpObject executor, RecoverEntry entry)
	{
		if (!await RecoverList.MayRecoverAsync(executor, entry)) return MarkupText.Plain(ErrorMessages.Notifications.PermissionDenied);

		return await RecoverList.RecoverAsync(executor, entry) switch
		{
			RecoverOutcome outcome => MarkupText.Plain(string.Join('\n',
				[$"Recovered {entry.Name}(#{entry.Objid.Number}). It is set HALT until you look it over.", .. outcome.Notes])),
			Error<string> error => MarkupText.Plain($"{entry.Name}(#{entry.Objid.Number}) cannot be recovered: {error.Value}")
		};
	}

	/// <summary><c>@recover/exits &lt;room&gt;</c>: the room's exits that were destroyed with it.</summary>
	private async ValueTask<MString> RecoverExitsAsync(IMUSHCodeParser parser, AnySharpObject executor, string target)
	{
		if (await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, target, LocateFlags.All) is not AnySharpObject { IsRoom: true } room)
			return MarkupText.Plain("@recover/exits takes a room that exists. Recover the room first.");

		var roomObjid = room.Object().DBRef;
		var exits = await RecoverList.VisibleAsync(executor)
			.Where(entry => entry.Type == "EXIT" && entry.Location is { } source && source.Equals(roomObjid))
			.ToListAsync();
		if (exits.Count == 0) return MarkupText.Plain($"No destroyed exits of {room.Object().Name} are on the recover list.");

		List<string> lines = [];
		foreach (var exit in exits)
		{
			lines.Add((await RestoreAsync(executor, exit)).ToPlainText());
		}

		return MarkupText.Plain(string.Join('\n', lines));
	}

	private async ValueTask<MString> RecoverKeepAsync(AnySharpObject executor, string target, string? reason)
	{
		if (await RecoverEntryAsync(target) is not RecoverEntry entry)
			return MarkupText.Plain($"Nothing destroyed is held under {target}. @recover lists what is.");
		if (!await RecoverList.MayRecoverAsync(executor, entry)) return MarkupText.Plain(ErrorMessages.Notifications.PermissionDenied);
		if (reason is null && entry.Keep is null) return MarkupText.Plain($"{entry.Name}(#{entry.Objid.Number}) is not kept.");

		await RecoverList.SetKeepAsync(executor, entry, reason);
		return MarkupText.Plain(reason is null
			? $"{entry.Name}(#{entry.Objid.Number}) is no longer kept. It expires with the others."
			: $"{entry.Name}(#{entry.Objid.Number}) is kept: it never expires, and only @purge/confirm #{entry.Objid.Number} removes it.");
	}

	private async ValueTask<MString> RecoverDetailAsync(IMUSHCodeParser parser, AnySharpObject executor, string target)
	{
		if (await RecoverEntryAsync(target) is not RecoverEntry entry || !await MaySeeRecoverEntryAsync(executor, entry))
			return MarkupText.Plain($"Nothing destroyed is held under {target}. @recover lists what is.");

		var plan = await RecoverList.PlanAsync(entry, executor);
		(string, MString)[] fields =
		[
			("Object", MarkupText.Plain($"{entry.Name}(#{entry.Objid.Number})")),
			("Objid", MarkupText.Plain(entry.Objid.ToString())),
			("Type", MarkupText.Plain(entry.Type)),
			("Owner", MarkupText.Plain(await RecoverRelationAsync(entry.Owner))),
			("Location", MarkupText.Plain(await RecoverRelationAsync(entry.Location))),
			("Home", MarkupText.Plain(await RecoverRelationAsync(entry.Home))),
			("Parent", MarkupText.Plain(await RecoverRelationAsync(entry.Parent))),
			("Zone", MarkupText.Plain(await RecoverRelationAsync(entry.Zone))),
			("Flags", MarkupText.Plain(entry.Flags.Count == 0 ? "none" : string.Join(' ', entry.Flags))),
			("Powers", MarkupText.Plain(entry.Powers.Count == 0 ? "none" : string.Join(' ', entry.Powers))),
			("Roles", MarkupText.Plain(entry.Roles.Count == 0 ? "none" : string.Join(' ', entry.Roles))),
			("Destroyed", MarkupText.Plain(entry.DestroyedBy is { } by
				? $"{Day(entry.DestroyedAt)} by {await RecoverRelationAsync(by)}"
				: Day(entry.DestroyedAt))),
			("Kept", MarkupText.Plain(entry.Keep is { } keep
				? $"{keep.Reason} ({await RecoverRelationAsync(keep.By)}, {Day(keep.At)})"
				: await RecoverList.IsKeptAsync(entry) ? "yes, a package records it" : "no")),
			("Until", MarkupText.Plain(await RecoverUntilAsync(entry))),
			("Size", MarkupText.Plain($"{entry.Bytes.ToString("N0", CultureInfo.InvariantCulture)} bytes")),
			("Recovering", MarkupText.Plain(plan.Refusal ?? (plan.Notes.Count == 0 ? "puts it back as it was" : string.Join(' ', plan.Notes))))
		];
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain($"Recover entry #{entry.Objid.Number}"), ServerLayout.KeyValues(fields)),
			78, parser.HouseLook());
	}

	/// <summary><c>@recover</c>, with the RhostMUSH @reclist filters as switches.</summary>
	private async ValueTask<MString> RecoverListAsync(IMUSHCodeParser parser, AnySharpObject executor, string[] switches,
		string argument)
	{
		if (await RecoverFilterAsync(parser, executor, switches, argument) is not Func<RecoverEntry, bool> filter)
			return MarkupText.Plain(RecoverFilterUsage);

		var entries = await RecoverList.VisibleAsync(executor).Where(filter).ToListAsync();
		if (switches.Contains("COUNT") || entries.Count == 0)
			return MarkupText.Plain(entries.Count == 1 ? "1 destroyed object is on the recover list." : $"{entries.Count} destroyed objects are on the recover list.");

		List<IEnumerable<string>> rows = [];
		foreach (var entry in entries)
		{
			rows.Add([
				$"#{entry.Objid.Number}", entry.Name, entry.Type, await RecoverRelationAsync(entry.Owner),
				Day(entry.DestroyedAt), await RecoverUntilAsync(entry)
			]);
		}

		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Dbref")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Name")) { Min = 10 },
				new TableColumn(MarkupText.Plain("Type")) { Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("Owner")) { Priority = 3 },
				new TableColumn(MarkupText.Plain("Destroyed")) { Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("Until")) { Wrap = false }
			],
			rows);
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain($"Recover list ({entries.Count})"), table), 78, parser.HouseLook());
	}

	private const string RecoverFilterUsage =
		"@recover/type takes PLAYER, ROOM, THING or EXIT; /owner a player; /age a number of days; /list a name pattern.";

	/// <summary>Which entries a listing or a bulk purge covers, or null when the argument does not fit the switch.</summary>
	private async ValueTask<Func<RecoverEntry, bool>?> RecoverFilterAsync(IMUSHCodeParser parser, AnySharpObject executor,
		string[] switches, string argument)
	{
		if (switches.Contains("TYPE"))
		{
			var type = argument.ToUpperInvariant();
			return type is "PLAYER" or "ROOM" or "THING" or "EXIT" ? entry => entry.Type == type : null;
		}

		if (switches.Contains("OWNER"))
		{
			var owner = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, argument, LocateFlags.PlayersPreference)
				is AnySharpObject { IsPlayer: true } player ? player.Object().DBRef : (DBRef?)null;
			return owner is { } objid ? entry => entry.Owner is { } former && former.Equals(objid) : null;
		}

		if (switches.Contains("AGE") || switches.Contains("TIME"))
		{
			if (!int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var days)) return null;
			var before = DateTimeOffset.UtcNow.AddDays(-days);
			return entry => entry.DestroyedAt <= before;
		}

		if (argument.Length > 0)
		{
			var pattern = new Regex(argument.GlobToRegex(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
			return entry => pattern.IsMatch(entry.Name);
		}

		return _ => true;
	}

	/// <summary>
	/// <c>@purge</c> with an object or a bulk switch: removes recover entries for good. A bulk purge lists what it
	/// would remove until it is repeated with /confirm, and copies the world first; a kept entry goes only by name,
	/// with /confirm.
	/// </summary>
	private async ValueTask<MString> PurgeRecoverListAsync(IMUSHCodeParser parser, AnySharpObject executor, string[] switches,
		string argument)
	{
		var confirm = switches.Contains("CONFIRM");
		if (!switches.Any(PurgeBulkSwitches.Contains))
		{
			if (await RecoverEntryAsync(argument) is not RecoverEntry entry)
				return MarkupText.Plain($"Nothing destroyed is held under {argument}. @recover lists what is.");

			if (await RecoverList.IsKeptAsync(entry) && !confirm)
				return MarkupText.Plain($"{entry.Name}(#{entry.Objid.Number}) is kept: "
					+ $"{entry.Keep?.Reason ?? "a package records it"}. @purge/confirm #{entry.Objid.Number} removes it anyway.");

			return MarkupText.Plain(await RecoverList.PurgeAsync(executor, entry, evenIfKept: confirm)
				? $"{entry.Name}(#{entry.Objid.Number}) is gone for good."
				: $"{entry.Name}(#{entry.Objid.Number}) could not be purged.");
		}

		if (await RecoverFilterAsync(parser, executor, switches, switches.Contains("ALL") ? string.Empty : argument)
			is not Func<RecoverEntry, bool> filter)
		{
			return MarkupText.Plain("@purge/time takes a number of days; /type PLAYER, ROOM, THING or EXIT; /owner a player.");
		}

		List<RecoverEntry> chosen = [];
		var kept = 0;
		await foreach (var entry in Mediator.CreateStream(new GetRecoverEntriesQuery()).Where(filter))
		{
			if (await RecoverList.IsKeptAsync(entry)) kept++;
			else chosen.Add(entry);
		}

		var skipped = kept == 0 ? string.Empty : $" {kept} kept {(kept == 1 ? "entry is" : "entries are")} left alone.";
		if (chosen.Count == 0) return MarkupText.Plain($"No recover entries match.{skipped}");

		if (!confirm)
		{
			var named = chosen.Take(PurgePreviewLines).Select(entry => $"  #{entry.Objid.Number} {entry.Name} ({entry.Type}, {Day(entry.DestroyedAt)})");
			var more = chosen.Count > PurgePreviewLines ? [$"  ...and {chosen.Count - PurgePreviewLines} more."] : Array.Empty<string>();
			return MarkupText.Plain(string.Join('\n',
			[
				$"This would remove {chosen.Count} recover {(chosen.Count == 1 ? "entry" : "entries")} for good:", .. named, .. more,
				$"Nothing has been removed.{skipped} Repeat the command with /confirm to remove them; the world is copied first."
			]));
		}

		if (WorldBackupService is { IsSupported: true, PackageOperationKeep: > 0 })
		{
			var provenance = new BackupProvenance("pre-purge", executor.Object().DBRef.ToString(), "purge",
				$"{chosen.Count} recover entries");
			if (await WorldBackupService.CreateBeforePackageOperationAsync(provenance, ExecutionBudget.CurrentToken) is Error<string> failed)
				return MarkupText.Plain($"The world could not be copied before the purge ({failed.Value}), so nothing was removed.");
		}

		var purged = 0;
		foreach (var entry in chosen)
		{
			if (await RecoverList.PurgeAsync(executor, entry)) purged++;
		}

		return MarkupText.Plain($"Removed {purged} recover {(purged == 1 ? "entry" : "entries")} for good.{skipped}");
	}

	/// <summary>The entry an argument names: <c>#N</c> or an objid.</summary>
	private async ValueTask<RecoverEntry?> RecoverEntryAsync(string target)
		=> DBRef.TryParse(target, out var parsed) && parsed is { } number
			&& await Mediator.Send(new GetRecoverEntryQuery(number.Number)) is RecoverEntry entry
			&& (number.CreationMilliseconds is null || number.Equals(entry.Objid))
				? entry
				: null;

	private async ValueTask<bool> MaySeeRecoverEntryAsync(AnySharpObject viewer, RecoverEntry entry)
		=> await RecoverList.MaySeeAllAsync(viewer) || await RecoverList.MayRecoverAsync(viewer, entry);

	/// <summary>An object an entry recorded: its name and number while it is still the same object, or its number marked gone.</summary>
	private async ValueTask<string> RecoverRelationAsync(DBRef? objid)
		=> objid is not { } recorded
			? "none"
			: await Mediator.Send(new GetObjectNodeQuery(recorded)) is AnySharpObject live
				? $"{live.Object().Name}(#{recorded.Number})"
				: await Mediator.Send(new GetRecoverEntryQuery(recorded.Number)) is RecoverEntry destroyed && destroyed.Objid.Equals(recorded)
					? $"#{recorded.Number} (destroyed)"
					: $"#{recorded.Number} (gone)";

	/// <summary>When an entry expires: kept, forever, or the day the age pass removes it.</summary>
	private async ValueTask<string> RecoverUntilAsync(RecoverEntry entry)
		=> await RecoverList.IsKeptAsync(entry)
			? "kept"
			: RecoverList.RetentionFor(entry.Type) is { } keep ? Day(entry.DestroyedAt + keep) : "forever";

	private static string Day(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
