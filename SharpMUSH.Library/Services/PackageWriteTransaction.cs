using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// The write side of one package operation: apply, rollback or uninstall. Each write goes through
/// here and is recorded with its inverse, read from the state just before the write. An operation
/// that fails is reverted by replaying the inverses newest first, which returns everything it
/// touched to what it was before, however often it was written:
/// <code>
/// await using var writes = await transactions.CreateAsync(packageManager, cancellationToken);
/// // ... writes through `writes` ...
/// await registry.AddPackageRevisionAsync(revision); // the commit write
/// writes.Commit();
/// </code>
/// A failure the operation returns is reverted with <see cref="RevertAsync"/>, which reports what
/// could not be; an exception is reverted by <see cref="DisposeAsync"/>.
/// </summary>
/// <remarks>
/// Forward writes must be awaited sequentially. Closing rejects new writes and joins the admitted
/// write before compensation; commit rejects a pending write. A transaction cannot be reused.
/// A write or inverse callback cannot close its own transaction; independent closers join it.
/// This is compensation, not isolation: nothing spans the stores, so other writers see intermediate
/// state, and a process that dies part way through keeps what it had written. An operation commits
/// with its last write, made straight to the registry and not through here, because it cannot be
/// reversed: a revision record, or the package's removal. This is the permanent contract (#1186):
/// docs/design/world-transactions.md §0 says what a crash leaves behind and how an operator recovers.
/// </remarks>
public sealed class PackageWriteTransaction : IAsyncDisposable
{
	private readonly IMediator mediator;
	private readonly IObjectStore objects;
	private readonly IAttributeStore attributes;
	private readonly IFlagAndPowerStore flags;
	private readonly IPackageRegistryService registry;
	private readonly IApplicationRegistryService applications;
	private readonly ILockService locks;
	private readonly SharpPlayer packageManager;
	private readonly IAsyncDisposable _lifetime;

	/// <summary>Issued only by <see cref="PackageWriteTransactionFactory"/>; obtain one through <see cref="IPackageWriteTransactionFactory"/>.</summary>
	internal PackageWriteTransaction(PackageWriteTransactionFactory origin, IMediator transactionMediator,
		IObjectStore transactionObjects, IAttributeStore transactionAttributes, IFlagAndPowerStore transactionFlags,
		IPackageRegistryService transactionRegistry, IApplicationRegistryService transactionApplications,
		ILockService transactionLocks, SharpPlayer manager, IAsyncDisposable lifetime)
	{
		Origin = origin;
		mediator = transactionMediator;
		objects = transactionObjects;
		attributes = transactionAttributes;
		flags = transactionFlags;
		registry = transactionRegistry;
		applications = transactionApplications;
		locks = transactionLocks;
		packageManager = manager;
		_lifetime = lifetime;
	}

	/// <summary>The world this transaction was issued for; helpers check their own services against it.</summary>
	internal PackageWriteTransactionFactory Origin { get; }

	/// <summary>The Package Manager wizard this operation writes as.</summary>
	public SharpPlayer PackageManager => packageManager;

	private readonly Stack<(string What, Func<Task> Revert)> _undo = new();
	private readonly HashSet<int> _created = [];
	// Source writes are sequential. Closing stops admissions before waiting for the actual
	// admitted write, including its undo registration, then publishes one shared recovery result.
	private readonly object _stateGate = new();
	private static readonly AsyncLocal<OwnedExecution?> CurrentExecution = new();
	private bool _committed;
	private TaskCompletionSource? _forward;
	// The innermost admission holding _forward: a single write, or a helper and the writes it nests.
	private Holder? _holder;
	private Task<IReadOnlyList<string>>? _recovery;
	private bool _reportImplicitFailure;

	// A recovery dispatch/cooperative deadline, not a hard completion limit for a started inverse.
	internal static readonly TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(30);
	internal TimeProvider RecoveryClock { get; init; } = TimeProvider.System;

	private AnySharpObject Executor => packageManager;

	/// <summary>
	/// Marks the operation committed, straight after its commit write: from here its writes stay,
	/// and disposing reverts nothing.
	/// </summary>
	public void Commit()
	{
		lock (_stateGate)
		{
			RequireOpen();
			RequireIdle();
			_undo.Clear();
			_committed = true;
		}
	}

