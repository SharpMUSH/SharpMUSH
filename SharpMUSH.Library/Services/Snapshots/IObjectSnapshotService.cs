using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Snapshots;

namespace SharpMUSH.Library.Services.Snapshots;

public interface IObjectSnapshotService
{
	Task<ObjectSnapshot> CaptureAsync(CapabilityActor actor, DBRef target, string description, int retain = 10, CancellationToken ct = default);
	Task<SnapshotHistory> ListAsync(CapabilityActor actor, DBRef target, CancellationToken ct = default);
	Task<SnapshotPreview> PreviewAsync(CapabilityActor actor, DBRef target, string snapshotId, SnapshotSelection selection, CancellationToken ct = default);
	Task<SnapshotRestoreResult> RestoreAsync(CapabilityActor actor, DBRef target, string snapshotId, SnapshotSelection selection, string previewToken, CancellationToken ct = default);
}

public sealed class SnapshotOperationException(string code, string message) : Exception(message)
{
	public string Code { get; } = code;
}
