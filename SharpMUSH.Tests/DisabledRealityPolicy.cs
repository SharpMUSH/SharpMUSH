using SharpMUSH.Library.Models;
using SharpMUSH.Library.Reality;

namespace SharpMUSH.Tests;

// Explicit disabled-feature fixture; production hosts must provide their configured policy.
internal sealed class DisabledRealityPolicy : IRealityPolicy
{
	public static readonly DisabledRealityPolicy Instance = new();
	public ValueTask<bool> IsEnabledAsync(CancellationToken ct = default) => ValueTask.FromResult(false);
	public ValueTask<bool> CanPerceiveAsync(DBRef receiver, DBRef target, CancellationToken ct = default) => ValueTask.FromResult(true);
	public ValueTask<string?> DescriptionAttributeAsync(DBRef receiver, DBRef target, CancellationToken ct = default) => ValueTask.FromResult<string?>(null);
}
