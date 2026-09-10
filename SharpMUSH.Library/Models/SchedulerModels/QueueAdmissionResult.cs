namespace SharpMUSH.Library.Models.SchedulerModels;

public enum QueueRejectionReason { None, GlobalLimit, OwnerLimit, ShuttingDown, InvalidTarget, AlreadyReleased }

/// <summary>A rejected submission never owns a PID or queue capacity.</summary>
public readonly record struct QueueAdmissionResult(long? Pid, QueueRejectionReason Reason)
{
	public bool Accepted => Pid.HasValue && Reason == QueueRejectionReason.None;
	public string Error => $"#-1 QUEUE REJECTED: {Reason.ToString().ToUpperInvariant()}";
}

public sealed record QueueUsage(int Total, IReadOnlyDictionary<string, int> Owners,
 IReadOnlyDictionary<QueueRejectionReason, long> Rejections);