	/// <summary>
	/// Reverts every write so far, newest first, for an operation that failed with
	/// <paramref name="failure"/>; the returned error says whether every write could be reverted.
	/// </summary>
	public async Task<Error<string>> RevertAsync(Error<string> failure)
	{
		var unrestored = await BeginRecovery(explicitRevert: true);
		return new Error<string>(unrestored.Count == 0
			? $"{failure.Value} Its changes were undone."
			: $"{failure.Value} Its changes were undone except: {string.Join("; ", unrestored)}.");
	}

	/// <summary>
	/// Reverts an operation that neither committed nor reverted: one an exception is unwinding. A
	/// failure to restore is reported by <see cref="PackageCompensationException"/>, which can
	/// supersede the original unwinding exception. An explicit <see cref="RevertAsync"/> instead
	/// combines the original failure and unrestored steps in its returned error. Repeated disposal
	/// joins the same recovery without replay; an implicit recovery failure remains observable.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		var unrestored = await BeginRecovery(explicitRevert: false);
		if (_reportImplicitFailure && unrestored.Count != 0)
		{
			throw new PackageCompensationException(unrestored);
		}
	}

	private Task<IReadOnlyList<string>> BeginRecovery(bool explicitRevert)
	{
		for (var execution = CurrentExecution.Value; execution is not null; execution = execution.Parent)
		{
			if (execution.IsActive && ReferenceEquals(execution.Transaction, this))
			{
				throw new InvalidOperationException("A package transaction cannot close from its own active write or inverse.");
			}
		}

		TaskCompletionSource<IReadOnlyList<string>> completion;
		Task pending;
		lock (_stateGate)
		{
			if (_committed && explicitRevert) throw new InvalidOperationException("The package transaction has committed.");

			if (_recovery is not null) return _recovery;
			completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
			_recovery = completion.Task;
			_reportImplicitFailure = !explicitRevert && !_committed;
			pending = _forward?.Task ?? Task.CompletedTask;
		}

		// Publish before invoking any external inverse, including synchronous callbacks. Every
		// closer observes and joins this task; the runner captures all failures into it.
		_ = RecoverAsync(pending, completion);
		return completion.Task;
	}

	private async Task RecoverAsync(Task pending, TaskCompletionSource<IReadOnlyList<string>> completion)
	{
		var recovery = RevertJoinedAsync(pending);
		await ((Task)recovery).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

		// Physical ownership ends only after actual forward work and compensation have joined.
		// Commit alone cannot release it, and every closer shares this same terminal completion.
		var release = ReleaseLifetimeAsync();
		await release.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		var failure = Failure(recovery) is { } recoveryFailure
			? Failure(release) is { } releaseFailure ? new AggregateException(recoveryFailure, releaseFailure) : recoveryFailure
			: Failure(release);
		if (failure is not null) completion.SetException(failure);
		else completion.SetResult(recovery.Result);
	}

	/// <summary>A release that throws before returning its task still faults this one, so recovery completes.</summary>
	private async Task ReleaseLifetimeAsync() => await _lifetime.DisposeAsync();

	private async Task<IReadOnlyList<string>> RevertJoinedAsync(Task pending)
	{
		await pending;
		if (_committed) return [];
		using (new OwnedExecution(this)) return await RevertAllAsync();
	}

	/// <summary>The exception a finished task ended with, unwrapped as an await would throw it; null when it succeeded.</summary>
	private static Exception? Failure(Task task) => task switch
	{
		{ IsCanceled: true } => new TaskCanceledException(task),
		{ Exception: { InnerExceptions.Count: 1 } single } => single.InnerException,
		{ Exception: { } many } => many,
		_ => null
	};

	private void RequireOpen()
	{
		if (_committed || _recovery is not null)
		{
			throw new InvalidOperationException("The package transaction is no longer open.");
		}
	}

	private void RequireIdle()
	{
		if (_forward is not null)
		{
			throw new InvalidOperationException("A package transaction write is already in progress; await it before starting another.");
		}
	}

	private IDisposable AdmitWrite() => Admit(helper: false);

	/// <summary>
	/// Admits a package helper's direct execution, before it gathers state, runs a caller's callback
	/// or writes. Until the returned admission is disposed, the helper's own flow may call this
	/// transaction's writes and <see cref="Track"/>; independent work is refused, and closing waits
	/// for it before compensating. Once closing has begun, the helper's further writes are refused,
	/// while its <see cref="Track"/> of a write it already made is still recorded and undone.
	/// Dispose it after the helper's last write and its tracking: <c>using var admitted = writes.AdmitHelper();</c>
	/// </summary>
	internal IDisposable AdmitHelper() => Admit(helper: true);

	private Holder Admit(bool helper)
	{
		lock (_stateGate)
		{
			RequireOpen();
			if (_holder is null)
			{
				RequireIdle();
				_forward = new(TaskCreationOptions.RunContinuationsAsynchronously);
			}
			else if (!HeldByHelperHere())
			{
				RequireIdle();
			}

			return _holder = new Holder(this, helper, _holder, new OwnedExecution(this));
		}
	}

	/// <summary>Whether the innermost admission is a helper's, and the current flow is running inside it.</summary>
	private bool HeldByHelperHere()
	{
		if (_holder is not { Helper: true } holder) return false;
		for (var execution = CurrentExecution.Value; execution is not null; execution = execution.Parent)
		{
			if (ReferenceEquals(execution, holder.Execution)) return execution.IsActive;
		}

		return false;
	}

	private sealed class Holder(PackageWriteTransaction transaction, bool helper, Holder? outer, OwnedExecution execution) : IDisposable
	{
		private PackageWriteTransaction? _transaction = transaction;
		public bool Helper { get; } = helper;
		public OwnedExecution Execution { get; } = execution;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _transaction, null) is not { } owner) return;
			Execution.Dispose();
			TaskCompletionSource? completion = null;
			lock (owner._stateGate)
			{
				owner._holder = outer;
				if (outer is null)
				{
					completion = owner._forward;
					owner._forward = null;
				}
			}

			completion?.SetResult();
		}
	}

	/// <summary>
	/// Distinguishes a self-join through an extensible callback from an independent closer. Linked
	/// frames also detect A → B → A callbacks; expired captured contexts retain no active ownership.
	/// Enter synchronously in the async operation, so the frame flows through its actual awaits.
	/// </summary>
	private sealed class OwnedExecution : IDisposable
	{
		public PackageWriteTransaction Transaction { get; }
		public OwnedExecution? Parent { get; }
		private bool _active = true;
		public bool IsActive => Volatile.Read(ref _active);

		public OwnedExecution(PackageWriteTransaction transaction)
		{
			Transaction = transaction;
			Parent = CurrentExecution.Value;
			CurrentExecution.Value = this;
		}

		public void Dispose()
		{
			Volatile.Write(ref _active, false);
			if (ReferenceEquals(CurrentExecution.Value, this)) CurrentExecution.Value = Parent;
		}
	}

	private async Task<IReadOnlyList<string>> RevertAllAsync()
	{
		var unrestored = new List<string>();
		// Recovery must not inherit the cancelled execution/request lifetime it is repairing.
		using var recovery = new ExecutionBudget(RecoveryTimeout, CancellationToken.None, RecoveryClock);
		using var scope = recovery.Enter();
		while (_undo.TryPop(out var step))
		{
			if (recovery.IsExceeded)
			{
				unrestored.Add($"{step.What} (not attempted: recovery deadline exceeded)");
				continue;
			}

			try
			{
				// Do not start another inverse after the deadline. A started inverse is always joined:
				// racing it with WaitAsync would let mutation escape its operation's gate/sequence.
				recovery.ThrowIfExceeded();
				await step.Revert();
			}
			catch (Exception ex)
			{
				unrestored.Add($"{step.What} ({ex.Message})");
			}
		}

		return unrestored;
	}

	private void OnRevert(string what, Func<Task> revert)
	{
		lock (_stateGate) _undo.Push((what, revert));
	}

	/// <summary>
	/// Records a write made outside this class with its inverse, for a store it does not cover: roles,
	/// wiki pages and configuration. A helper calls it inside its <see cref="AdmitHelper"/> admission.
	/// </summary>
	public void Track(string what, Func<Task> revert)
	{
		lock (_stateGate)
		{
			if (!HeldByHelperHere())
			{
				RequireOpen();
				RequireIdle();
			}
			else if (_committed)
			{
				RequireOpen();
			}

			_undo.Push((what, revert));
		}
	}

	private async Task<AnySharpObject?> CurrentAsync(DBRef dbref) =>
		await objects.GetObjectNodeAsync(dbref) is AnySharpObject node ? node : null;

	// ── Objects ─────────────────────────────────────────────────────────────

	/// <summary>Creates an object; reverted by marking it GOING, the @destroy convention.</summary>
	public async Task<AnySharpObject?> CreateAsync(ICommand<DBRef> create, CancellationToken cancellationToken)
	{
		using var forward = AdmitWrite();
		var dbref = await mediator.Send(create, cancellationToken);
		if (await objects.GetObjectNodeAsync(dbref, cancellationToken) is not AnySharpObject node)
		{
			return null;
		}

		_created.Add(dbref.Number);
		var objid = node.Object().DBRef;
		OnRevert($"{objid} (created)", async () =>
		{
			if (await CurrentAsync(objid) is { } current
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
		using var forward = AdmitWrite();
		if (!_created.Contains(exit.Object.DBRef.Number))
		{
			throw new InvalidOperationException(
				$"{exit.Object.DBRef} was not created by this transaction; an existing exit's link cannot be reverted.");
		}

		await mediator.Send(new LinkExitCommand(exit, destination), cancellationToken);
	}

	public async Task SetNameAsync(AnySharpObject node, string name, CancellationToken cancellationToken)
	{
		using var forward = AdmitWrite();
		var dbref = node.Object().DBRef;
		if (await CurrentAsync(dbref) is not { } current || current.Object().Name == name)
		{
			return;
		}

		var previous = current.Object().Name;
		await mediator.Send(new SetNameCommand(node, MarkupText.Plain(name)), cancellationToken);
		OnRevert($"{dbref} name", async () =>
		{
			if (await CurrentAsync(dbref) is { } now)
			{
				await mediator.Send(new SetNameCommand(now, MarkupText.Plain(previous)));
			}
		});
	}

	public async Task SetParentAsync(AnySharpObject node, AnySharpObject parent, CancellationToken cancellationToken)
	{
		using var forward = AdmitWrite();
		var dbref = node.Object().DBRef;
		var previous = await node.Object().Parent.WithCancellation(cancellationToken);
		await mediator.Send(new SetObjectParentCommand(node, parent), cancellationToken);
		OnRevert($"{dbref} parent", async () =>
		{
			if (await CurrentAsync(dbref) is not { } now)
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
		using var forward = AdmitWrite();
		if (await CurrentAsync(dbref) is not { } current
			|| (await current.Object().ReadOwnFlagNamesAsync(cancellationToken)).Contains(flag.Name) == set)
		{
			return;
		}

		await SendFlagAsync(current, flag, set, cancellationToken);
		OnRevert($"{dbref} flag {flag.Name}", async () =>
		{
			if (await CurrentAsync(dbref) is { } now)
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
		using var forward = AdmitWrite();
		if (await CurrentAsync(dbref) is not { } current
			|| (await current.Object().ReadOwnPowerNamesAsync(cancellationToken)).Contains(power.Name) == set)
		{
			return;
		}

		await SendPowerAsync(current, power, set, cancellationToken);
		OnRevert($"{dbref} power {power.Name}", async () =>
		{
			if (await CurrentAsync(dbref) is { } now)
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

	public Task<Result<Success>> SetLockAsync(AnySharpObject target, string name, string value, CancellationToken cancellationToken) =>
		WriteLockAsync(target.Object().DBRef, () => locks.SetAsync(Executor, target, name, value, cancellationToken));

	public Task<Result<Success>> UnsetLockAsync(AnySharpObject target, string name, CancellationToken cancellationToken) =>
		WriteLockAsync(target.Object().DBRef, () => locks.UnsetAsync(Executor, target, name, cancellationToken));

	/// <summary>
	/// A lock write, reverted by restoring the object's whole lock table as it was. A lock name can
	/// be spelled several ways and resolves on write, so the table, not the name, is what is compared.
	/// </summary>
	private async Task<Result<Success>> WriteLockAsync(DBRef dbref, Func<ValueTask<Result<Success>>> write)
	{
		using var forward = AdmitWrite();
		if (await CurrentAsync(dbref) is not { } current)
		{
			return await write();
		}

		var previous = current.Object().Locks;
		var result = await write();
		if (result is Success)
		{
			OnRevert($"{dbref} locks", async () =>
			{
				if (await CurrentAsync(dbref) is not { } now)
				{
					return;
				}

				var target = now.Object();
				foreach (var name in target.Locks.Keys.Where(k => !previous.ContainsKey(k)).ToList())
				{
					RequireRestored(await locks.UnsetAsync(Executor, now, name));
				}

				foreach (var (name, data) in previous.Where(l => !target.Locks.TryGetValue(l.Key, out var live) || live != l.Value))
				{
					RequireRestored(await locks.RestoreAsync(Executor, now, name, data));
				}
			});
		}

		return result;
	}

	private static void RequireRestored(Result<Success> result)
	{
		if (result is Error<string> error) throw new InvalidOperationException(error.Value);
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
		using var forward = AdmitWrite();
		var previous = await AttributeStateAsync(dbref, path, cancellationToken);
		await mediator.Send(new SetAttributeCommand(dbref, path, value, owner), cancellationToken);
		OnRevert($"{dbref}/{string.Join('`', path)}", () => RestoreAttributeAsync(dbref, path, previous));
	}

	public async Task ClearAttributeAsync(DBRef dbref, string[] path, CancellationToken cancellationToken)
	{
		using var forward = AdmitWrite();
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
		await foreach (var flag in AttributeFlagsNamed(previous.Flags.Except(live, StringComparer.OrdinalIgnoreCase)))
		{
			await mediator.Send(new SetAttributeFlagCommand(dbref, leaf, flag));
		}

		await foreach (var flag in AttributeFlagsNamed(live.Except(previous.Flags, StringComparer.OrdinalIgnoreCase)))
		{
			await mediator.Send(new UnsetAttributeFlagCommand(dbref, leaf, flag));
		}
	}

	/// <summary>The attribute flags these names resolve to; a name no flag has any more is skipped.</summary>
	private IAsyncEnumerable<SharpAttributeFlag> AttributeFlagsNamed(IEnumerable<string> names) =>
		names.ToAsyncEnumerable()
			.Select((name, cancellationToken) => attributes.GetAttributeFlagAsync(name, cancellationToken))
			.OfType<SharpAttributeFlag>();

	/// <summary>Sets a flag on an attribute; false when the attribute does not exist.</summary>
	public Task<bool> SetAttributeFlagAsync(DBRef dbref, string[] path, SharpAttributeFlag flag, CancellationToken cancellationToken) =>
		WriteAttributeFlagAsync(dbref, path, flag, true, cancellationToken);

	/// <summary>Clears a flag on an attribute; false when the attribute does not exist.</summary>
	public Task<bool> UnsetAttributeFlagAsync(DBRef dbref, string[] path, SharpAttributeFlag flag, CancellationToken cancellationToken) =>
		WriteAttributeFlagAsync(dbref, path, flag, false, cancellationToken);

	private async Task<bool> WriteAttributeFlagAsync(
		DBRef dbref, string[] path, SharpAttributeFlag flag, bool set, CancellationToken cancellationToken)
	{
		using var forward = AdmitWrite();
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
		using var forward = AdmitWrite();
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

	private async Task<PackageWikiPageRecord?> PackageWikiPageAsync(string packageId, string ns, string slug) =>
		await registry.GetPackageWikiPageAsync(packageId, ns, slug) is PackageWikiPageRecord found ? found : null;

	public Task UpsertPackageWikiPageAsync(PackageWikiPageRecord record) =>
		WriteRowAsync($"{record.PackageId} wiki page {record.Namespace}:{record.Slug}",
			() => PackageWikiPageAsync(record.PackageId, record.Namespace, record.Slug),
			() => registry.UpsertPackageWikiPageAsync(record),
			registry.UpsertPackageWikiPageAsync,
			() => registry.RemovePackageWikiPageAsync(record.PackageId, record.Namespace, record.Slug));

	public Task RemovePackageWikiPageAsync(string packageId, string ns, string slug) =>
		WriteRowAsync($"{packageId} wiki page {ns}:{slug}",
			() => PackageWikiPageAsync(packageId, ns, slug),
			() => registry.RemovePackageWikiPageAsync(packageId, ns, slug),
			registry.UpsertPackageWikiPageAsync,
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
		using var forward = AdmitWrite();
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
