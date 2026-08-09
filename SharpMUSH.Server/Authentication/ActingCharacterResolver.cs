using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// The single rule for "which character is this session acting as", shared by every caller that
/// answers it: <see cref="AccountSessionAuthenticationHandler"/> (which turns it into claims),
/// <c>AccountController.GetCharacters</c> (which reports it as <c>isActing</c> on the roster), and
/// <c>AuthController.AccountLogin</c> (which binds it into the token it mints). They used to derive
/// it separately and could disagree.
/// </summary>
public static class ActingCharacterResolver
{
	/// <summary>
	/// The character a session with no explicit binding falls back to: the account's lowest-dbref
	/// character.
	/// </summary>
	/// <remarks>
	/// The tie-break has to be defined rather than incidental, because <c>GetCharactersAsync</c>
	/// passes the backend query through unsorted — an unordered pick would name a different
	/// character on different requests for a multi-character account. Lowest dbref is the account's
	/// oldest surviving character, and it is the same rule login has always used to choose the
	/// character it binds, so an implicitly-resolved session and a freshly-logged-in one always
	/// agree on who they are.
	/// </remarks>
	public static SharpPlayer? Primary(IReadOnlyList<SharpPlayer> characters)
		=> characters.OrderBy(c => c.Object.Key).FirstOrDefault();

	/// <summary>
	/// The character <paramref name="session"/> acts as, re-checked against the live
	/// <paramref name="characters"/> roster on every call.
	/// </summary>
	/// <remarks>
	/// <para>
	/// An explicit binding always wins and never falls back: a session that names a character acts
	/// as that character or as nobody. That keeps <c>AuthController.SwitchCharacter</c> — the only
	/// way to choose a character — authoritative, and it keeps a character unlinked after the token
	/// was minted from silently redirecting to a different one.
	/// </para>
	/// <para>
	/// A session that names NO character is the "registered, no character yet" case: the session is
	/// minted at registration, before the account owns anything to bind. It resolves to
	/// <see cref="Primary"/> so that creating the first character takes effect on the very next
	/// request, instead of leaving the registration session permanently characterless — every write
	/// needing a character identity refused it, and the roster reported the account's only character
	/// as not acting, until the player logged out and back in.
	/// </para>
	/// </remarks>
	public static SharpPlayer? Resolve(
		IAccountSessionStore.SessionIdentity? session, IReadOnlyList<SharpPlayer> characters)
	{
		if (session is not { } identity)
			return null;

		if (identity.CharacterKey is not { } key)
			return Primary(characters);

		return characters.FirstOrDefault(c => c.Object.Key == key
			&& c.Object.CreationTime == identity.CharacterCreationTime);
	}
}
