using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Reality;

/// <summary>Pre-reality behavior for callers using the published legacy service constructors.</summary>
internal sealed class DisabledRealityPolicy : IRealityPolicy
{
	internal static readonly DisabledRealityPolicy Instance = new();
	private DisabledRealityPolicy() { }
	public ValueTask<bool> CanPerceiveAsync(DBRef receiver, DBRef target, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		return ValueTask.FromResult(true);
	}
	public ValueTask<bool> IsEnabledAsync(CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		return ValueTask.FromResult(false);
	}
	public ValueTask<string?> DescriptionAttributeAsync(DBRef receiver, DBRef target, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		return ValueTask.FromResult<string?>(null);
	}
}
