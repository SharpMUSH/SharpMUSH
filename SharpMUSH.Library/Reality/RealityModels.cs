using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Reality;

public sealed record RealityConfiguration(int Version, bool Enabled, string[] Layers)
{
	public static RealityConfiguration Default => new(1, false, ["normal"]);
}

public sealed record ObjectReality(int Version, DBRef Object, string[] Receive, string[] Transmit,
	Dictionary<string, string> Descriptions)
{
	public static ObjectReality Default(DBRef obj) => new(1, obj, ["normal"], ["normal"], []);
}

public interface IRealityPolicy
{
	ValueTask<bool> CanPerceiveAsync(DBRef receiver, DBRef target, CancellationToken ct = default);
	ValueTask<bool> IsEnabledAsync(CancellationToken ct = default);
	ValueTask<string?> DescriptionAttributeAsync(DBRef receiver, DBRef target, CancellationToken ct = default);
}
