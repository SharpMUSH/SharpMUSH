using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Common;

/// <summary>
/// Resolves object-reference arguments (rooms, players) in the scene functions and the <c>@scene</c>
/// command handlers through the engine's <see cref="ILocateService"/> — so <c>here</c>, <c>me</c>,
/// player names, <c>*name</c>, and dbrefs all resolve the same way they do for every other engine
/// function, instead of the raw <c>DBRef.TryParse</c>-only resolution the storage layer does.
///
/// <para>Resolution is always from the ENACTOR's (<c>%#</c>) perspective, not the executor's: scene
/// verbs run on the WIZARD Scene Logger, so keying on the executor would make <c>here</c>/<c>me</c>
/// resolve to the logger (#2 Master Room) rather than the player who actually typed the command. This
/// mirrors the enactor-keyed reasoning in <c>SceneFunctions.SceneVisibleToAsync</c>.</para>
///
/// <para>Non-notifying by design: a miss returns <c>null</c> and callers fall back to the raw text
/// (so this is purely additive — it upgrades a resolvable reference to a concrete dbref and never makes
/// an existing call stricter). That also keeps the capture hot path — <c>scenewhere(loc(%#))</c> /
/// <c>scenefocus(%#)</c> on every pose — free of "I can't see that" notification spam.</para>
/// </summary>
public static class SceneLocate
{
	// Room / object references: here, me, dbref, and locally-visible names. LocateFlags.All carries
	// MatchHereForLookerLocation + MatchMeForLooker + AbsoluteMatch.
	private const LocateFlags ObjectFlags = LocateFlags.All;

	// Player references: me, dbref, and GLOBAL player names / *name. The built-in PlayerMatchFlags omits
	// MatchMeForLooker (so it would not resolve "me") and AbsoluteMatch (so it would not resolve "#N"),
	// so spell out a player-matching set that also covers those two forms the scene softcode relies on.
	private const LocateFlags PlayerFlags =
		LocateFlags.PlayersPreference | LocateFlags.MatchMeForLooker | LocateFlags.AbsoluteMatch |
		LocateFlags.MatchOptionalWildCardForPlayerName | LocateFlags.EnglishStyleMatching |
		LocateFlags.NoVisibilityCheck;

	/// <summary>
	/// Resolves a room/object reference to a concrete dbref string, or returns <paramref name="name"/>
	/// unchanged when it is empty or does not resolve (additive fallback).
	/// </summary>
	public static async ValueTask<string> ObjectOrSelf(IMUSHCodeParser parser, string name)
		=> await ResolveAsync(parser, name, ObjectFlags) ?? name;

	/// <summary>
	/// Resolves a player reference to a concrete dbref string, or returns <paramref name="name"/>
	/// unchanged when it is empty or does not resolve (additive fallback).
	/// </summary>
	public static async ValueTask<string> PlayerOrSelf(IMUSHCodeParser parser, string name)
		=> await ResolveAsync(parser, name, PlayerFlags) ?? name;

	private static async ValueTask<string?> ResolveAsync(IMUSHCodeParser parser, string name, LocateFlags flags)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}

		var mediator = parser.ServiceProvider.GetRequiredService<IMediator>();
		var locate = parser.ServiceProvider.GetRequiredService<ILocateService>();
		var enactor = await parser.CurrentState.KnownEnactorObject(mediator);

		var located = await locate.Locate(parser, enactor, enactor, name, flags);
		return located.IsAnyObject ? located.AsAnyObject.Object().DBRef.ToString() : null;
	}
}
