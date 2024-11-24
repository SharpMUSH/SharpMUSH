using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Commands;

public static partial class Commands
{
		[SharpCommand(Name = "]", Behavior = CommandBehavior.SingleToken | CommandBehavior.NoParse, MinArgs = 1, MaxArgs = 1)]
		public static async ValueTask<Option<CallState>> NoParse(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
				var newParser = parser.Push(parser.CurrentState with { ParseMode = ParseMode.NoParse });
				// TODO: There is likely a better way to pick this up where this left off, instead of re-parsing.
				await newParser.CommandParse(
					MModule.concat(
						MModule.concat(
							parser.CurrentState.Arguments["0"].Message!,
								MModule.single(" ")),
							parser.CurrentState.Arguments["1"].Message!));

				return new CallState(string.Empty);
		}

		[SharpCommand(Name = "&", Behavior = CommandBehavior.SingleToken | CommandBehavior.NoParse | CommandBehavior.EqSplit,
			MinArgs = 2, MaxArgs = 3)]
		public static async ValueTask<Option<CallState>> Set_Attrib_Ampersand(IMUSHCodeParser parser,
			SharpCommandAttribute _2)
		{
				// This will come in as arg[0] = <attr>, arg[1]: <object> and arg[2] as [value]
				var args = parser.CurrentState.Arguments;
				var enactor = (await parser.CurrentState.EnactorObject(parser.Mediator)).WithoutNone();
				var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).WithoutNone();

				var locate = await parser.LocateService.LocateAndNotifyIfInvalid(parser,
					enactor,
					executor,
					args["1"].Message!.ToString(), Library.Services.LocateFlags.All);

				// Arguments are getting here in an evaluated state, when they should not be.
				if (!locate.IsValid())
				{
						return new CallState(locate.IsError ? locate.AsError.Value : Errors.ErrorCantSeeThat);
				}

				// TODO: Switch to Clear an attribute! Take note of deeper authorization needed in case of the attribute having leaves.
				var realLocated = locate.WithoutError().WithoutNone();
				var contents = args.TryGetValue("2", out var tmpContents) ? tmpContents.Message! : MModule.empty();

				var setResult = await parser.AttributeService.SetAttributeAsync(executor, realLocated, MModule.plainText(args["0"].Message!), contents);
				await parser.NotifyService.Notify(enactor,
					setResult.Match(
						_ => $"{realLocated.Object().Name}/{args["0"].Message} - Set.",
						failure => failure.Value)
					);

				return new CallState(setResult.Match(
					_ => $"{realLocated.Object().Name}/{args["0"].Message}",
					_ => string.Empty));
		}
}