using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Extensions;

/// <summary>
/// Channel privilege membership.
///
/// <para>Privileges are persisted as a <see cref="string"/> array of names taken from PennMUSH's
/// <c>chan_privs</c> table (<c>src/extchat.c</c>). They reach the database from user input, so their
/// casing is whatever the player typed — <c>@channel/add Foo=wizard</c> stores <c>"wizard"</c>. Every
/// permission check must therefore compare case-insensitively; an ordinal <c>Contains</c> silently
/// answers "no such privilege" for a channel that plainly has it.</para>
/// </summary>
public static class SharpChannelExtensions
{
	public static bool HasPriv(this SharpChannel channel, string priv)
		=> channel.Privs.HasPriv(priv);

	public static bool HasPriv(this string[] privs, string priv)
		=> privs.Contains(priv, StringComparer.OrdinalIgnoreCase);
}
