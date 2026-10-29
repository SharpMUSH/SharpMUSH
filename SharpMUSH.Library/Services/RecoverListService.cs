using System.Runtime.CompilerServices;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public class RecoverListService(
	IMediator mediator,
	IPermissionService permissions,
	IExpandedObjectDataService expandedData,
	IPackageRegistryService packages,
	IAuditLog audit,
	IEnumerable<IFreedObjectCleanup> cleanups,
	IOptionsMonitor<SharpMUSHOptions> configuration,
	ILogger<RecoverListService> logger) : IRecoverListService
{
	private const string Player = "PLAYER";
	private const string Room = "ROOM";
	private const string Exit = "EXIT";

	/// <inheritdoc />
	public TimeSpan? RetentionFor(string type)
	{
		var dump = configuration.CurrentValue.Dump;
		var setting = type == Player ? dump.RecoverKeepPlayers : dump.RecoverKeepObjects;
		return RecoverRetention.TryParse(setting, out var keep) ? keep : null;
	}

	/// <inheritdoc />
	public async ValueTask<bool> MaySeeAllAsync(AnySharpObject viewer) => await viewer.IsSee_All();

	/// <inheritdoc />
	public async IAsyncEnumerable<RecoverEntry> VisibleAsync(AnySharpObject viewer,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		var all = await MaySeeAllAsync(viewer);
		var owner = (await viewer.Object().Owner.WithCancellation(ct)).Object.DBRef;
		await foreach (var entry in mediator.CreateStream(new GetRecoverEntriesQuery(), ct))
		{
			if (all || entry.Owner is { } former && former.Equals(owner)) yield return entry;
		}
	}

	/// <inheritdoc />
	public async ValueTask<bool> MayRecoverAsync(AnySharpObject executor, RecoverEntry entry)
		=> await executor.IsWizard()
			|| (await LiveAsync(entry.Owner, CancellationToken.None) is { } owner && await permissions.Controls(executor, owner));

	/// <inheritdoc />
	public async ValueTask<RecoverPlan> PlanAsync(RecoverEntry entry, AnySharpObject recoverer, CancellationToken ct = default)
	{
		List<string> notes = [];
		var recovererOwner = (await recoverer.Object().Owner.WithCancellation(ct)).Object.DBRef;

		// A character owns itself; anything else goes back to its owner, or to whoever recovers it.
		var owner = entry.Type == Player
			? entry.Objid
			: await LiveAsync(entry.Owner, ct) is { } formerOwner
				? formerOwner.Object().DBRef
				: Noted(recovererOwner, $"Its owner {Name(entry.Owner)} is gone, so {Name(recovererOwner)} owns it now.");

		var parent = await LiveRelationAsync(entry.Parent, "parent", ct);
		var zone = await LiveRelationAsync(entry.Zone, "zone", ct);

		DBRef? location = null;
		DBRef? home;
		string? refusal = null;
		switch (entry.Type)
		{
			case Room:
				home = await LiveContainerAsync(entry.Home, ct);
				if (entry.Home is not null && home is null) notes.Add($"Its drop-to {Name(entry.Home)} is gone; it has none now.");
				break;
			case Exit:
				location = await LiveAsync(entry.Location, ct) is { IsRoom: true } source ? source.Object().DBRef : null;
				if (location is null)
					refusal = $"Its room {Name(entry.Location)} is gone. Recover the room first; @recover/exits on it brings its exits back.";
				home = await LiveContainerAsync(entry.Home, ct);
				if (entry.Home is not null && home is null) notes.Add($"Its destination {Name(entry.Home)} is gone; it is unlinked.");
				break;
			default:
				// A thing or a character goes to its home. Without one, a character goes to default_home and a thing
				// to whoever recovers it.
				home = await LiveContainerAsync(entry.Home, ct);
				if (home is null)
				{
					home = entry.Type == Player
						? new DBRef((int)configuration.CurrentValue.Database.DefaultHome)
						: await RecovererContainerAsync(recoverer, ct);
					notes.Add($"Its home {Name(entry.Home)} is gone, so its home is {Name(home)} now.");
				}

				location = home;
				break;
		}

		if (entry.Type == Player)
		{
			notes.Add("What it owned and the attributes it set elsewhere stay with the probate player; the mail it had received was deleted.");
			if (entry.Account is not null) notes.Add("It is linked to its account again if that account still exists.");
		}
		else if (entry.Type != Exit)
		{
			notes.Add("Anything that was inside it when it was destroyed was sent home and stays there.");
		}

		if (entry.Type == Room)
		{
			notes.Add("Its exits were destroyed with it; @recover/exits brings them back.");
		}

		return new RecoverPlan(new RecoverRestore(entry.Objid, owner, location, home, parent, zone, RelinkAccount: true),
			notes, refusal);

		DBRef Noted(DBRef value, string note)
		{
			notes.Add(note);
			return value;
		}

		async ValueTask<DBRef?> LiveRelationAsync(DBRef? recorded, string what, CancellationToken token)
		{
			if (recorded is null) return null;
			if (await LiveAsync(recorded, token) is { } live) return live.Object().DBRef;
			notes.Add($"Its {what} {Name(recorded)} is gone; it has none now.");
			return null;
		}
	}

	/// <inheritdoc />
	public async ValueTask<Result<RecoverOutcome>> RecoverAsync(AnySharpObject executor, RecoverEntry entry,
		CancellationToken ct = default)
	{
		var plan = await PlanAsync(entry, executor, ct);
		if (plan.Refusal is { } refusal) return new Error<string>(refusal);

		return await mediator.Send(new RestoreRecoverEntryCommand(plan.Restore), ct) switch
		{
			DBRef objid => await RecoveredAsync(executor, objid, plan.Notes, ct),
			Error<string> error => error
		};
	}

	/// <summary>Quiets a recovered object until someone looks at it, and records the recovery.</summary>
	private async ValueTask<Result<RecoverOutcome>> RecoveredAsync(AnySharpObject executor, DBRef objid,
		IReadOnlyList<string> notes, CancellationToken ct)
	{
		if (await mediator.Send(new GetObjectNodeQuery(objid), ct) is AnySharpObject recovered)
		{
			// HALT, as RhostMUSH sets it: its $-commands, listens and @startup stay quiet until someone looks.
			if (await mediator.Send(new GetObjectFlagQuery("HALT"), ct) is { } halt)
			{
				await mediator.Send(new SetObjectFlagCommand(recovered, halt), ct);
			}

			await expandedData.SetExpandedDataAsync(new DestroyedByData(), recovered.Object());
			await audit.RecordAsync(executor, AuditActions.ObjectRecover, AuditTargets.Of(recovered), "from the recover list", ct);
		}

		return new RecoverOutcome(objid, notes);
	}

	/// <inheritdoc />
	public async ValueTask<bool> SetKeepAsync(AnySharpObject executor, RecoverEntry entry, string? reason,
		CancellationToken ct = default)
	{
		var keep = reason is null ? null : new RecoverKeep(executor.Object().DBRef, reason, DateTimeOffset.UtcNow);
		if (!await mediator.Send(new SetRecoverKeepCommand(entry.Objid, keep), ct)) return false;

		await audit.RecordAsync(executor, keep is null ? AuditActions.RecoverUnkeep : AuditActions.RecoverKeep,
			Target(entry), reason, ct);
		return true;
	}

	/// <inheritdoc />
	public async ValueTask<bool> IsKeptAsync(RecoverEntry entry, CancellationToken ct = default)
		=> entry.Keep is not null || (await PackageObjidsAsync()).Contains(entry.Objid.ToString());

	/// <inheritdoc />
	public async ValueTask<bool> PurgeAsync(AnySharpObject? actor, RecoverEntry entry, CancellationToken ct = default)
	{
		if (!await mediator.Send(new PurgeRecoverEntryCommand(entry.Objid), ct)) return false;

		foreach (var cleanup in cleanups)
		{
			await cleanup.ObjectsFreedAsync([entry.Objid], ct);
		}

		var details = $"{entry.Type.ToLowerInvariant()} destroyed {entry.DestroyedAt:yyyy-MM-dd}";
		if (actor is null)
			await audit.RecordSystemAsync(AuditActions.RecoverPurge, Target(entry), details + ", past its retention", ct);
		else
			await audit.RecordAsync(actor, AuditActions.RecoverPurge, Target(entry), details, ct);
		return true;
	}

	/// <inheritdoc />
	public async ValueTask<int> ExpireAsync(DateTimeOffset now, CancellationToken ct = default)
	{
		var named = await PackageObjidsAsync();
		var expired = await mediator.CreateStream(new GetRecoverEntriesQuery(), ct)
			.Where(entry => entry.Keep is null && !named.Contains(entry.Objid.ToString())
				&& RetentionFor(entry.Type) is { } keep && entry.DestroyedAt + keep <= now)
			.ToListAsync(ct);

		var purged = 0;
		foreach (var entry in expired)
		{
			if (await PurgeAsync(null, entry, ct)) purged++;
		}

		if (purged > 0) logger.LogInformation("Purged {Count} recover entries past their retention", purged);
		return purged;
	}

	/// <summary>The objids every installed package records, whose entries stay for a package rollback to find.</summary>
	private async ValueTask<HashSet<string>> PackageObjidsAsync()
	{
		HashSet<string> objids = new(StringComparer.Ordinal);
		foreach (var package in await packages.GetInstalledPackagesAsync())
		{
			objids.UnionWith((await packages.GetPackageObjectsAsync(package.Id)).Select(record => record.Objid));
		}

		return objids;
	}

	/// <summary>The object an objid names, when it is still the object it was.</summary>
	private async ValueTask<AnySharpObject?> LiveAsync(DBRef? objid, CancellationToken ct)
		=> objid is { } recorded && await mediator.Send(new GetObjectNodeQuery(recorded), ct) is AnySharpObject live
			? live
			: null;

	private async ValueTask<DBRef?> LiveContainerAsync(DBRef? objid, CancellationToken ct)
		=> await LiveAsync(objid, ct) is { IsExit: false } live ? live.Object().DBRef : null;

	/// <summary>Where a thing whose home is gone goes: into whoever recovers it, or an exit's room.</summary>
	private static async ValueTask<DBRef> RecovererContainerAsync(AnySharpObject recoverer, CancellationToken ct)
		=> recoverer is SharpExit exit
			? (await exit.Location.WithCancellation(ct)).Object().DBRef
			: recoverer.Object().DBRef;

	private static string Name(DBRef? objid) => objid is { } known ? $"#{known.Number}" : "(none)";

	private static AuditTarget Target(RecoverEntry entry)
		=> new(entry.Type == Player ? AuditTargetKinds.Character : AuditTargetKinds.Object,
			entry.Objid.ToString(), entry.Name);
}
