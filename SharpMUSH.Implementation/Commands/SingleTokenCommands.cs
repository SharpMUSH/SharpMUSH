using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "]", Behavior = CommandBehavior.SingleToken | CommandBehavior.NoParse, MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> NoParse(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: There is likely a better way to pick this up where this left off, instead of re-parsing.
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
		MinArgs = 2, MaxArgs = 3)]
	public static async ValueTask<Option<CallState>> SetAttribute(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
	{
		// This will come in as arg[0] = <attr>, arg[1]: <object> and arg[2] as [value]
		var args = parser.CurrentState.Arguments;
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var executor = (await parser.CurrentState.ExecutorObject(Mediator!)).WithoutNone();

		var locate = await LocateService!.LocateAndNotifyIfInvalid(parser,
			enactor,
			executor,
			args["1"].Message!.ToString(), LocateFlags.All);

		// Arguments are getting here in an evaluated state, when they should not be.
		if (!locate.IsValid())
		{
			return new CallState(locate.IsError ? locate.AsError.Value : Errors.ErrorCantSeeThat);
		}

		// TODO: Switch to Clear an attribute! Take note of deeper authorization needed in case of the attribute having leaves.
		var realLocated = locate.WithoutError().WithoutNone();
		var contents = args.TryGetValue("2", out var tmpContents) ? tmpContents.Message! : MModule.empty();

		var setResult =
			await AttributeService!.SetAttributeAsync(executor, realLocated, MModule.plainText(args["0"].Message!),
				contents);
		await NotifyService!.Notify(enactor,
			setResult.Match(
				_ => $"{realLocated.Object().Name}/{args["0"].Message} - Set.",
				failure => failure.Value)
		);

		return new CallState(setResult.Match(
			_ => $"{realLocated.Object().Name}/{args["0"].Message}",
			_ => string.Empty));
	}
}