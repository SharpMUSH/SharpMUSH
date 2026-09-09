using DotNext.Threading;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mediator;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Snapshots;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services.Snapshots;

/// <summary>
/// Per-object, bounded snapshot histories use the existing provider-neutral expanded-data store
/// and therefore travel with database backups. Writes are serialized within this server. Restore
/// is deliberately recoverable rather than claiming a transaction across Mediator mutations:
/// persist the before-image and pending marker first; retain it until an operator recovers.
/// </summary>
public sealed partial class ObjectSnapshotService(
	IObjectStore objects, IAttributeStore attributes, IExpandedDataStore expanded,
	IAdministrativeCapabilityService capabilities, IPermissionService permissions,
	IAttributeService attributeService, IManipulateSharpObjectService manipulation,
	ILockService locks, IMediator mediator) : IObjectSnapshotService
{
	public const string StorageKey = "sharpmush.object-snapshots.v1";
	private const int MaxAttributes = 1024;
	private const int MaxBytes = 2 * 1024 * 1024;
	private readonly SemaphoreSlim _writes = new(1, 1);
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public async Task<SnapshotHistory> ListAsync(CapabilityActor actor, DBRef target, CancellationToken ct = default)
	{
		var scope = await capabilities.AuthorizeAsync(actor, PortalPermission.SnapshotCapture, ct)
			? PortalPermission.SnapshotCapture : PortalPermission.SnapshotRestore;
		var (executor, obj) = await Authorize(actor, target, scope, ct);
		var history = await Read(obj, ct);
		var visible = new List<ObjectSnapshot>();
		var reads = new ReadContext(objects, attributes, obj.Object().DBRef);
		foreach (var saved in history.Snapshots.Where(s => s.CreatorAccount == actor.AccountId))
		{
			Find(history, saved.Id, obj, actor.AccountId);
			if (await CanRead(executor, obj, saved, reads, ct)) visible.Add(saved);
		}
		return history with { Snapshots = visible.ToArray() };
	}

	public async Task<ObjectSnapshot> CaptureAsync(CapabilityActor actor, DBRef target, string description, int retain = 10, CancellationToken ct = default)
	{
		if (retain is < 1 or > 20 || description is null || description.Length > 500) throw Error("invalid", "Retention must be 1–20 and description at most 500 characters.");
		await _writes.WaitAsync(ct);
		try
		{
			var (executor, obj) = await Authorize(actor, target, PortalPermission.SnapshotCapture, ct);
			var history = await Read(obj, ct);
			var snapshot = await Capture(actor, executor, obj, description, retain, ct);
			await Save(obj, Append(history, snapshot), ct);
			return snapshot;
		}
		finally { _writes.Release(); }
	}

	public async Task<SnapshotPreview> PreviewAsync(CapabilityActor actor, DBRef target, string snapshotId, SnapshotSelection selection, CancellationToken ct = default)
	{
		var (executor, obj) = await Authorize(actor, target, PortalPermission.SnapshotRestore, ct);
		var snapshot = Find(await Read(obj, ct), snapshotId, obj, actor.AccountId);
		selection = NormalizeSelection(selection);
		var reads = new ReadContext(objects, attributes, obj.Object().DBRef);
		await ValidateSelection(executor, obj, snapshot, selection, reads, ct);
		var current = await Capture(actor, executor, obj, "preview", snapshot.Retain, ct, selection, snapshot.Locks.Keys.Concat(snapshot.AbsentLocks).ToHashSet(StringComparer.Ordinal), reads);
		return Preview(snapshot, current, selection);
	}

	public async Task<SnapshotRestoreResult> RestoreAsync(CapabilityActor actor, DBRef target, string snapshotId, SnapshotSelection selection, string previewToken, CancellationToken ct = default)
	{
		await _writes.WaitAsync(ct);
		try
		{
			var (executor, obj) = await Authorize(actor, target, PortalPermission.SnapshotRestore, ct);
			var history = await Read(obj, ct);
			var snapshot = Find(history, snapshotId, obj, actor.AccountId);
			selection = NormalizeSelection(selection);
			var reads = new ReadContext(objects, attributes, obj.Object().DBRef);
			await ValidateSelection(executor, obj, snapshot, selection, reads, ct);
			var before = await Capture(actor, executor, obj, "Before restore " + snapshot.Id, history.Snapshots.FirstOrDefault()?.Retain ?? snapshot.Retain, ct, selection, snapshot.Locks.Keys.Concat(snapshot.AbsentLocks).ToHashSet(StringComparer.Ordinal), reads);
			if (Preview(snapshot, before, selection).Token != previewToken)
				throw Error("stale-preview", "The object or selection changed. Preview again before restoring.");
			if (history.PendingRecoveryId is not null && history.PendingRecoveryId != snapshot.Id)
				throw Error("recovery-required", "Recover the pending before-image before starting another restore.");
			before = before with
			{
				AbsentAttributes = selection.Attributes.SelectMany(name => name.Split('`').Select((_, index) => string.Join('`', name.Split('`').Take(index + 1))))
					.Except(before.Attributes.Select(a => a.Name)).ToArray(),
				AbsentLocks = selection.Locks ? snapshot.Locks.Keys.Concat(snapshot.AbsentLocks).Except(before.Locks.Keys).ToArray() : [],
				RecoverySelection = selection,
				Locks = before.Locks.Where(p => selection.Locks && (snapshot.Locks.ContainsKey(p.Key) || snapshot.AbsentLocks.Contains(p.Key))).ToDictionary(p => p.Key, p => p.Value)
			};
			before = FinalizeImage(before);
			history = Append(history, before) with { PendingRecoveryId = before.Id, LastRestoreError = null };
			// Once this durable write succeeds, every partial mutation has a retained recovery image.
			await Save(obj, history, ct);
			try
			{
				await Apply(actor, executor, obj, snapshot, selection, ct);
				await Save(obj, history with { PendingRecoveryId = null }, ct);
				return new(true, before.Id, null);
			}
			catch (Exception ex)
			{
				// Best effort diagnostic. The pending marker was committed before any object writes,
				// so a crash or storage outage still leaves a discoverable recovery state.
				var reason = ex is SnapshotOperationException ? ex.Message : "A mutation or storage operation failed.";
				try { await Save(obj, history with { LastRestoreError = reason }, CancellationToken.None); }
				catch { /* The previously committed pending marker remains authoritative. */ }
				return new(false, before.Id, "Restore stopped; preview and restore the retained recovery snapshot. " + reason);
			}
		}
		finally { _writes.Release(); }
	}

	public async Task ResolveRecoveryAsync(CapabilityActor actor, DBRef target, string recoverySnapshotId, CancellationToken ct = default)
	{
		await _writes.WaitAsync(ct);
		try
		{
			var (_, obj) = await Authorize(actor, target, PortalPermission.SnapshotRestore, ct);
			var history = await Read(obj, ct);
			if (history.PendingRecoveryId is null || history.PendingRecoveryId != recoverySnapshotId)
				throw Error("stale-preview", "The pending recovery changed. Reload before acknowledging it.");
			// Explicitly accept the current object; retain its before-image without exposing it
			// to a new account. This permits recovery after an account or ownership transition.
			await Save(obj, history with
			{
				PendingRecoveryId = null,
				LastRestoreError = null,
				LastResolution = new(recoverySnapshotId, actor.AccountId, actor.ActiveCharacter!.Value.ToString(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
			}, ct);
		}
		finally { _writes.Release(); }
	}

	private async Task<(AnySharpObject Executor, AnySharpObject Object)> Authorize(CapabilityActor actor, DBRef target, string scope, CancellationToken ct)
	{
		if (!target.IsObjid || actor.ActiveCharacter is not { IsObjid: true } active ||
			!await capabilities.AuthorizeAsync(actor, scope, ct)) throw Error("denied", "A linked active player with the required capability is required.");
		var executor = await objects.GetObjectNodeAsync(active, ct);
		var obj = await objects.GetObjectNodeAsync(target, ct);
		if (!executor.IsPlayer || executor.AsPlayer.Object.DBRef != active || obj.IsNone || obj.Known.Object().DBRef != target)
			throw Error("missing", "The player or object identity no longer exists.");
		if (obj.IsPlayer) throw Error("invalid", "Player objects, credentials and account relationships are excluded from snapshots.");
		if (!await permissions.Controls(executor.Known, obj.Known)) throw Error("denied", "The active player must control the object.");
		return (executor.Known, obj.Known);
	}

	private async Task<ObjectSnapshot> Capture(CapabilityActor actor, AnySharpObject executor, AnySharpObject obj, string description, int retain, CancellationToken ct, SnapshotSelection? selection = null, IReadOnlySet<string>? lockNames = null, ReadContext? reads = null)
	{
		reads ??= new(objects, attributes, obj.Object().DBRef);
		var selectedNames = selection?.Attributes.SelectMany(name => name.Split('`').Select((_, index) => string.Join('`', name.Split('`').Take(index + 1)))).ToHashSet(StringComparer.Ordinal);
		var captured = new List<SnapshotAttribute>();
		var capturedBytes = 0;
		await foreach (var attribute in attributes.GetAttributesAsync(obj.Object().DBRef, "**", ct))
		{
			if (selectedNames is not null && !selectedNames.Contains(attribute.LongName)) continue;
			var path = await reads.Path(attribute.LongName, ct);
			if (!await permissions.CanViewAttribute(executor, obj, path)) continue;
			if (captured.Count == MaxAttributes) throw Error("limit", "Snapshot exceeds 1024 attributes.");
			var markup = MarkupTextSerializer.Serialize(attribute.Value);
			capturedBytes += Encoding.UTF8.GetByteCount(markup);
			if (capturedBytes > MaxBytes) throw Error("limit", "Snapshot exceeds 2 MiB.");
			var owner = await attribute.Owner.WithCancellation(ct);
			if (owner is null) throw Error("missing", "An attribute owner is missing.");
			var ancestors = new List<SnapshotAccess>();
			foreach (var ancestor in path.Where(a => a.LongName != attribute.LongName))
			{
				var ancestorOwner = await ancestor.Owner.WithCancellation(ct) ?? throw Error("missing", "An ancestor owner is missing.");
				ancestors.Add(new(ancestor.LongName, ancestor.Flags.Select(f => f.Name).Order().ToArray(), ancestorOwner.Object.DBRef.ToString()));
			}
			captured.Add(new(attribute.LongName, markup, attribute.Flags.Select(f => f.Name).Order().ToArray(), owner.Object.DBRef.ToString()) { Ancestors = ancestors.ToArray() });
		}
		var lockData = new Dictionary<string, SnapshotLock>(StringComparer.Ordinal);
		foreach (var (name, value) in obj.Object().Locks.OrderBy(p => p.Key))
			if ((selection is null || selection.Locks && lockNames?.Contains(name) == true) && await permissions.CanReadLock(executor, obj, value.Flags)) lockData[name] = new(value.LockString, (int)value.Flags);
		var snapshot = new ObjectSnapshot(Guid.NewGuid().ToString("N"), 1, obj.Object().DBRef.ToString(), obj.Object().Type,
			actor.AccountId, actor.ActiveCharacter!.Value.ToString(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), description, retain,
			selection is null || selection.Name ? obj.Object().Name : "", captured.OrderBy(a => a.Name).ToArray(), lockData,
			selection is null || selection.Flags ? (await obj.Object().Flags.Value.ToListAsync(ct)).Where(f => f.Name != obj.Object().Type).Select(f => f.Name).Order().ToArray() : [], "");
		return FinalizeImage(snapshot);
	}

	private static ObjectSnapshot FinalizeImage(ObjectSnapshot snapshot)
	{
		var finalized = snapshot with { Digest = Hash(JsonSerializer.Serialize(snapshot with { Digest = "" }, Json)) };
		if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(finalized, Json)) > MaxBytes) throw Error("limit", "Snapshot exceeds 2 MiB.");
		return finalized;
	}

	private static ObjectSnapshot Find(SnapshotHistory history, string id, AnySharpObject obj, string accountId)
	{
		var snapshot = history.Snapshots.SingleOrDefault(s => s.Id == id && s.CreatorAccount == accountId) ?? throw Error("missing", "Snapshot not found.");
		if (snapshot.Attributes is null || snapshot.Locks is null || snapshot.Flags is null || snapshot.Attributes.Length > MaxAttributes || snapshot.Retain is < 1 or > 20 ||
			snapshot.AbsentAttributes is null || snapshot.AbsentLocks is null || snapshot.RecoverySelection is { Attributes: null } ||
			snapshot.Attributes.Any(a => a is null || a.Flags is null || a.Name is null || a.Markup is null || a.Ancestors is null || a.Ancestors.Any(v => v is null || v.Name is null || v.Flags is null || v.Owner is null)) ||
			snapshot.Attributes.Select(a => a.Name).Distinct(StringComparer.Ordinal).Count() != snapshot.Attributes.Length ||
			snapshot.AbsentAttributes.Intersect(snapshot.Attributes.Select(a => a.Name)).Any() || snapshot.AbsentLocks.Intersect(snapshot.Locks.Keys).Any() ||
			snapshot.Locks.Any(p => p.Value is null || p.Value.Expression is null) ||
			Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(snapshot, Json)) > MaxBytes ||
			snapshot.SchemaVersion != 1 || snapshot.ObjectId != obj.Object().DBRef.ToString() || snapshot.ObjectType != obj.Object().Type ||
			snapshot.Digest != Hash(JsonSerializer.Serialize(snapshot with { Digest = "" }, Json)))
			throw Error("corrupt", "Snapshot schema, digest, type or stable object identity is invalid.");
		return snapshot;
	}

	private async Task ValidateSelection(AnySharpObject executor, AnySharpObject obj, ObjectSnapshot snapshot, SnapshotSelection selection, ReadContext reads, CancellationToken ct)
	{
		if (snapshot.RecoverySelection is { } required &&
			(required.Attributes.Concat(snapshot.AbsentAttributes).Except(selection.Attributes ?? []).Any() ||
				(selection.Attributes ?? []).Except(required.Attributes.Concat(snapshot.AbsentAttributes)).Any() ||
				required.Locks != selection.Locks || required.Flags != selection.Flags || required.Name != selection.Name))
			throw Error("invalid", "Recovery must select exactly the fields from the interrupted restore.");
		if (!await CanRead(executor, obj, snapshot, reads, ct)) throw Error("denied", "The snapshot contains fields the active player cannot currently read.");
		if (selection.Attributes is null || selection.Attributes.Distinct(StringComparer.Ordinal).Count() != selection.Attributes.Length ||
			selection.Attributes.Any(name => !snapshot.Attributes.Any(a => a.Name == name) && !snapshot.AbsentAttributes.Contains(name))) throw Error("invalid", "Select unique attributes present in the snapshot.");
		foreach (var name in selection.Attributes)
		{
			var chain = await reads.Path(name, ct);
			if (!await permissions.CanSet(executor, obj, chain)) throw Error("denied", "An attribute is protected: " + name);
			if (snapshot.Attributes.FirstOrDefault(a => a.Name == name) is { } savedAttribute)
				_ = MarkupTextSerializer.Deserialize(savedAttribute.Markup);
		}
		if (selection.Flags)
			foreach (var flag in snapshot.Flags)
				if (await mediator.Send(new GetObjectFlagQuery(flag), ct) is null) throw Error("missing", "An object flag no longer exists: " + flag);
		if (selection.Locks)
			foreach (var name in snapshot.AbsentLocks)
				ValidateLockWrite(obj, name);
		if (selection.Locks)
			foreach (var (name, value) in snapshot.Locks)
			{
				foreach (Match reference in LockReferences().Matches(value.Expression))
				{
					if (!DBRef.TryParse(reference.Value, out var objid) || objid is not { IsObjid: true } full)
						throw Error("invalid", "A lock reference lacks a stable creation identity.");
					var referred = await objects.GetObjectNodeAsync(full, ct);
					if (referred.IsNone || referred.Known.Object().DBRef != full) throw Error("missing", "A lock references a missing object.");
				}
				if (!locks.Validate(value.Expression, obj)) throw Error("invalid", "Invalid lock expression: " + name);
				// Do not use snapshots to remove a lock's privileged write protection.
				ValidateLockWrite(obj, name, (LockService.LockFlags)value.Flags);
			}
	}

	private static SnapshotSelection NormalizeSelection(SnapshotSelection? selection)
	{
		if (selection?.Attributes is not { } names || names.Any(name => name is null))
			throw Error("invalid", "Select unique attributes present in the snapshot.");
		return selection with { Attributes = names.Select(name => name.ToUpperInvariant()).ToArray() };
	}

	// One operation, one object: authorization is still fresh for each request and each write.
	// Retain access metadata only, so large current attribute values do not fill the lookup cache.
	private sealed class ReadContext(IObjectStore objects, IAttributeStore attributes, DBRef target)
	{
		private readonly Dictionary<DBRef, SharpPlayer> owners = [];
		private readonly Dictionary<string, SharpAttributeFlag> flags = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, SharpAttribute[]> paths = new(StringComparer.OrdinalIgnoreCase);
		public async Task<SharpPlayer> Owner(DBRef identity, CancellationToken ct)
		{
			if (owners.TryGetValue(identity, out var cached)) return cached;
			var found = await objects.GetObjectNodeAsync(identity, ct);
			if (!found.IsPlayer || !found.AsPlayer.Object.DBRef.Equals(identity))
				throw Error("missing", "An attribute creator no longer exists.");
			return owners[identity] = found.AsPlayer;
		}
		public async Task<SharpAttributeFlag> Flag(string name, CancellationToken ct)
		{
			if (flags.TryGetValue(name, out var cached)) return cached;
			return flags[name] = await attributes.GetAttributeFlagAsync(name, ct)
				?? throw Error("missing", "An attribute flag no longer exists.");
		}
		public async Task<SharpAttribute[]> Path(string name, CancellationToken ct)
		{
			if (paths.TryGetValue(name, out var cached)) return cached;
			var path = await attributes.GetAttributeAsync(target, name.Split('`'), ct).ToArrayAsync(ct);
			return paths[name] = path.Select(attribute => attribute with { Value = MarkupText.Empty }).ToArray();
		}
	}

	private static void ValidateLockWrite(AnySharpObject obj, string name, LockService.LockFlags savedFlags = 0)
	{
		var protectedFlags = LockService.LockFlags.Wizard | LockService.LockFlags.Locked | LockService.LockFlags.Owner;
		if (((savedFlags | obj.Object().Locks.GetValueOrDefault(name, new()).Flags) & protectedFlags) != 0)
			throw Error("denied", "Protected locks require their normal administrative workflow: " + name);
	}

	private async Task<bool> CanRead(AnySharpObject executor, AnySharpObject obj, ObjectSnapshot saved, ReadContext reads, CancellationToken ct)
	{
		foreach (var attribute in saved.Attributes)
		{
			var historicalPath = new List<SharpAttribute>();
			foreach (var value in attribute.Ancestors.Append(new SnapshotAccess(attribute.Name, attribute.Flags, attribute.Owner)))
			{
				if (!DBRef.TryParse(value.Owner, out var ownerRef) || ownerRef is not { IsObjid: true } full) throw Error("corrupt", "Invalid attribute creator identity.");
				var owner = await reads.Owner(full, ct);
				var flags = new List<SharpAttributeFlag>();
				foreach (var name in value.Flags)
					flags.Add(await reads.Flag(name, ct));
				var historical = new SharpAttribute("", "", value.Name, flags, null, value.Name,
					new(_ => Task.FromResult(Array.Empty<SharpAttribute>().ToAsyncEnumerable())),
					new(_ => Task.FromResult<SharpPlayer?>(owner)), new(_ => Task.FromResult<SharpAttributeEntry?>(null)));
				historicalPath.Add(historical);
			}
			if (!await permissions.CanViewAttribute(executor, obj, historicalPath.ToArray())) return false;
			var current = await reads.Path(attribute.Name, ct);
			if (current.Length > 0 && !await permissions.CanViewAttribute(executor, obj, current)) return false;
		}
		foreach (var (name, value) in saved.Locks)
			if (!await permissions.CanReadLock(executor, obj, (LockService.LockFlags)value.Flags) ||
				!await permissions.CanReadLock(executor, obj, obj.Object().Locks.GetValueOrDefault(name, new()).Flags)) return false;
		return true;
	}

	[GeneratedRegex(@"(?<![:\w])#\d+(?::\d+)?", RegexOptions.CultureInvariant)]
	private static partial Regex LockReferences();

	private async Task Apply(CapabilityActor actor, AnySharpObject executor, AnySharpObject obj, ObjectSnapshot snapshot, SnapshotSelection selection, CancellationToken ct)
	{
		// Selection builds operations; every operation passes the same unconditional fresh gate.
		// Value and flag writes are separate so revocation between them is also observed.
		var mutations = new List<Func<Task>>();
		foreach (var name in selection.Attributes.OrderByDescending(name => name.Count(c => c == '`')))
		{
			if (snapshot.AbsentAttributes.Contains(name))
			{
				mutations.Add(async () =>
				{
					var cleared = await attributeService.ClearAttributeAsync(executor, obj, name, IAttributeService.AttributePatternMode.Exact);
					if (cleared.IsT1) throw Error("write-failed", cleared.AsT1.Value);
				});
				continue;
			}
			var saved = snapshot.Attributes.Single(a => a.Name == name);
			mutations.Add(async () =>
			{
				var result = await attributeService.SetAttributeAsync(executor, obj, name, MarkupTextSerializer.Deserialize(saved.Markup));
				if (result.IsT1) throw Error("write-failed", result.AsT1.Value);
			});
			mutations.Add(async () =>
			{
				var current = await attributes.GetAttributeAsync(obj.Object().DBRef, name.Split('`'), ct).LastAsync(ct);
				var flags = current.Flags.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
				var changes = flags.Except(saved.Flags).Select(f => "!" + f).Concat(saved.Flags.Except(flags)).ToArray();
				if (changes.Length == 0) return;
				var changed = await attributeService.SetAttributeFlagsAsync(executor, obj, name, changes);
				if (changed.IsT1) throw Error("write-failed", changed.AsT1.Value);
			});
		}
		if (selection.Locks)
		{
			foreach (var name in snapshot.AbsentLocks)
				mutations.Add(async () => { ValidateLockWrite(obj, name); await mediator.Send(new UnsetLockCommand(obj.Object(), name), ct); });
			foreach (var (name, saved) in snapshot.Locks)
				mutations.Add(async () => { ValidateLockWrite(obj, name, (LockService.LockFlags)saved.Flags); await mediator.Send(new SetLockCommand(obj.Object(), name, saved.Expression, executor) { Flags = (LockService.LockFlags)saved.Flags }, ct); });
		}
		if (selection.Flags)
		{
			var current = (await obj.Object().Flags.Value.ToListAsync(ct)).Where(f => f.Name != obj.Object().Type).Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
			foreach (var change in current.Except(snapshot.Flags).Select(f => "!" + f).Concat(snapshot.Flags.Except(current)))
				mutations.Add(async () =>
				{
					if ((await manipulation.SetOrUnsetFlag(executor, obj, change, false)).Message?.ToPlainText() != "1")
						throw Error("write-failed", "Flag change was rejected: " + change);
				});
		}
		if (selection.Name)
			mutations.Add(async () =>
			{
				var result = await manipulation.SetName(executor, obj, MarkupText.Plain(snapshot.Name), false);
				if (result.Message?.ToPlainText().StartsWith("#-", StringComparison.Ordinal) == true) throw Error("write-failed", "Name change rejected.");
			});
		foreach (var mutation in mutations)
		{
			(executor, obj) = await Authorize(actor, obj.Object().DBRef, PortalPermission.SnapshotRestore, ct);
			await mutation();
		}
	}

	private static SnapshotPreview Preview(ObjectSnapshot saved, ObjectSnapshot current, SnapshotSelection selection)
	{
		var changes = new List<SnapshotDifference>();
		foreach (var name in selection.Attributes)
		{
			var prior = current.Attributes.FirstOrDefault(a => a.Name == name);
			var desired = saved.Attributes.FirstOrDefault(a => a.Name == name);
			var before = prior is null ? "" : MarkupTextSerializer.Deserialize(prior.Markup).ToPlainText();
			var after = desired is null ? "[absent]" : MarkupTextSerializer.Deserialize(desired.Markup).ToPlainText();
			changes.Add(new("attribute:" + name, before, after, before == after && prior?.Markup != desired?.Markup));
			changes.Add(new("attribute-flags:" + name, string.Join(' ', prior?.Flags ?? []), string.Join(' ', desired?.Flags ?? [])));
		}
		if (selection.Name) changes.Add(new("name", current.Name, saved.Name));
		if (selection.Flags) changes.Add(new("flags", string.Join(' ', current.Flags), string.Join(' ', saved.Flags)));
		if (selection.Locks)
		{
			var merged = new Dictionary<string, SnapshotLock>(current.Locks);
			foreach (var name in saved.AbsentLocks) merged.Remove(name);
			foreach (var (name, value) in saved.Locks) merged[name] = value;
			changes.Add(new("locks", JsonSerializer.Serialize(current.Locks, Json), JsonSerializer.Serialize(merged, Json)));
		}
		// Exclude capture identity/time: the token binds the affected current content, destination and selection.
		var state = new { current.ObjectId, current.ObjectType, current.Name, current.Attributes, current.Locks, current.Flags, saved.Digest, selection };
		return new(saved.Id, current.ObjectId, Hash(JsonSerializer.Serialize(state, Json)), selection, changes.ToArray());
	}

	private async Task<SnapshotHistory> Read(AnySharpObject obj, CancellationToken ct)
	{
		try
		{
			var document = await expanded.GetExpandedObjectData<SnapshotStorageRecord>(obj.Object().Id!, StorageKey, ct);
			var history = document is null ? new SnapshotHistory([]) : document.History ?? throw Error("corrupt", "Missing snapshot history.");
			if (history.Snapshots is null || history.Snapshots.Length > 21 || history.Snapshots.Any(s => s is null) ||
				history.Snapshots.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != history.Snapshots.Length ||
				(history.PendingRecoveryId is not null && !history.Snapshots.Any(s => s.Id == history.PendingRecoveryId))) throw Error("corrupt", "Invalid snapshot history.");
			return history;
		}
		catch (JsonException) { throw Error("corrupt", "The stored snapshot document is corrupt."); }
	}
	private async Task Save(AnySharpObject obj, SnapshotHistory history, CancellationToken ct)
		=> await expanded.SetExpandedObjectData(obj.Object().Id!, StorageKey, new SnapshotStorageRecord(history), ct);
	private static SnapshotHistory Append(SnapshotHistory history, ObjectSnapshot snapshot)
		=> history with
		{
			Snapshots = history.Snapshots.Prepend(snapshot).OrderByDescending(s => s.CreatedAt)
			.Where((s, index) => index < snapshot.Retain || s.Id == history.PendingRecoveryId).ToArray()
		};
	private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
	private static SnapshotOperationException Error(string code, string message) => new(code, message);
}

/// <summary>Non-null envelope replaces the entire history across providers that merge non-null top-level fields.</summary>
public sealed record SnapshotStorageRecord(SnapshotHistory History);
