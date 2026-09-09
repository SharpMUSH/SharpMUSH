using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services;

public partial class TaskScheduler
{
	// One uncertain command blocks the mutation gate, so this slot and its reserved PID set
	// are bounded by the existing queue limit, including credit-only notifications.
	private Func<ValueTask>? _semaphoreCommandRepair;
	private readonly HashSet<long> _semaphoreCommandReservations = [];

	/// <summary>Caller owns the semaphore mutation lease. Persist before changing queue state.</summary>
	public async ValueTask<int> ApplySemaphoreCommandAsync(DbRefAttribute target, int? count, bool drain,
		Func<int, ValueTask> persist, Func<ValueTask<bool>> reconcile, Dictionary<string, MString>? registers = null)
	{
		using var deferredLease = await LockDeferred();
		QueueEntry[] selected;
		lock (_admissionLock)
		{
			if (_stopping) throw new OperationCanceledException("The queue is stopping.");
			selected = _pendingEntries.Values.Where(entry => entry.Group == $"{SemaphoreGroup}:{target}" &&
				!_ready.Contains(entry.Pid) && entry.Deferred?.ReleasePending != true && !_semaphoreRepairs.ContainsKey(entry.Pid))
				.OrderBy(entry => entry.Pid).Take(count ?? int.MaxValue).ToArray();
		}
		ParserState? registerState = null;
		if (registers is not null)
		{
			if (selected.Length == 0) return 0;
			if (selected[0].Deferred is not { } deferred) return 0;
			registerState = deferred.State;
		}
		lock (_admissionLock)
		{
			// A legacy timer can win while the register state is being read.
			if (registerState is not null && (!_pendingEntries.ContainsKey(selected[0].Pid) || _ready.Contains(selected[0].Pid))) return 0;
			selected = selected.Where(entry => _pendingEntries.ContainsKey(entry.Pid) && !_ready.Contains(entry.Pid)).ToArray();
			foreach (var entry in selected) _semaphoreCommandReservations.Add(entry.Pid);
		}
		var cleaned = new HashSet<long>();
		bool? confirmedCommit = null;
		async ValueTask Complete(bool committed)
		{
			// Keep every PID reserved until all long-lived timers have acknowledged removal.
			// A lost acknowledgement is retried; a confirmed removal is never repeated.
			if (committed)
			{
				foreach (var entry in selected)
				{
					if (cleaned.Contains(entry.Pid)) continue;
					ExecutionBudget.CurrentToken.ThrowIfCancellationRequested();
					await RemoveDeferredTrigger(entry);
					cleaned.Add(entry.Pid);
				}
			}
			if (committed && registerState is not null && registers is not null)
			{
				if (!registerState.Registers.TryPeek(out var frame)) registerState.Registers.Push(frame = new());
				foreach (var (key, value) in registers) frame[key.ToUpperInvariant()] = value;
			}
			foreach (var entry in selected)
			{
				ValueTask<QueueAdmissionResult>? activation = null;
				QueueEntry? removed = null;
				lock (_admissionLock)
				{
					_semaphoreCommandReservations.Remove(entry.Pid);
					if (committed)
					{
						if (drain || _stopping) removed = RemoveEntry(entry.Pid);
						else activation = Activate(entry.Pid);
					}
				}
				// Semaphore entries never carry direct-input release callbacks.
				if (removed is not null) DisposeEntry(removed);
				if (activation is { } pending) await pending;
			}
		}
		async ValueTask Repair()
		{
			confirmedCommit ??= await reconcile();
			await Complete(confirmedCommit.Value);
		}
		try
		{
			await persist(selected.Length);
			confirmedCommit = true;
		}
		catch (Exception writeFailure)
		{
			try
			{
				using var budget = ExecutionBudget.FromMilliseconds(1000, _shutdownCts.Token);
				using var scope = budget.Enter();
				await Repair();
			}
			catch (Exception repairFailure)
			{
				_semaphoreCommandRepair = async () =>
				{
					using var repairLease = await LockDeferred();
					await Repair();
				};
				throw new AggregateException("Semaphore command accounting is uncertain; subsequent mutations require reconciliation.", writeFailure, repairFailure);
			}
			throw;
		}
		try { await Complete(true); }
		catch
		{
			_semaphoreCommandRepair = async () =>
			{
				using var repairLease = await LockDeferred();
				await Repair();
			};
			throw;
		}
		return selected.Length;
	}
}
