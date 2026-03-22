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
	public static async ValueTask<Option<CallState>> NoParse(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

	// RSNoParse: only the RHS value is kept unevaluated (deferred/literal).
	// The LHS (object/attribute name slot) is evaluated normally by ArgumentSplit so that
	// register substitutions like %q0 in "& attr %q0=[value]" resolve before the locate step.
	// This matches PennMUSH's CS_NOPARSE semantics for &, which apply only to the stored value.
	// RSBrace: braces in the RHS are preserved during ANTLR parsing so that
	// `& ATTR OBJ={code}` stores `{code}` verbatim (matching PennMUSH get(OBJ/ATTR) behavior).
	// The SetAttribute handler handles DirectInput vs queue context to evaluate vs store raw.
	[SharpCommand(Name = "&", Behavior = CommandBehavior.SingleToken | CommandBehavior.RSNoParse | CommandBehavior.RSBrace | CommandBehavior.EqSplit,
		MinArgs = 2, MaxArgs = 3, ParameterNames = ["object/attribute", "value"])]
	public static async ValueTask<Option<CallState>> SetAttribute(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
	{
		// This will come in as arg[0] = <attr>, arg[1]: <object> and arg[2] as [value]
		var args = parser.CurrentState.Arguments;
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var executor = (await parser.CurrentState.ExecutorObject(Mediator!)).WithoutNone();

		// The attribute name (arg["0"]) is extracted from the raw command token (e.g. &hdr_%q1 obj=val
		// → attr="hdr_%q1"). In PennMUSH, the attribute name IS evaluated so that register
		// substitutions like %q1 resolve to their current values before the attribute is set.
		var attrNameRaw = args["0"].Message ?? MModule.empty();
		var attrNameParsed = (await parser.FunctionParse(attrNameRaw))?.Message ?? attrNameRaw;
		var attrName = MModule.plainText(attrNameParsed);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			enactor,
			executor,
			args["1"].Message!.ToString(), LocateFlags.All | LocateFlags.NoVisibilityCheck, async realLocated =>
			{
				MString contents;
				if (args.TryGetValue("2", out var tmpContents))
				{
					// PennMUSH QUEUE_NOLIST behavior via ParserStateFlags.DirectInput:
					// - DirectInput set   → command came directly from a player's network connection;
					//   treat the value as literal code (NoParse — no function evaluation).
					// - DirectInput clear → command is running from a queue/callback (@wait, @trigger,
					//   @force, etc.); evaluate the value before storage, matching PennMUSH behavior.
					contents = parser.CurrentState.Flags.HasFlag(ParserStateFlags.DirectInput)
						? tmpContents.Message!
						: await tmpContents.ParsedMessage() ?? MModule.empty();
				}
				else
				{
					contents = MModule.empty();
				}

				var setResult =
					await AttributeService!.SetAttributeAsync(executor, realLocated, attrName, contents);
				await NotifyService!.Notify(enactor,
					setResult.Match(
						_ => $"{realLocated.Object().Name}/{attrNameParsed} - Set.",
						failure => failure.Value)
				);

				return new CallState(setResult.Match(
					_ => $"{realLocated.Object().Name}/{attrNameParsed}",
					_ => string.Empty));
			});
	}
}