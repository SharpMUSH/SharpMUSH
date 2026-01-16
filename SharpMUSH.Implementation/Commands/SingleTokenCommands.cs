using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "]", Behavior = CommandBehavior.SingleToken | CommandBehavior.NoParse, MinArgs = 1, MaxArgs = 1, ParameterNames = [])]
	public async ValueTask<Option<CallState>> NoParse(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Re-parse the command with NoEval mode. This is necessary because the command
		// was already tokenized in Default mode by the time we reach this handler.
		// The "]" prefix changes evaluation semantics for the entire command.
		var oldCommand = MModule.multipleWithDelimiter(MModule.single(" "),
		[
			parser.CurrentState.Arguments["0"].Message!,
			parser.CurrentState.Arguments["1"].Message!
		]);

		await parser.With(s => s with { ParseMode = ParseMode.NoEval },
			async np => await np.CommandParse(oldCommand));
		
		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "&", Behavior = CommandBehavior.SingleToken | CommandBehavior.NoParse | CommandBehavior.EqSplit,
		MinArgs = 2, MaxArgs = 3, ParameterNames = ["object/attribute", "value"])]
	public async ValueTask<Option<CallState>> SetAttribute(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
	{
		// This will come in as arg[0] = <attr>, arg[1]: <object> and arg[2] as [value]
		var args = parser.CurrentState.Arguments;
		var enactor = (await parser.CurrentState.EnactorObject(_mediator!)).WithoutNone();
		var executor = (await parser.CurrentState.ExecutorObject(_mediator!)).WithoutNone();

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			enactor,
			executor,
			args["1"].Message!.ToString(), LocateFlags.All, async realLocated =>
			{
				var contents = args.TryGetValue("2", out var tmpContents) ? tmpContents.Message! : MModule.empty();

				var setResult =
					await _attributeService!.SetAttributeAsync(executor, realLocated, MModule.plainText(args["0"].Message!),
						contents);
				await _notifyService!.Notify(enactor,
					setResult.Match(
						_ => $"{realLocated.Object().Name}/{args["0"].Message} - Set.",
						failure => failure.Value)
				);

				return new CallState(setResult.Match(
					_ => $"{realLocated.Object().Name}/{args["0"].Message}",
					_ => string.Empty));
			});
	}
}