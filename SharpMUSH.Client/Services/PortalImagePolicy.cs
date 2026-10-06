using SharpMUSH.Library.Markup;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The game's <c>image_hosts</c> and <c>image_host_list</c>, as <c>api/server-info</c> last reported them,
/// which the markup registry holds every layout picture to as it renders.
/// </summary>
/// <remarks>
/// Static because the registry is built once, before the first server answer, and reads it on every
/// render. The game also applies the policy when <c>figure()</c> runs; this covers text stored before
/// the policy changed. Until the server has answered nothing but the game's own pictures is shown.
/// </remarks>
public static class PortalImagePolicy
{
	private static volatile Policy _current = new("allow", string.Empty);

	private sealed record Policy(string Mode, string Hosts);

	/// <summary>Records the policy the server reported; a server that reports none allows every host.</summary>
	public static void Set(string? mode, string? hosts) =>
		_current = new Policy(string.IsNullOrWhiteSpace(mode) ? "any" : mode, hosts ?? string.Empty);

	/// <summary>Whether a picture at <paramref name="source"/> may be shown.</summary>
	public static bool Allows(string source)
	{
		var policy = _current;
		return ImageHostPolicy.Allows(source, policy.Mode, policy.Hosts);
	}
}
