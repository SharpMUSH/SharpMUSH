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
	/// <c>@oob[/supported] &lt;players&gt;=&lt;package&gt;[ &lt;json&gt;]</c>: <c>oob()</c> as a command, so softcode need
	/// not wrap it in <c>think</c>. The package is the right side's first word and the JSON message the rest. Same
	/// targets, permission and delivery as <c>oob()</c> (<see cref="OutOfBandDelivery"/>); silent on success, with the
	/// number of connections reached as its output (<c>%&gt;</c>).
	/// </summary>
	[SharpCommand(Name = "@OOB", Output = CommandOutput.Value, Switches = ["SUPPORTED", "NOEVAL"],
		Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 2, ParameterNames = ["players", "package"])]
	public async ValueTask<Option<CallState>> Oob(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentList;
		var players = args.TryGet(0, out var playersState) ? playersState.Message.ToPlainText().Trim() : string.Empty;
		var right = args.TryGet(1, out var rightState) ? rightState.Message.ToPlainText().Trim() : string.Empty;

		// A right side that opens with JSON has no package: the message would go out under its own text.
		if (players.Length == 0 || right.Length == 0 || right[0] is '{' or '[')
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.OobUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArgument);
		}

		var split = right.IndexOfAny([' ', '\t']);
		var package = split < 0 ? right : right[..split];
		var message = split < 0 ? string.Empty : right[(split + 1)..].Trim();
		var onlySupported = parser.CurrentState.Switches.Contains("SUPPORTED");

		return await OutOfBand.SendAsync(parser, executor, players, package, message, onlySupported) switch
		{
			OutOfBandSent sent => new CallState(sent.Connections.ToString(CultureInfo.InvariantCulture)),
			PermissionRefusal refusal => await NotifyService.PermissionDenied(executor, refusal),
			Error<string> error => await RefuseOutOfBand(executor, error.Value)
		};
	}

	private async ValueTask<CallState> RefuseOutOfBand(AnySharpObject executor, string error)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.OobInvalidJson), executor);
		return new CallState(error);
	}
}
