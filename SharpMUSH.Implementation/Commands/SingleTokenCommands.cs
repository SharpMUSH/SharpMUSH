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
		parser.Push(parser.CurrentState with { ParseMode = ParseMode.NoParse });
		// TODO: There is likely a better way to pick this up where this left off, instead of re-parsing.
		await parser.CommandParse(
			MModule.concat(
				MModule.concat(
					parser.CurrentState.Arguments[0].Message!, 
						MModule.single(" ")), 
					parser.CurrentState.Arguments[1].Message!) );
		parser.Pop();
		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "&", Behavior = CommandBehavior.SingleToken | CommandBehavior.NoParse | CommandBehavior.EqSplit,
		MinArgs = 2, MaxArgs = 3)]
	public static async ValueTask<Option<CallState>> Set_Attrib_Ampersand(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
	{
		// This will come in as arg[0] = <attr>, arg[1]: <object> and arg[2] as [value]
		var args = parser.CurrentState.Arguments;
		var enactor = (await parser.CurrentState.Enactor!.Value.GetAsync(parser.Database)).WithoutNone();

		var locate = await parser.LocateService.LocateAndNotifyIfInvalid(parser,
			enactor,
			enactor,
			args[1].Message!.ToString(), Library.Services.LocateFlags.All);

		// Arguments are getting here in an evaluated state, when they should not be.
		if (!locate.IsValid())
		{
			return new CallState(locate.IsError ? locate.AsError.Value : Errors.ErrorCantSeeThat);
		}

		// TODO: Switch to Clear an attribute! Take note of deeper authorization needed in case of the attribute having leaves.
		var realLocated = locate.WithoutError().WithoutNone();
		var attributePath = args[0].Message!.ToString()!.ToUpper().Split('`');
		var contents = args[2]?.Message?.ToString() ?? string.Empty;
		var callerObj = await parser.CurrentState.Caller!.Value.GetAsync(parser.Database);
		var callerOwner = callerObj.Object()!.Owner();

		await parser.Database.SetAttributeAsync(realLocated.Object().DBRef, attributePath, contents, callerOwner);

		await parser.NotifyService.Notify(parser.CurrentState.Enactor!.Value,
			$"{realLocated.Object().Name}/{string.Join("`", attributePath)} - Set.");

		return new CallState(string.Empty);
	}
}