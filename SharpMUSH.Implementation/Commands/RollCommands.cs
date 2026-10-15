using SharpMUSH.Implementation.Dice;
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
	/// @ROLL — rolls a formula where everyone in the room sees it, and keeps the roll, so anyone can
	/// check it later with rollinfo(). Both sides are evaluated, as any command's are, so a $-command
	/// can hand on <c>%0</c>; <c>/noeval</c> takes the formula as typed, for labels in <c>[]</c> and
	/// pools in <c>{}</c>. <c>%></c> is the roll's id.
	/// </summary>
	[SharpCommand(Name = "@ROLL", Output = CommandOutput.Value, Switches = ["NOEVAL"],
		Behavior = CB.Default | CB.EqSplit | CB.NoParse | CB.NoGagged, MinArgs = 0, MaxArgs = 2,
		ParameterNames = ["formula", "data"])]
	public async ValueTask<Option<CallState>> RollCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var formula = !args.TryGetValue("0", out var formulaArg) ? string.Empty
			: parser.CurrentState.Switches.Contains("NOEVAL") ? formulaArg.Message.ToPlainText().Trim()
			: (await formulaArg.ParsedMessage())?.ToPlainText().Trim() ?? string.Empty;
		if (formula.Length == 0)
		{
			await NotifyService.Notify(executor, "Roll what?", executor);
			return CallState.Empty;
		}

		var data = args.TryGetValue("1", out var dataArg) && await dataArg.ParsedMessage() is { } parsed
			? parsed.ToPlainText().Trim()
			: string.Empty;

		return await RollData.RollAsync(parser, executor, LocateService, AttributeService, formula, data, DiceRollMode.Random) switch
		{
			DiceRollResult result => await AnnounceRollAsync(executor, result),
			Error<string> error => await RollRefusedAsync(executor, error.Value)
		};
	}

	private async ValueTask<CallState> AnnounceRollAsync(AnySharpObject executor, DiceRollResult result)
	{
		var recorded = await RollData.RecordAsync(Mediator, executor, executor, result);
		var line = $"{executor.Object().Name} rolls {result.Text} (roll {recorded.Id})";
		await CommunicationService.SendToRoomAsync(executor, await executor.Where(), _ => MarkupText.Plain(line),
			INotifyService.NotificationType.Emit);
		return new CallState(recorded.Id);
	}

	private async ValueTask<CallState> RollRefusedAsync(AnySharpObject executor, string error)
	{
		await NotifyService.Notify(executor, error, executor);
		return new CallState(error);
	}
}
