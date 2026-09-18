using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// The write side of one package operation: apply, rollback or uninstall. Each write goes through
/// here and is recorded with its inverse, read from the state just before the write. An operation
/// that fails is reverted by replaying the inverses newest first, which returns everything it
/// touched to what it was before, however often it was written:
/// <code>
/// await using var writes = BeginWrites(packageManager);
/// // ... writes through `writes` ...
/// await registry.AddPackageRevisionAsync(revision); // the commit write
/// writes.Commit();
/// </code>
/// A failure the operation returns is reverted with <see cref="RevertAsync"/>, which reports what
/// could not be; an exception is reverted by <see cref="DisposeAsync"/>.
/// </summary>
/// <remarks>
/// This is compensation, not isolation: nothing spans the stores, so other writers see intermediate
/// state, and a process that dies part way through keeps what it had written. An operation commits
/// with its last write, made straight to the registry and not through here, because it cannot be
/// reversed: a revision record, or the package's removal.
/// </remarks>
public sealed class PackageWriteTransaction(
	IMediator mediator,
	IObjectStore objects,
	IAttributeStore attributes,
	IFlagAndPowerStore flags,
	IPackageRegistryService registry,
	IApplicationRegistryService applications,
	SharpPlayer packageManager) : IAsyncDisposable
{
	private readonly Stack<(string What, Func<Task> Revert)> _undo = new();
	private readonly HashSet<int> _created = [];
	private bool _settled;

	private AnySharpObject Executor => packageManager;

	/// <summary>
	/// Marks the operation committed, straight after its commit write: from here its writes stay,
	/// and disposing reverts nothing.
	/// </summary>
	public void Commit()
	{
		_undo.Clear();
		_settled = true;
	}

	/// <summary>
	/// Reverts every write so far, newest first, for an operation that failed with
	/// <paramref name="failure"/>; the returned error says whether every write could be reverted.
	/// </summary>
	public async Task<Error<string>> RevertAsync(Error<string> failure)
	{
		var unrestored = await RevertAllAsync();
		return new Error<string>(unrestored.Count == 0
			? $"{failure.Value} Its changes were undone."
			: $"{failure.Value} Its changes were undone except: {string.Join("; ", unrestored)}.");
	}

	/// <summary>
	/// Reverts an operation that neither committed nor reverted: one an exception is unwinding. A
	/// write that cannot be reverted here is dropped, so that the exception, not the cleanup, surfaces.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		if (!_settled)
		{
			await RevertAllAsync();
		}
	}

	private async Task<IReadOnlyList<string>> RevertAllAsync()
	{
		_settled = true;
		var unrestored = new List<string>();
		while (_undo.TryPop(out var step))
		{
			try
			{
				await step.Revert();
			}
			catch (Exception ex)
			{
				unrestored.Add($"{step.What} ({ex.Message})");
			}
		}

		return unrestored;
	}

	private void OnRevert(string what, Func<Task> revert) => _undo.Push((what, revert));

	private async Task<AnySharpObject?> CurrentAsync(DBRef dbref) =>
		await objects.GetObjectNodeAsync(dbref) is AnySharpObject node ? node : null;

	// ── Objects ─────────────────────────────────────────────────────────────

	/// <summary>Creates an object; reverted by marking it GOING, the @destroy convention.</summary>
	public async Task<AnySharpObject?> CreateAsync(ICommand<DBRef> create, CancellationToken cancellationToken)
	{
		var dbref = await mediator.Send(create, cancellationToken);
		if (await objects.GetObjectNodeAsync(dbref, cancellationToken) is not AnySharpObject node)
		{
			return null;
		}

		_created.Add(dbref.Number);
		var objid = node.Object().DBRef;
		OnRevert($"{objid} (created)", async () =>
		{
			if (await CurrentAsync(objid) is AnySharpObject current
				&& await flags.GetObjectFlagAsync("GOING") is SharpObjectFlag going)
			{
				await mediator.Send(new SetObjectFlagCommand(current, going));
			}
		});
		return node;
	}

	/// <summary>Links an exit this transaction created: reverting its creation retires the link with it.</summary>
	public async Task LinkCreatedExitAsync(SharpExit exit, AnySharpContainer destination, CancellationToken cancellationToken)
	{
		if (!_created.Contains(exit.Object.DBRef.Number))
		{
			throw new InvalidOperationException(
				$"{exit.Object.DBRef} was not created by this transaction; an existing exit's link cannot be reverted.");
		}

		await mediator.Send(new LinkExitCommand(exit, destination), cancellationToken);
	}

	public async Task SetNameAsync(AnySharpObject node, string name, CancellationToken cancellationToken)
	{
		var dbref = node.Object().DBRef;
		if (await CurrentAsync(dbref) is not AnySharpObject current || current.Object().Name == name)
		{
			return;
		}

		var previous = current.Object().Name;
		await mediator.Send(new SetNameCommand(node, MarkupText.Plain(name)), cancellationToken);
		OnRevert($"{dbref} name", async () =>
		{
			if (await CurrentAsync(dbref) is AnySharpObject now)
			{
				await mediator.Send(new SetNameCommand(now, MarkupText.Plain(previous)));
			}
		});
	}

	public async Task SetParentAsync(AnySharpObject node, AnySharpObject parent, CancellationToken cancellationToken)
	{
		var dbref = node.Object().DBRef;
		var previous = await node.Object().Parent.WithCancellation(cancellationToken);
		await mediator.Send(new SetObjectParentCommand(node, parent), cancellationToken);
		OnRevert($"{dbref} parent", async () =>
		{
			if (await CurrentAsync(dbref) is not AnySharpObject now)
			{
				return;
			}

			if (previous is AnySharpObject previousParent)
			{
				await mediator.Send(new SetObjectParentCommand(now, previousParent));
			}
			else
			{
				await mediator.Send(new UnsetObjectParentCommand(now));
			}
		});
	}

	public Task SetFlagAsync(AnySharpObject node, SharpObjectFlag flag, CancellationToken cancellationToken) =>
		WriteFlagAsync(node.Object().DBRef, flag, true, cancellationToken);

	public Task UnsetFlagAsync(AnySharpObject node, SharpObjectFlag flag, CancellationToken cancellationToken) =>
		WriteFlagAsync(node.Object().DBRef, flag, false, cancellationToken);

	private async Task WriteFlagAsync(DBRef dbref, SharpObjectFlag flag, bool set, CancellationToken cancellationToken)
	{
		if (await CurrentAsync(dbref) is not AnySharpObject current
			|| await current.Object().Flags.Value.AnyAsync(f => f.Name == flag.Name, cancellationToken) == set)
		{
			return;
		}

		await SendFlagAsync(current, flag, set, cancellationToken);
		OnRevert($"{dbref} flag {flag.Name}", async () =>
		{
			if (await CurrentAsync(dbref) is AnySharpObject now)
			{
				await SendFlagAsync(now, flag, !set, CancellationToken.None);
			}
		});
	}

	private async Task SendFlagAsync(AnySharpObject node, SharpObjectFlag flag, bool set, CancellationToken cancellationToken)
	{
		if (set)
		{
			await mediator.Send(new SetObjectFlagCommand(node, flag), cancellationToken);
		}
		else
		{
			await mediator.Send(new UnsetObjectFlagCommand(node, flag), cancellationToken);
		}
	}

	public Task SetPowerAsync(AnySharpObject node, SharpPower power, CancellationToken cancellationToken) =>
		WritePowerAsync(node.Object().DBRef, power, true, cancellationToken);

	public Task UnsetPowerAsync(AnySharpObject node, SharpPower power, CancellationToken cancellationToken) =>
		WritePowerAsync(node.Object().DBRef, power, false, cancellationToken);

	private async Task WritePowerAsync(DBRef dbref, SharpPower power, bool set, CancellationToken cancellationToken)
	{
		if (await CurrentAsync(dbref) is not AnySharpObject current
			|| await current.Object().Powers.Value.AnyAsync(p => p.Name == power.Name, cancellationToken) == set)
		{
			return;
		}

		await SendPowerAsync(current, power, set, cancellationToken);
		OnRevert($"{dbref} power {power.Name}", async () =>
		{
			if (await CurrentAsync(dbref) is AnySharpObject now)
			{
				await SendPowerAsync(now, power, !set, CancellationToken.None);
			}
		});
	}

	private async Task SendPowerAsync(AnySharpObject node, SharpPower power, bool set, CancellationToken cancellationToken)
	{
		if (set)
		{
			await mediator.Send(new SetObjectPowerCommand(node, power), cancellationToken);
		}
		else
		{
			await mediator.Send(new UnsetObjectPowerCommand(node, power), cancellationToken);
		}
	}

	// ── Locks ───────────────────────────────────────────────────────────────

	public Task<Result<Success>> SetLockAsync(SharpObject target, string name, string value, CancellationToken cancellationToken) =>
		WriteLockAsync(target.DBRef, () => mediator.Send(new SetLockCommand(target, name, value, Executor), cancellationToken));

	public Task<Result<Success>> UnsetLockAsync(SharpObject target, string name, CancellationToken cancellationToken) =>
		WriteLockAsync(target.DBRef, () => mediator.Send(new UnsetLockCommand(target, name, Executor), cancellationToken));

	/// <summary>
	/// A lock write, reverted by restoring the object's whole lock table as it was. A lock name can
	/// be spelled several ways and resolves on write, so the table, not the name, is what is compared.
	/// </summary>
	private async Task<Result<Success>> WriteLockAsync(DBRef dbref, Func<ValueTask<Result<Success>>> write)
	{
		if (await CurrentAsync(dbref) is not AnySharpObject current)
		{
			return await write();
		}

		var previous = current.Object().Locks;
		var result = await write();
		if (result is Success)
		{
			OnRevert($"{dbref} locks", async () =>
			{
				if (await CurrentAsync(dbref) is not AnySharpObject now)
				{
					return;
				}

				var target = now.Object();
				foreach (var name in target.Locks.Keys.Where(k => !previous.ContainsKey(k)).ToList())
				{
					await mediator.Send(new UnsetLockCommand(target, name, Executor));
				}

				foreach (var (name, data) in previous.Where(l => !target.Locks.TryGetValue(l.Key, out var live) || live != l.Value))
				{
					await mediator.Send(new SetLockCommand(target, name, data.LockString, Executor)
					{
						Flags = data.Flags,
						Creator = data.Creator,
						PreserveCreator = true
					});
				}
			});
		}

		return result;
	}

	// ── Attributes ──────────────────────────────────────────────────────────

	private sealed record AttributeState(MString Value, IReadOnlyList<string> Flags, SharpPlayer? Owner);

	private async Task<SharpAttribute?> LeafAsync(DBRef dbref, string[] path, CancellationToken cancellationToken)
	{
		var chain = await attributes.GetAttributeAsync(dbref, path, cancellationToken).ToArrayAsync(cancellationToken);
		return chain.Length == path.Length ? chain[^1] : null;
	}

	private async Task<AttributeState?> AttributeStateAsync(DBRef dbref, string[] path, CancellationToken cancellationToken) =>
		await LeafAsync(dbref, path, cancellationToken) is SharpAttribute leaf
			? new AttributeState(leaf.Value, leaf.Flags.Select(f => f.Name).ToList(),
				leaf.Owner is null ? null : await leaf.Owner.WithCancellation(cancellationToken))
			: null;

	public async Task SetAttributeAsync(DBRef dbref, string[] path, MString value, SharpPlayer owner, CancellationToken cancellationToken)
	{
		var previous = await AttributeStateAsync(dbref, path, cancellationToken);
		await mediator.Send(new SetAttributeCommand(dbref, path, value, owner), cancellationToken);
		OnRevert($"{dbref}/{string.Join('`', path)}", () => RestoreAttributeAsync(dbref, path, previous));
	}

	public async Task ClearAttributeAsync(DBRef dbref, string[] path, CancellationToken cancellationToken)
	{
		if (await AttributeStateAsync(dbref, path, cancellationToken) is not AttributeState previous)
		{
			return;
		}

		await mediator.Send(new ClearAttributeCommand(dbref, path), cancellationToken);
		OnRevert($"{dbref}/{string.Join('`', path)}", () => RestoreAttributeAsync(dbref, path, previous));
	}

	private async Task RestoreAttributeAsync(DBRef dbref, string[] path, AttributeState? previous)
	{
		if (previous is null)
		{
			if (await LeafAsync(dbref, path, CancellationToken.None) is not null)
			{
				await mediator.Send(new ClearAttributeCommand(dbref, path));
			}

			return;
		}

		await mediator.Send(new SetAttributeCommand(dbref, path, previous.Value, previous.Owner ?? packageManager));
		if (await LeafAsync(dbref, path, CancellationToken.None) is not SharpAttribute leaf)
		{
			return;
		}

		var live = leaf.Flags.Select(f => f.Name).ToList();
		foreach (var name in previous.Flags.Except(live, StringComparer.OrdinalIgnoreCase))
		{
			if (await attributes.GetAttributeFlagAsync(name) is SharpAttributeFlag flag)
			{
				await mediator.Send(new SetAttributeFlagCommand(dbref, leaf, flag));
			}
		}

		foreach (var name in live.Except(previous.Flags, StringComparer.OrdinalIgnoreCase))
		{
			if (await attributes.GetAttributeFlagAsync(name) is SharpAttributeFlag flag)
			{
				await mediator.Send(new UnsetAttributeFlagCommand(dbref, leaf, flag));
			}
		}
	}

	/// <summary>Sets a flag on an attribute; false when the attribute does not exist.</summary>
	public Task<bool> SetAttributeFlagAsync(DBRef dbref, string[] path, SharpAttributeFlag flag, CancellationToken cancellationToken) =>
		WriteAttributeFlagAsync(dbref, path, flag, true, cancellationToken);

	/// <summary>Clears a flag on an attribute; false when the attribute does not exist.</summary>
	public Task<bool> UnsetAttributeFlagAsync(DBRef dbref, string[] path, SharpAttributeFlag flag, CancellationToken cancellationToken) =>
		WriteAttributeFlagAsync(dbref, path, flag, false, cancellationToken);

	private async Task<bool> WriteAttributeFlagAsync(
		DBRef dbref, string[] path, SharpAttributeFlag flag, bool set, CancellationToken cancellationToken)
	{
		if (await LeafAsync(dbref, path, cancellationToken) is not SharpAttribute leaf)
		{
			return false;
		}

		if (leaf.Flags.Any(f => f.Name == flag.Name) == set)
		{
			return true;
		}

		await SendAttributeFlagAsync(dbref, leaf, flag, set, cancellationToken);
		OnRevert($"{dbref}/{string.Join('`', path)} flag {flag.Name}", async () =>
		{
			if (await LeafAsync(dbref, path, CancellationToken.None) is SharpAttribute now)
			{
				await SendAttributeFlagAsync(dbref, now, flag, !set, CancellationToken.None);
			}
		});
		return true;
	}

	private async Task SendAttributeFlagAsync(
		DBRef dbref, SharpAttribute leaf, SharpAttributeFlag flag, bool set, CancellationToken cancellationToken)
	{
		if (set)
		{
			await mediator.Send(new SetAttributeFlagCommand(dbref, leaf, flag), cancellationToken);
		}
		else
		{
			await mediator.Send(new UnsetAttributeFlagCommand(dbref, leaf, flag), cancellationToken);
		}
	}

	// ── Package registry ────────────────────────────────────────────────────

	/// <summary>
	/// A registry row write, reverted by writing back the row it replaced, or removing the row when
	/// there was none.
	/// </summary>
	private async Task WriteRowAsync<TRow>(
		string what, Func<Task<TRow?>> read, Func<Task> write, Func<TRow, Task> restore, Func<Task> remove)
		where TRow : class
	{
		var previous = await read();
		await write();
		OnRevert(what, () => previous is null ? remove() : restore(previous));
	}

	private async Task<PackageObjectRecord?> PackageObjectAsync(string packageId, string @ref) =>
		(await registry.GetPackageObjectsAsync(packageId)).FirstOrDefault(o => o.Ref == @ref);

	public Task UpsertPackageObjectAsync(PackageObjectRecord record) =>
		WriteRowAsync($"{record.PackageId} object {record.Ref}",
			() => PackageObjectAsync(record.PackageId, record.Ref),
			() => registry.UpsertPackageObjectAsync(record),
			registry.UpsertPackageObjectAsync,
			() => registry.RemovePackageObjectAsync(record.PackageId, record.Ref));

	public Task RemovePackageObjectAsync(string packageId, string @ref) =>
		WriteRowAsync($"{packageId} object {@ref}",
			() => PackageObjectAsync(packageId, @ref),
			() => registry.RemovePackageObjectAsync(packageId, @ref),
			registry.UpsertPackageObjectAsync,
			() => Task.CompletedTask);

	private async Task<ManagedAttributeRecord?> ManagedAttributeAsync(string packageId, string objid, string attribute) =>
		(await registry.GetManagedAttributesAsync(packageId)).FirstOrDefault(a =>
			a.Objid == objid && string.Equals(a.Attribute, attribute, StringComparison.OrdinalIgnoreCase));

	public Task UpsertManagedAttributeAsync(ManagedAttributeRecord record) =>
		WriteRowAsync($"{record.PackageId} baseline {record.Objid}/{record.Attribute}",
			() => ManagedAttributeAsync(record.PackageId, record.Objid, record.Attribute),
			() => registry.UpsertManagedAttributeAsync(record),
			registry.UpsertManagedAttributeAsync,
			() => registry.RemoveManagedAttributeAsync(record.PackageId, record.Objid, record.Attribute));

	public Task RemoveManagedAttributeAsync(string packageId, string objid, string attribute) =>
		WriteRowAsync($"{packageId} baseline {objid}/{attribute}",
			() => ManagedAttributeAsync(packageId, objid, attribute),
			() => registry.RemoveManagedAttributeAsync(packageId, objid, attribute),
			registry.UpsertManagedAttributeAsync,
			() => Task.CompletedTask);

	private async Task<ManagedStructureRecord?> ManagedStructureAsync(string packageId, string objid) =>
		(await registry.GetManagedStructuresAsync(packageId)).FirstOrDefault(s => s.Objid == objid);

	public Task UpsertManagedStructureAsync(ManagedStructureRecord record) =>
		WriteRowAsync($"{record.PackageId} structure {record.Objid}",
			() => ManagedStructureAsync(record.PackageId, record.Objid),
			() => registry.UpsertManagedStructureAsync(record),
			registry.UpsertManagedStructureAsync,
			() => registry.RemoveManagedStructureAsync(record.PackageId, record.Objid));

	public Task RemoveManagedStructureAsync(string packageId, string objid) =>
		WriteRowAsync($"{packageId} structure {objid}",
			() => ManagedStructureAsync(packageId, objid),
			() => registry.RemoveManagedStructureAsync(packageId, objid),
			registry.UpsertManagedStructureAsync,
			() => Task.CompletedTask);

	/// <summary>
	/// The installed-package row. Reverting a first install removes the package, which also clears
	/// every row written under it; the rows written after this one are reverted before it.
	/// </summary>
	public Task UpsertInstalledPackageAsync(InstalledPackageRecord record) =>
		WriteRowAsync($"{record.Id} install record",
			async () => await registry.GetInstalledPackageAsync(record.Id) is InstalledPackageRecord found ? found : null,
			() => registry.UpsertInstalledPackageAsync(record),
			registry.UpsertInstalledPackageAsync,
			() => registry.RemoveInstalledPackageAsync(record.Id));

	public async Task SetPackageDependenciesAsync(string packageId, IReadOnlyList<PackageDependencyRecord> dependencies)
	{
		var previous = await registry.GetPackageDependenciesAsync(packageId);
		await registry.SetPackageDependenciesAsync(packageId, dependencies);
		OnRevert($"{packageId} dependencies", () => registry.SetPackageDependenciesAsync(packageId, previous));
	}

	private async Task<RegisteredApplication?> ApplicationAsync(string slug) =>
		await applications.GetApplicationAsync(slug) is RegisteredApplication found ? found : null;

	public Task UpsertApplicationAsync(RegisteredApplication application) =>
		WriteRowAsync($"application {application.Slug}",
			() => ApplicationAsync(application.Slug),
			() => applications.UpsertApplicationAsync(application),
			applications.UpsertApplicationAsync,
			() => applications.RemoveApplicationAsync(application.Slug));

	public Task RemoveApplicationAsync(string slug) =>
		WriteRowAsync($"application {slug}",
			() => ApplicationAsync(slug),
			() => applications.RemoveApplicationAsync(slug),
			applications.UpsertApplicationAsync,
			() => Task.CompletedTask);
}
