using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

/// <summary>
/// Commands TinyMUX and RhostMUSH have and SharpMUSH has no name for, reached by their own names only under
/// those dialects (<see cref="Library.Softcode.SoftcodeDialects"/>). Each has a <c>@dialect_</c> name of its
/// own, so a SharpMUSH game's names and abbreviations stay as they are.
/// </summary>
public partial class Commands
{
	/// <summary>
	/// <c>@last [&lt;player&gt;]</c>: where and when the player last connected, and the last connection that
	/// failed. Without a player, the executor's owner. Only staff and those who control the player may look.
	/// </summary>
	[SharpCommand(Name = "@DIALECT_LAST", Switches = [], Behavior = CB.Default | CB.NoGuest, MinArgs = 0, MaxArgs = 1,
		ParameterNames = ["player"])]
	public async ValueTask<Option<CallState>> DialectLast(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var name = parser.CurrentState.ArgumentsOrdered.GetValueOrDefault("0")?.Message.ToPlainText().Trim() ?? string.Empty;
		if (name.Length == 0 || name.Equals("me", StringComparison.OrdinalIgnoreCase))
		{
			AnySharpObject owner = await executor.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken);
			return await LastConnectsAsync(executor, owner);
		}

		return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, name) switch
		{
			AnySharpObject player => await LastConnectsAsync(executor, player),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> LastConnectsAsync(AnySharpObject executor, AnySharpObject player)
	{
		if (!await executor.IsWizard() && !await executor.IsRoyalty() && !await PermissionService.Controls(executor, player))
			return await NotifyService.PermissionDeniedNotControlled(executor, player);

		var playerRef = player.Object().DBRef;
		var last = await PlainAttributeAsync(playerRef, "LAST");
		var site = await PlainAttributeAsync(playerRef, "LASTSITE");
		var failed = (await PlainAttributeAsync(playerRef, "LASTFAILED"))?.Trim();
		await NotifyService.Notify(executor, string.IsNullOrWhiteSpace(last)
			? $"{player.Object().Name} has no connection on record."
			: $"Last connect: from {(string.IsNullOrWhiteSpace(site) ? "an unknown site" : site)} on {last}.");
		await NotifyService.Notify(executor, string.IsNullOrEmpty(failed)
			? "No failed connect since then."
			: $"Last failed connect: from {failed}.");
		return CallState.Empty;
	}

	/// <summary>
	/// <c>PUEBLOCLIENT &lt;version&gt;</c>: a Pueblo client's answer to the game's Pueblo greeting, which turns
	/// the connection's Pueblo output on as <c>SOCKSET PUEBLO=</c> does. The greeting goes out, and the client's
	/// usual answer is read before it gets here, only while the <c>pueblo</c> option is on; while it is off the
	/// line does nothing.
	/// </summary>
	[SharpCommand(Name = "PUEBLOCLIENT", Switches = [], Behavior = CB.SOCKET | CB.NoParse, MinArgs = 0, MaxArgs = 0,
		ParameterNames = ["version"])]
	public async ValueTask<Option<CallState>> PuebloClient(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (!Configuration.CurrentValue.Net.Pueblo || CurrentConnection(parser) is not { } connection)
			return new None();

		var result = SocketOptions.Set(connection, "PUEBLO", $"PUEBLOCLIENT {SocketArgument(parser)}");
		await PublishSocketPinsAsync(connection, result.Probe, result.Pueblo, result.Telnet);
		return new None();
	}
}
