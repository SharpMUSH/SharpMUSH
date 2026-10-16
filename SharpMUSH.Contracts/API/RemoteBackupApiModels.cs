using System.ComponentModel.DataAnnotations;

namespace SharpMUSH.Library.API;

public sealed record RemoteBackupSettings(bool Enabled, int IntervalHours, [property: Required, StringLength(2048)] string Repository);
public sealed record RemoteBackupConfigure([property: Required] RemoteBackupSettings Settings, string? Password, string? AccountId, string? AccountKey, bool ClearAccountId = false, bool ClearAccountKey = false);
public sealed record RemoteBackupRun(Guid Id, string Reason, string State, DateTimeOffset QueuedAt,
 DateTimeOffset? CompletedAt = null, string? SnapshotId = null, int? ExitCode = null, string? ErrorCode = null);
public sealed record RemoteBackupStatus(RemoteBackupSettings Settings, bool PasswordSet, bool AccountIdSet,
 bool AccountKeySet, DateTimeOffset? AgentSeenAt, DateTimeOffset? LastSuccessAt, IReadOnlyList<RemoteBackupRun> Runs);
public sealed record RemoteBackupClaim(Guid Id, string Nonce, string Repository, string Password,
 string? AccountId, string? AccountKey, string CopyPath, DateTimeOffset Deadline, bool ReconcileOnly = false);
public sealed record RemoteBackupReceipt(Guid Id, [property: Required, StringLength(64)] string Nonce, int ExitCode, [property: StringLength(64)] string? SnapshotId, [property: StringLength(128)] string? ErrorCode);
