// NOTE: lives here rather than in SharpMUSH.Library, on the same grounds as TerminalCapabilities — the
// ConnectionServer reads a client's module list too, and must not take a dependency on the full Library.

namespace SharpMUSH.Library.Utilities;

/// <summary>
/// A client's GMCP <c>Core.Supports</c> list as the main process keeps it: connection metadata
/// <see cref="MetadataKey"/>, space-separated <c>Module version</c> pairs (<c>Room 1 Client.Media 1</c>).
/// </summary>
public static class GmcpSupports
{
	/// <summary>The connection metadata key the list is kept under.</summary>
	public const string MetadataKey = "GMCP_SUPPORTS";

	/// <summary>The list as it is kept, from entries such as <c>"Room 1"</c>.</summary>
	public static string Format(IEnumerable<string> modules) => string.Join(' ', modules);

	/// <summary>
	/// The module names in a kept list, without their versions. A version is the number after a name;
	/// a name is never a bare number.
	/// </summary>
	public static IEnumerable<string> ModuleNames(string? listed) =>
		(listed ?? string.Empty)
		.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
		.Where(token => !token.All(char.IsAsciiDigit));

	/// <summary>
	/// TelnetNegotiationCore's rule (<c>GmcpServerSession.Supports</c>): true when the client listed
	/// <paramref name="package"/> or a module it belongs to, so <c>Room</c> covers <c>Room.Info</c>.
	/// Names match without regard to case.
	/// </summary>
	public static bool Lists(string? listed, string package)
	{
		var modules = ModuleNames(listed).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var name = package.Trim();

		while (name.Length > 0)
		{
			if (modules.Contains(name))
			{
				return true;
			}

			var dot = name.LastIndexOf('.');
			name = dot <= 0 ? string.Empty : name[..dot];
		}

		return false;
	}

	/// <summary>
	/// The rule <c>oob(..., supported)</c> sends by: a client that listed nothing has never said, since
	/// Blightmud, TinTin++ and MUSHclient do not send Core.Supports, so it is sent everything; one that
	/// listed modules is sent only what <see cref="Lists"/> finds.
	/// </summary>
	public static bool Accepts(string? listed, string package) =>
		!ModuleNames(listed).Any() || Lists(listed, package);
}
