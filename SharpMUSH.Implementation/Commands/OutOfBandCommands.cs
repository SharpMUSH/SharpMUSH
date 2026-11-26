using System.Globalization;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// <c>@oob[/supported][/noeval][/silent|/noisy] &lt;players&gt;=&lt;package&gt;[,&lt;message&gt;]</c>: <c>oob()</c> as a
	/// command, so softcode need not wrap it in <c>think</c>. The arguments are <c>oob()</c>'s, with <c>/supported</c> for
	/// its fourth. Same targets, permission and delivery as <c>oob()</c> (<see cref="OutOfBandDelivery"/>). It confirms
	/// as <c>@pemit</c> does, unless <c>/silent</c> or <c>silent_pemit</c>; its output (<c>%&gt;</c>) is the number of
	/// connections reached.
	/// </summary>
	[SharpCommand(Name = "@OOB", Output = CommandOutput.Value, Switches = ["SUPPORTED", "NOEVAL", "SILENT", "NOISY"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0, MaxArgs = 3, ParameterNames = ["players", "package", "message"])]
	public async ValueTask<Option<CallState>> Oob(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentList;
		var players = args.TryGet(0, out var playersState) ? playersState.Message.ToPlainText().Trim() : string.Empty;
		var package = args.TryGet(1, out var packageState) ? packageState.Message.ToPlainText().Trim() : string.Empty;
		var message = args.TryGet(2, out var messageState) ? messageState.Message.ToPlainText().Trim() : string.Empty;

		if (players.Length == 0 || package.Length == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.OobUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArgument);
		}

		var switches = parser.CurrentState.Switches;
		var onlySupported = switches.Contains("SUPPORTED");
		// cmd_pemit's SILENT_OR_NOISY(sw, SILENT_PEMIT): confirm unless /silent, or silent_pemit without /noisy.
		var silent = switches.Contains("SILENT")
			|| (!switches.Contains("NOISY") && Configuration.CurrentValue.Compatibility.SilentPEmit);

		return await OutOfBand.SendAsync(parser, executor, players, package, message, onlySupported) switch
		{
			OutOfBandSent sent => await ConfirmOutOfBand(executor, package, sent.Connections, silent),
			PermissionRefusal refusal => await NotifyService.PermissionDenied(executor, refusal),
			Error<string> error => await RefuseOutOfBand(executor, error.Value)
		};
	}

	private async ValueTask<CallState> ConfirmOutOfBand(AnySharpObject executor, string package, int connections, bool silent)
	{
		if (!silent)
		{
			await (connections == 1
				? NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.OobSentToOneConnectionFormat), executor, package)
				: NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.OobSentToConnectionsFormat), executor, package, connections));
		}

		return new CallState(connections.ToString(CultureInfo.InvariantCulture));
	}

	private async ValueTask<CallState> RefuseOutOfBand(AnySharpObject executor, string error)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.OobInvalidJson), executor);
		return new CallState(error);
	}
}
