using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The locate shapes that are not operations of their own. Each is <see cref="ILocateService.Locate"/>
/// or <see cref="ILocateService.LocateAndNotifyIfInvalid"/> with a fixed flag set or a different return
/// type, so none of them can report a failed locate differently from the noisy primitive.
/// </summary>
public static class LocateServiceExtensions
{
	// A player-name match is GLOBAL in PennMUSH: pmatch()/player lookup resolves any player by name
	// regardless of where they stand or whether the looker can "see" them. It no longer needs a flag to
	// say so — the dark/can-examine gate that used to reject a perfectly valid player here (404'ing
	// GET /api/profile/<name> for every character, since the profile http_handler #4 is neither near nor
	// a controller) was fun_locate's, and has gone back there.
	// AbsoluteMatch because lookup_player resolves "#1" as readily as a name, and every caller of this
	// helper hands it whatever the user typed. It used to arrive by accident: the flag set names a scope,
	// so nothing was injected, and a dbref reached no scope at all.
	private const LocateFlags PlayerMatchFlags =
		LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference | LocateFlags.EnglishStyleMatching |
		LocateFlags.MatchOptionalWildCardForPlayerName | LocateFlags.AbsoluteMatch;

	/// <summary>
	/// <see cref="ILocateService.LocateAndNotifyIfInvalid"/>, answered as a function argument: the object,
	/// or the error string a function returns in its place (<c>#-1 NO MATCH</c> for a plain miss).
	/// </summary>
	public static async ValueTask<AnySharpObjectOrErrorCallState> LocateAndNotifyIfInvalidWithCallState(
		this ILocateService locate,
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name,
		LocateFlags flags)
		=> await locate.LocateAndNotifyIfInvalid(parser, looker, executor, name, flags) switch
		{
			AnySharpObject found => found,
			Error<string> error => new Error<CallState>(new CallState(error.Value)),
			_ => new Error<CallState>(new CallState(ErrorMessages.Returns.NoMatch))
		};

	public static async ValueTask<CallState> LocateAndNotifyIfInvalidWithCallStateFunction(
		this ILocateService locate,
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name,
		LocateFlags flags,
		Func<AnySharpObject, ValueTask<CallState>> foundFunc)
		=> await locate.LocateAndNotifyIfInvalidWithCallState(parser, looker, executor, name, flags) switch
		{
			Error<CallState> error => error.Value,
			AnySharpObject obj => await foundFunc(obj)
		};

	public static async ValueTask<CallState> LocateAndNotifyIfInvalidWithCallStateFunction(
		this ILocateService locate,
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name,
		LocateFlags flags,
		Func<AnySharpObject, CallState> foundFunc)
		=> await locate.LocateAndNotifyIfInvalidWithCallState(parser, looker, executor, name, flags) switch
		{
			Error<CallState> error => error.Value,
			AnySharpObject obj => foundFunc(obj)
		};

	/// <summary>
	/// The player half of PennMUSH's <c>lookup_desc()</c> (src/bsd.c), which is how every connection
	/// function resolves its argument: <c>lookup_player()</c> first, and on a miss
	/// <c>match_result(executor, name, TYPE_PLAYER, MAT_ABSOLUTE | MAT_PLAYER | MAT_ME | MAT_TYPE)</c>.
	/// <para>
	/// Two differences from <see cref="LocatePlayerAndNotifyIfInvalid"/>, both load-bearing. The
	/// <c>MAT_ME</c> means "me" resolves to the caller, which is what a player types and what every
	/// one of <c>terminfo()</c>, <c>ssl()</c>, <c>host()</c> and their siblings is asked with. And it
	/// is silent: those are functions, they answer with a string, and the notifying locate was
	/// emitting "I can't see that here." into the player's own output every time one of them was
	/// called with a name that did not resolve.
	/// </para>
	/// </summary>
	public static ValueTask<AnyOptionalSharpObjectOrError> LocateConnectionTarget(
		this ILocateService locate,
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name)
		=> locate.Locate(parser, looker, executor, name, PlayerMatchFlags | LocateFlags.MatchMeForLooker);

	public static ValueTask<AnyOptionalSharpObjectOrError> LocatePlayerAndNotifyIfInvalid(
		this ILocateService locate,
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name)
		=> locate.LocateAndNotifyIfInvalid(parser, looker, executor, name, PlayerMatchFlags);

	public static ValueTask<AnySharpObjectOrErrorCallState> LocatePlayerAndNotifyIfInvalidWithCallState(
		this ILocateService locate,
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name)
		=> locate.LocateAndNotifyIfInvalidWithCallState(parser, looker, executor, name, PlayerMatchFlags);

	public static async ValueTask<CallState> LocatePlayerAndNotifyIfInvalidWithCallStateFunction(
		this ILocateService locate,
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name,
		Func<SharpPlayer, ValueTask<CallState>> foundFunc)
		=> await locate.LocatePlayerAndNotifyIfInvalidWithCallState(parser, looker, executor, name) switch
		{
			Error<CallState> error => error.Value,
			AnySharpObject and SharpPlayer player => await foundFunc(player),
			AnySharpObject other => throw new InvalidOperationException(
				$"A player-only locate matched {other.Object().DBRef}, which is not a player.")
		};

	public static ValueTask<AnyOptionalSharpObjectOrError> LocatePlayer(
		this ILocateService locate,
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name)
		=> locate.Locate(parser, looker, executor, name, PlayerMatchFlags);
}
