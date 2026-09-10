using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.RecurringJobs;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using QueueScheduler = SharpMUSH.Library.Services.Interfaces.ITaskScheduler;

namespace SharpMUSH.Library.Services.RecurringJobs;

/// <summary>One engine owns bounded durable definitions. A persisted firing token precedes admission;
/// callbacks reload that token and authority, so restart and edited/deleted definitions cannot replay work.</summary>
public sealed class RecurringJobService(
	IExpandedDataStore store, IObjectStore objects, IAdministrativeCapabilityService capabilities,
	IPermissionService permissions, IAttributeService attributes, QueueScheduler queue, IMUSHCodeParser parser,
	TimeProvider? clock = null) : IRecurringJobService
{
	public const string StorageKey = "sharpmush.recurring-jobs.v1";
	private readonly TimeProvider _clock = clock ?? TimeProvider.System;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private bool _initialized;

	public async Task<RecurringJob[]> ListAsync(CapabilityActor actor, bool all = false, CancellationToken ct = default)
	{
		await Authorize(actor, all ? PortalPermission.JobsManage : PortalPermission.JobsManageOwn, ct);
		var includeOwn = await capabilities.AuthorizeAsync(actor, PortalPermission.JobsManageOwn, ct);
		var jobs = await Read(ct);
		return jobs.Where(j => j.OwnerAccount == actor.AccountId ? includeOwn : all).OrderBy(j => j.Id).ToArray();
	}

	public async Task<RecurringJob> CreateAsync(CapabilityActor actor, RecurringJobRequest request, CancellationToken ct = default)
	{
		await _gate.WaitAsync(ct);
		try
		{
			var executor = await Authorize(actor, PortalPermission.JobsManageOwn, ct);
			if (request.Description is null || request.Description.Length > 500 || string.IsNullOrWhiteSpace(request.Attribute) || request.Attribute.Length > 1024)
				throw Error("invalid", "Provide an attribute and a description of at most 500 characters.");
			var target = Identity(request.Target);
			await Executable(executor, target, request.Attribute, ct);
			var next = Schedule(request.Schedule, request.TimeZone).Next(_clock.GetUtcNow()) ?? throw Error("invalid", "The schedule has no future occurrence.");
			var jobs = await Read(ct);
			if (jobs.Length >= 256 || jobs.Count(j => j.OwnerAccount == actor.AccountId) >= 32) throw Error("limit", "Job limit reached (32 per account, 256 per world).");
			var job = new RecurringJob(Guid.NewGuid().ToString("N"), actor.AccountId, actor.ActiveCharacter!.Value.ToString(), target.ToString(),
				request.Attribute.ToUpperInvariant(), request.Schedule, request.TimeZone, request.Description, true, 1,
				next.ToUnixTimeMilliseconds(), null, null, "scheduled", null);
			await Save([.. jobs, job], ct);
			return job;
		}
		finally { _gate.Release(); }
	}

	public async Task<RecurringJob> ConfigureAsync(CapabilityActor actor, string id, string schedule, string timeZone, bool enabled, CancellationToken ct = default)
	{
		await _gate.WaitAsync(ct);
		try
		{
			var jobs = await Read(ct);
			var job = Find(jobs, id);
			await Authorize(actor, job.OwnerAccount == actor.AccountId ? PortalPermission.JobsManageOwn : PortalPermission.JobsManage, ct);
			var next = Schedule(schedule, timeZone).Next(_clock.GetUtcNow());
			if (enabled && next is null) throw Error("invalid", "The schedule has no future occurrence.");
			job = job with { Schedule = schedule, TimeZone = timeZone, Enabled = enabled, Revision = checked(job.Revision + 1), NextRun = enabled ? next!.Value.ToUnixTimeMilliseconds() : null, RunToken = null, Status = enabled ? "scheduled" : "disabled", LastError = null };
			await Save(Replace(jobs, job), ct);
			return job;
		}
		finally { _gate.Release(); }
	}

	public async Task DeleteAsync(CapabilityActor actor, string id, CancellationToken ct = default)
	{
		await _gate.WaitAsync(ct);
		try
		{
			var jobs = await Read(ct);
			var job = Find(jobs, id);
			await Authorize(actor, job.OwnerAccount == actor.AccountId ? PortalPermission.JobsManageOwn : PortalPermission.JobsManage, ct);
			await Save(jobs.Where(j => j.Id != id).ToArray(), ct);
		}
		finally { _gate.Release(); }
	}

	public async Task InitializeAsync(CancellationToken ct = default)
	{
		await _gate.WaitAsync(ct);
		try
		{
			if (_initialized) return;
			var now = _clock.GetUtcNow();
			var jobs = await Read(ct);
			for (var index = 0; index < jobs.Length; index++)
			{
				var job = jobs[index];
				var missed = job.RunToken is not null || job.NextRun <= now.ToUnixTimeMilliseconds();
				try
				{
					jobs[index] = job with
					{
						NextRun = job.Enabled ? Schedule(job.Schedule, job.TimeZone).Next(now)?.ToUnixTimeMilliseconds() : null,
						RunToken = null,
						Status = job.Enabled ? "scheduled" : "disabled",
						LastError = missed ? "Missed or interrupted firing skipped at startup." : job.LastError
					};
				}
				catch (RecurringJobException ex) { jobs[index] = job with { Enabled = false, NextRun = null, RunToken = null, Status = "invalid", LastError = ex.Message }; }
			}
			await Save(jobs, ct);
			_initialized = true;
		}
		finally { _gate.Release(); }
	}

	public async Task RunDueAsync(CancellationToken ct = default)
	{
		await InitializeAsync(ct);
		await _gate.WaitAsync(ct);
		try
		{
			var jobs = await Read(ct);
			var now = _clock.GetUtcNow();
			foreach (var original in jobs.Where(j => j.Enabled && j.NextRun <= now.ToUnixTimeMilliseconds()).ToArray())
			{
				// A canceled ready callback still owns its reservation until the consumer drains it.
				// Consult that ledger rather than a persisted token, which can outlive an external halt.
				if (queue.HasPendingWork("recurring:" + original.Id, "recurring"))
				{
					jobs = Replace(jobs, original with
					{
						NextRun = Schedule(original.Schedule, original.TimeZone).Next(now)?.ToUnixTimeMilliseconds(),
						LastError = "Firing skipped while earlier queue work remains pending."
					});
					await Save(jobs, ct);
					continue;
				}
				var token = Guid.NewGuid().ToString("N");
				var job = original with
				{
					NextRun = Schedule(original.Schedule, original.TimeZone).Next(now)?.ToUnixTimeMilliseconds(),
					LastRun = now.ToUnixTimeMilliseconds(),
					RunToken = token,
					Status = "queued",
					LastError = null
				};
				jobs = Replace(jobs, job);
				await Save(jobs, ct); // At-most-once claim, even if admission or the process fails next.
				try
				{
					// Admission's provider reads use the ambient token. Link host polling cancellation
					// without extending an existing evaluation deadline or detaching the admission.
					using var admissionCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, ExecutionBudget.CurrentToken);
					using var admissionBudget = new ExecutionBudget(Timeout.InfiniteTimeSpan, admissionCancellation.Token);
					using var admissionScope = admissionBudget.Enter();
					var admission = await queue.AdmitWork(() => Execute(job.Id, token), "recurring:" + job.Id, "recurring", Identity(job.Character), notifyOnRejection: false);
					if (!admission.Accepted)
					{
						job = job with { RunToken = null, Status = "rejected", LastError = "Queue rejected firing: " + admission.Reason };
						jobs = Replace(jobs, job);
						await Save(jobs, ct);
					}
				}
				catch (Exception)
				{
					jobs = Replace(jobs, job with { RunToken = null, Status = "failed", LastError = "Queue admission failed." });
					await Save(jobs, ct);
				}
			}
		}
		finally { _gate.Release(); }
	}

	private async ValueTask<CallState?> Execute(string id, string token)
	{
		CallState? result;
		string? error;
		var ct = ExecutionBudget.CurrentToken;
		try
		{
			ValueTask<CallState?> evaluation;
			await _gate.WaitAsync(ct);
			try
			{
				var jobs = await Read(ct);
				var job = jobs.SingleOrDefault(j => j.Id == id);
				if (job is null || !job.Enabled || job.RunToken != token) return null;
				await Save(Replace(jobs, job with { Status = "running" }), ct);
				var active = Identity(job.Character);
				var actor = new CapabilityActor(job.OwnerAccount, active, active);
				var executor = await Authorize(actor, PortalPermission.JobsManageOwn, ct);
				var target = Identity(job.Target);
				var code = await Executable(executor, target, job.Attribute, ct);
				ExecutionBudget.Current?.ThrowIfExceeded();
				// Starting evaluation is the dispatch boundary. Do not hold the gate while awaiting
				// softcode, which may itself disable or delete this job through normal commands.
				evaluation = parser.FromState(ParserState.RootFor(active) with
				{
					CurrentEvaluation = new DBAttribute(target, job.Attribute),
					ExecutionBudget = ExecutionBudget.Current
				}).CommandListParse(code);
			}
			finally { _gate.Release(); }
			result = await evaluation;
			var message = result?.Message?.ToPlainText();
			error = message?.StartsWith("#-", StringComparison.Ordinal) == true ? "Attribute execution returned an error." : null;
		}
		catch (Exception ex)
		{
			result = null;
			error = ex is RecurringJobException ? ex.Message : "Attribute execution failed or exceeded its budget.";
		}
		// A failed acknowledgement must not retry a second status mutation or reset the claim.
		// Await the bounded write directly, including provider cancellation acknowledgement.
		await Finish(id, token, error);
		return result;
	}

	private async Task Finish(string id, string token, string? error)
	{
		using var cleanup = new ExecutionBudget(TimeSpan.FromSeconds(1));
		using var scope = cleanup.Enter();
		var ct = cleanup.Token;
		await _gate.WaitAsync(ct);
		try
		{
			var jobs = await Read(ct);
			var job = jobs.SingleOrDefault(j => j.Id == id && j.RunToken == token);
			if (job is not null) await Save(Replace(jobs, job with { RunToken = null, LastError = error, Status = error is null ? "completed" : "failed" }), ct);
		}
		finally { _gate.Release(); }
	}

	private async Task<AnySharpObject> Authorize(CapabilityActor actor, string scope, CancellationToken ct)
	{
		if (actor.ActiveCharacter is not { IsObjid: true } active || actor.Executor != active || !await capabilities.AuthorizeAsync(actor, scope, ct))
			throw Error("denied", "A linked active player with the required jobs capability is required.");
		var executor = await objects.GetObjectNodeAsync(active, ct);
		if (!executor.IsPlayer || executor.AsPlayer.Object.DBRef != active || (await executor.AsPlayer.Object.Flags.Value.ToListAsync(ct)).Any(f => f.Name is "HALT" or "GOING")) throw Error("missing", "The executing player no longer exists.");
		return executor.Known;
	}
	private async Task<MarkupText> Executable(AnySharpObject executor, DBRef target, string attribute, CancellationToken ct)
	{
		var obj = await objects.GetObjectNodeAsync(target, ct);
		if (obj.IsNone || obj.Known.Object().DBRef != target || (await obj.Known.Object().Flags.Value.ToListAsync(ct)).Any(f => f.Name is "HALT" or "GOING")) throw Error("missing", "The target identity no longer exists.");
		if (!await permissions.Controls(executor, obj.Known).AsTask().WaitAsync(ct)) throw Error("denied", "The executing player must control the target.");
		// HTTP callers carry a request token but have no ambient queue budget. Propagate
		// both lifetimes into the read-only attribute API and bound legacy implementations.
		var remaining = ExecutionBudget.Current?.Remaining ?? TimeSpan.MaxValue;
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, ExecutionBudget.CurrentToken);
		using var readBudget = new ExecutionBudget(remaining == TimeSpan.MaxValue ? Timeout.InfiniteTimeSpan : remaining, cancellation.Token);
		using var readScope = readBudget.Enter();
		var value = await attributes.GetAttributeAsync(executor, obj.Known, attribute, IAttributeService.AttributeMode.Execute, false)
			.AsTask().WaitAsync(readBudget.Token);
		if (!value.IsAttribute || value.AsAttribute.Length == 0) throw Error("denied", "The target attribute is missing or not executable.");
		return value.AsAttribute.Last().Value;
	}
	private async Task<RecurringJob[]> Read(CancellationToken ct)
	{
		var data = await store.GetExpandedServerData<RecurringJobDocument>(StorageKey, ct);
		var jobs = data is null ? [] : data.Jobs ?? throw Error("corrupt", "Missing recurring job definitions.");
		if (jobs.Length > 256 || jobs.Any(j => j is null) || jobs.Select(j => j.Id).Distinct(StringComparer.Ordinal).Count() != jobs.Length)
			throw Error("corrupt", "Invalid recurring job document.");
		return jobs;
	}
	private async Task Save(RecurringJob[] jobs, CancellationToken ct) => await store.SetExpandedServerData(StorageKey, new RecurringJobDocument(jobs), ct);
	private static RecurringJob[] Replace(RecurringJob[] jobs, RecurringJob job) => jobs.Select(j => j.Id == job.Id ? job : j).ToArray();
	private static RecurringJob Find(RecurringJob[] jobs, string id) => jobs.SingleOrDefault(j => j.Id == id) ?? throw Error("missing", "Job not found.");
	private static DBRef Identity(string value) => DBRef.TryParse(value, out var reference) && reference is { IsObjid: true } full ? full : throw Error("invalid", "Use a full object identity (#number:creation).");
	private static FiveFieldSchedule Schedule(string expression, string timeZone)
	{
		try { return new(expression, timeZone); }
		catch (Exception ex) when (ex is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
		{ throw Error("invalid", "Invalid five-field schedule or timezone."); }
	}
	private static RecurringJobException Error(string code, string message) => new(code, message);
}
