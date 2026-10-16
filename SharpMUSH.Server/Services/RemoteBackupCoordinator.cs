using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

public sealed class RemoteBackupData
{
	public RemoteBackupSettings Settings { get; set; } = new(false, 24, "");
	public DateTimeOffset? AgentSeenAt { get; set; }
	public DateTimeOffset? LastSuccessAt { get; set; }
	public List<RemoteBackupJob> Jobs { get; set; } = [];
}

public sealed class RemoteBackupJob
{
	public required RemoteBackupRun Run { get; set; }
	public required string ProtectedProfile { get; set; }
	public string? CopyPath { get; set; }
	public string? Nonce { get; set; }
	public string? WorkerId { get; set; }
	public DateTimeOffset? Deadline { get; set; }
}

/// <summary>One process owns durable transitions; expanded server data dispatches through the Mediator.</summary>
public sealed partial class RemoteBackupCoordinator(
	IExpandedObjectDataService data,
	ISecretStore secrets,
	IDataProtectionProvider protection,
	IWorldBackupService backups,
	TimeProvider time,
	RemoteBackupSourcePaths sourcePaths)
{
	public static readonly SecretDefinition Password = new("backup.restic.password", "Remote backup repository encryption password.", "server.admin");
	public static readonly SecretDefinition AccountId = new("backup.restic.account.id", "Remote backup storage account identifier.", "server.admin");
	public static readonly SecretDefinition AccountKey = new("backup.restic.account.key", "Remote backup storage account key.", "server.admin");
	private readonly SemaphoreSlim _gate = new(1, 1);
	// Protected by _gate. A completed provider copy survives a transient final read/save failure.
	private readonly Dictionary<Guid, Result<WorldBackup>> _prepared = [];
	private readonly HashSet<Guid> _preparing = [];
	private readonly IDataProtector _protector = protection.CreateProtector("SharpMUSH.RemoteBackup.Profile.v1");
	private static readonly TimeSpan Lease = TimeSpan.FromHours(2);

	public static Result<Success> Validate(RemoteBackupSettings settings)
	{
		if (settings.IntervalHours is < 1 or > 8760) return new Error<string>("Interval must be between 1 and 8760 hours.");
		var repository = settings.Repository ?? "";
		if (repository.Length is < 1 or > 2048 || repository.Any(char.IsControl) || repository.Any(char.IsWhiteSpace))
			return new Error<string>("A supported repository is required; whitespace is not allowed.");
		if (B2Repository().IsMatch(repository)) return new Success();
		var prefix = repository.StartsWith("s3:", StringComparison.Ordinal) ? 3
			: repository.StartsWith("rest:", StringComparison.Ordinal) ? 5 : 0;
		if (prefix == 0 || !Uri.TryCreate(repository[prefix..], UriKind.Absolute, out var uri)
			|| uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
			|| uri.AbsolutePath.Length <= 1 || string.IsNullOrEmpty(uri.Host))
			return new Error<string>("Use b2:bucket:path, s3:https://host/bucket or rest:https://host/repository; credentials and query strings are forbidden in locations.");
		return new Success();
	}

	public static bool IsSuccessful(RemoteBackupReceipt receipt)
		=> receipt.ExitCode == 0 && receipt.SnapshotId is { } id && SnapshotId().IsMatch(id);

	public static string FailureCode(int exitCode) => exitCode switch
	{
		-106 => "wiki-root-unmapped",
		-107 => "configured-wiki-directory-missing",
		-100 => "copy-root-unmapped",
		-101 => "plugin-root-unmapped",
		-102 => "configured-plugin-directory-missing",
		-103 => "key-ring-root-unmapped",
		-104 => "backup-source-path-escape",
		-105 => "local-copy-missing",
		-3 => "required-key-ring-missing",
		-2 => "worker-interrupted-result-unknown",
		-1 => "worker-failed-or-timed-out",
		3 => "partial-backup",
		10 => "repository-not-found",
		11 => "repository-locked",
		12 => "repository-password-rejected",
		130 => "cancelled",
		_ => "restic-failed"
	};

	[GeneratedRegex("^b2:[a-zA-Z0-9][a-zA-Z0-9-]{0,62}:[a-zA-Z0-9_./-]+$")]
	private static partial Regex B2Repository();
	[GeneratedRegex("^[a-f0-9]{64}$")]
	private static partial Regex SnapshotId();

	private async Task<RemoteBackupData> ReadAsync()
		=> await data.GetExpandedServerDataAsync<RemoteBackupData>() ?? new();

	public async Task<RemoteBackupStatus> StatusAsync()
	{
		await _gate.WaitAsync();
		try
		{
			var state = await ReadAsync();
			var statuses = await secrets.ListAsync();
			bool Has(string name) => statuses.Any(status => status.Name == name && status.Source is SecretSource.Stored or SecretSource.Environment or SecretSource.File);
			return new(state.Settings, Has(Password.Name), Has(AccountId.Name), Has(AccountKey.Name), state.AgentSeenAt,
				state.LastSuccessAt,
				state.Jobs.Select(job => job.Run).Reverse().ToArray());
		}
		finally { _gate.Release(); }
	}

	public async Task<Result<Success>> ConfigureAsync(RemoteBackupConfigure request, SecretAuthor author)
	{
		if (Validate(request.Settings) is Error<string> error) return error;
		if (new[] { request.Password, request.AccountId, request.AccountKey }.Any(value => value?.Length > SecretStore.MaxValueLength || value?.Contains('\0') == true))
			return new Error<string>("A credential is too long or contains a null character.");
		if (request.ClearAccountId && !string.IsNullOrEmpty(request.AccountId) || request.ClearAccountKey && !string.IsNullOrEmpty(request.AccountKey))
			return new Error<string>("Choose either replacing or clearing each storage credential.");
		await _gate.WaitAsync();
		try
		{
			var changes = new[] { (Password, request.Password, false), (AccountId, request.AccountId, request.ClearAccountId), (AccountKey, request.AccountKey, request.ClearAccountKey) };
			var statuses = await secrets.ListAsync();
			if (changes.Any(change => change.Item3 && statuses.Any(status => status.Name == change.Item1.Name && status.Source is SecretSource.Environment or SecretSource.File)))
				return new Error<string>("An operator environment/file override supplies this credential; remove that override before clearing it in the website.");
			if (request.Settings.Enabled && string.IsNullOrEmpty(request.Password) && await SecretAsync(Password.Name) is not string)
				return new Error<string>("Set a repository password before enabling managed backups.");
			var snapshot = await secrets.SnapshotAsync([Password.Name, AccountId.Name, AccountKey.Name]);
			try
			{
				foreach (var (definition, value, clear) in changes)
				{
					if (clear) await secrets.ClearAsync(definition.Name);
					else if (!string.IsNullOrEmpty(value) && await secrets.SetAsync(definition.Name, value, author) is Error<string> failure)
					{
						await secrets.RestoreAsync(snapshot);
						return failure;
					}
				}
				var state = await ReadAsync();
				state.Settings = request.Settings;
				await data.SetExpandedServerDataAsync(state);
				return new Success();
			}
			catch
			{
				await secrets.RestoreAsync(snapshot);
				throw;
			}
		}
		finally { _gate.Release(); }
	}

	private ValueTask<FoundResult<string>> SecretAsync(string name) => secrets.GetAsync(name);

	public async Task<Result<RemoteBackupRun>> QueueAsync(string reason)
	{
		await _gate.WaitAsync();
		try { return await QueueLockedAsync(await ReadAsync(), reason); }
		finally { _gate.Release(); }
	}

	private async Task<Result<RemoteBackupRun>> QueueLockedAsync(RemoteBackupData state, string reason)
	{
		if (!state.Settings.Enabled) return new Error<string>("Managed remote backups are disabled.");
		if (Validate(state.Settings) is Error<string> invalid) return invalid;
		if (!backups.IsSupported) return new Error<string>("This provider cannot create a safe remote-backup copy.");
		if (state.Jobs.Any(job => job.Run.State is "queued" or "preparing" or "ready" or "running" or "unknown"))
			return new Error<string>("Resolve the outstanding backup job before requesting another.");
		if (await SecretAsync(Password.Name) is not string password)
			return new Error<string>("The repository password is missing or unreadable.");
		var accountId = await SecretAsync(AccountId.Name);
		var accountKey = await SecretAsync(AccountKey.Name);
		if (accountId is Error<string> || accountKey is Error<string>)
			return new Error<string>("A storage credential is unreadable on this server.");
		var profile = new RemoteBackupClaim(Guid.Empty, "", state.Settings.Repository, password,
			accountId switch { string value => value, _ => null }, accountKey switch { string value => value, _ => null }, "", default,
			PluginsPath: sourcePaths.PluginsPath,
			PluginsRequired: sourcePaths.PluginsExplicitlyConfigured || Directory.Exists(sourcePaths.PluginsPath),
			KeyRingPath: sourcePaths.KeyRingPath,
			WikiPath: sourcePaths.WikiPath,
			WikiRequired: sourcePaths.WikiExplicitlyConfigured || Directory.Exists(sourcePaths.WikiPath));
		var job = new RemoteBackupJob
		{
			Run = new(Guid.NewGuid(), reason, "queued", time.GetUtcNow()),
			ProtectedProfile = _protector.Protect(JsonSerializer.Serialize(profile))
		};
		state.Jobs.Add(job);
		var recent = state.Jobs.TakeLast(50).Select(job => job.Run.Id).ToHashSet();
		state.Jobs = state.Jobs.Where(job => recent.Contains(job.Run.Id) || job.Run.State == "abandoned").ToList();
		await data.SetExpandedServerDataAsync(state);
		return job.Run;
	}

	/// <summary>A bounded worker lease never implies that its process has stopped. Unknown copies stay.</summary>
	public async Task TickAsync(CancellationToken ct)
	{
		Guid? prepare = null;
		await _gate.WaitAsync(ct);
		try
		{
			var state = await ReadAsync();
			var now = time.GetUtcNow();
			foreach (var job in state.Jobs.Where(job => job.Run.State == "preparing" && !_preparing.Contains(job.Run.Id) && !_prepared.ContainsKey(job.Run.Id)))
			{
				// The initial save may have committed before reporting a failure. No provider was
				// started unless that save returned; never leave such an inactive job wedged.
				job.Run = job.Run with { State = "failed", ErrorCode = "local-copy-interrupted", CompletedAt = now };
				job.ProtectedProfile = "";
			}
			foreach (var job in state.Jobs.Where(job => job.Run.State == "running" && job.Deadline < now))
				job.Run = job.Run with { State = "unknown", ErrorCode = "agent-lease-expired" };
			if (state.Jobs.FirstOrDefault(job => job.Run.State == "queued") is { } queued)
			{
				queued.Run = queued.Run with { State = "preparing" };
				prepare = queued.Run.Id;
			}
			await SavePreparationResultsAsync(state);
			var latest = state.Jobs.LastOrDefault()?.Run.QueuedAt;
			if (state.Settings.Enabled && (latest is null || now - latest >= TimeSpan.FromHours(state.Settings.IntervalHours)))
				await QueueLockedAsync(state, "scheduled");
			if (prepare is { } active) _preparing.Add(active);
		}
		finally { _gate.Release(); }
		if (prepare is { } id) await PrepareAsync(id, ct);
	}

	private async Task PrepareAsync(Guid id, CancellationToken ct)
	{
		Result<WorldBackup> result = new Error<string>("local-copy-failed");
		try
		{
			if (await backups.CreateRemoteAsync(id, ct) is WorldBackup written && OwnedCopy(id, written.Path)) result = written;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// Provider exception contents can include paths or credentials; the public result is bounded.
		}
		finally
		{
			await _gate.WaitAsync(CancellationToken.None);
			try
			{
				_prepared[id] = result;
				_preparing.Remove(id);
			}
			finally { _gate.Release(); }
		}
		await _gate.WaitAsync(ct);
		try { await SavePreparationResultsAsync(await ReadAsync()); }
		finally { _gate.Release(); }
	}

	/// <summary>Retry metadata only. Never repeat a provider operation or reset an already claimed job.</summary>
	private async Task SavePreparationResultsAsync(RemoteBackupData state)
	{
		foreach (var job in state.Jobs.Where(job => job.Run.State == "preparing"))
		{
			if (!_prepared.TryGetValue(job.Run.Id, out var result)) continue;
			if (result is WorldBackup copy)
			{
				job.CopyPath = copy.Path;
				job.Run = job.Run with { State = "ready", ErrorCode = null, CompletedAt = null };
			}
			else
			{
				job.Run = job.Run with { State = "failed", ErrorCode = "local-copy-failed", CompletedAt = time.GetUtcNow() };
				job.ProtectedProfile = "";
			}
		}
		await data.SetExpandedServerDataAsync(state);
		_prepared.Clear();
	}

	public async Task RecoverAsync()
	{
		await _gate.WaitAsync();
		try
		{
			var state = await ReadAsync();
			foreach (var job in state.Jobs.Where(job => job.Run.State == "preparing"))
			{
				job.Run = job.Run with { State = "failed", ErrorCode = "local-copy-interrupted", CompletedAt = time.GetUtcNow() };
				job.ProtectedProfile = "";
			}
			await data.SetExpandedServerDataAsync(state);
		}
		finally { _gate.Release(); }
	}

	public async Task HeartbeatAsync()
	{
		await _gate.WaitAsync();
		try
		{
			var state = await ReadAsync();
			state.AgentSeenAt = time.GetUtcNow();
			await data.SetExpandedServerDataAsync(state);
		}
		finally { _gate.Release(); }
	}

	public async Task<Found<RemoteBackupClaim>> ClaimAsync(string workerId)
	{
		await _gate.WaitAsync();
		try
		{
			var state = await ReadAsync();
			state.AgentSeenAt = time.GetUtcNow();
			var job = state.Jobs.FirstOrDefault(job => job.Run.State is "running" or "unknown" && job.WorkerId == workerId)
				?? state.Jobs.FirstOrDefault(job => job.Run.State == "ready");
			Found<RemoteBackupClaim> claim = new NotFound();
			if (job is not null)
			{
				var profile = JsonSerializer.Deserialize<RemoteBackupClaim>(_protector.Unprotect(job.ProtectedProfile))!;
				var reconcile = job.Run.State is "running" or "unknown";
				job.WorkerId = workerId;
				job.Nonce ??= Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
				job.Deadline ??= time.GetUtcNow() + Lease;
				job.Run = job.Run with { State = "running" };
				claim = profile with { Id = job.Run.Id, Nonce = job.Nonce, CopyPath = job.CopyPath!, Deadline = job.Deadline.Value, ReconcileOnly = reconcile };
			}
			await data.SetExpandedServerDataAsync(state);
			return claim;
		}
		finally { _gate.Release(); }
	}

	public async Task<bool> ReceiptAsync(RemoteBackupReceipt receipt)
	{
		await _gate.WaitAsync();
		try
		{
			var state = await ReadAsync();
			var job = state.Jobs.FirstOrDefault(job => job.Run.Id == receipt.Id);
			if (job is null || job.Nonce != receipt.Nonce || job.Nonce is null) return false;
			if (job.Run.State is "succeeded" or "failed" or "unconfirmed")
			{
				Cleanup(job);
				return true;
			}
			if (job.Run.State is not ("running" or "unknown" or "abandoned")) return false;
			var success = IsSuccessful(receipt);
			if (success) state.LastSuccessAt = time.GetUtcNow();
			job.Run = job.Run with
			{
				State = success ? "succeeded" : receipt.ExitCode == -2 ? "unconfirmed" : "failed",
				CompletedAt = time.GetUtcNow(),
				SnapshotId = success ? receipt.SnapshotId : null,
				ExitCode = receipt.ExitCode,
				ErrorCode = success ? null : FailureCode(receipt.ExitCode)
			};
			job.ProtectedProfile = "";
			await data.SetExpandedServerDataAsync(state);
			// A receipt promises the worker process exited. Unknown jobs without a receipt retain their copy.
			Cleanup(job);
			return true;
		}
		finally { _gate.Release(); }
	}
	/// <summary>Unblocks scheduling after operator investigation; never deletes an uncertain copy.</summary>
	public async Task<bool> AbandonAsync(Guid id)
	{
		await _gate.WaitAsync();
		try
		{
			var state = await ReadAsync();
			var job = state.Jobs.FirstOrDefault(job => job.Run.Id == id && job.Run.State == "unknown");
			if (job is null) return false;
			job.Run = job.Run with { State = "abandoned", ErrorCode = "operator-abandoned-copy-retained", CompletedAt = time.GetUtcNow() };
			job.ProtectedProfile = "";
			await data.SetExpandedServerDataAsync(state);
			return true;
		}
		finally { _gate.Release(); }
	}

	private string JobRoot(Guid id) => Path.Join(Path.GetFullPath(backups.Root) + ".remote-jobs", id.ToString("N"));

	private bool OwnedCopy(Guid id, string path)
	{
		var full = Path.GetFullPath(path);
		return string.Equals(Path.GetDirectoryName(full), JobRoot(id), StringComparison.Ordinal)
			&& !Path.GetFileName(full).StartsWith('.') && Path.GetFileName(full) != "latest";
	}

	private void Cleanup(RemoteBackupJob job)
	{
		if (job.CopyPath is not { } path || !OwnedCopy(job.Run.Id, path)) return;
		try
		{
			if (Directory.Exists(JobRoot(job.Run.Id))) Directory.Delete(JobRoot(job.Run.Id), true);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// The receipt is already durable. A retried receipt may finish cleanup safely.
		}
	}

}
