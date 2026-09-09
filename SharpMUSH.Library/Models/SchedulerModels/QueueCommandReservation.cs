namespace SharpMUSH.Library.Models.SchedulerModels;

/// <summary>A counted command that is not runnable until published. Dispose abandons only
/// unpublished work; publication and abandonment each claim ownership at most once.</summary>
public sealed class QueueCommandReservation : IDisposable
{
	private readonly Func<ValueTask<QueueAdmissionResult>>? _publish;
	private readonly Action? _releasePending;
	private int _claimed;
	public QueueAdmissionResult Admission { get; }

	internal QueueCommandReservation(QueueAdmissionResult admission,
		Func<ValueTask<QueueAdmissionResult>>? publish = null, Action? releasePending = null)
	{
		Admission = admission;
		_publish = publish;
		_releasePending = releasePending;
	}

	public static QueueCommandReservation Rejected(QueueRejectionReason reason)
		=> new(new(null, reason));

	public ValueTask<QueueAdmissionResult> PublishAsync()
	{
		if (!Admission.Accepted) return ValueTask.FromResult(Admission);
		if (Interlocked.Exchange(ref _claimed, 1) != 0)
			return ValueTask.FromResult(new QueueAdmissionResult(null, QueueRejectionReason.AlreadyReleased));
		return PublishCoreAsync();
	}

	private async ValueTask<QueueAdmissionResult> PublishCoreAsync()
	{
		try { return await _publish!(); }
		finally { _releasePending?.Invoke(); }
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _claimed, 1) == 0) _releasePending?.Invoke();
	}
}
