using OneOf.Monads;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Commands;

public static partial class Commands
{
	[SharpCommand(Name = "]", Behavior = CommandBehavior.SingleToken | CommandBehavior.NoParse, MinArgs = 1, MaxArgs = 1)]
	public static Option<CallState> NoParse(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		parser.Push(parser.CurrentState with { ParseMode = ParseMode.NoParse });
		parser.CommandParse(parser.CurrentState.Handle!, parser.CurrentState.Arguments[0]!.Message!).GetAwaiter().GetResult();
		parser.Pop();
		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "&", Behavior = CommandBehavior.SingleToken | CommandBehavior.NoParse | CommandBehavior.EqSplit, MinArgs = 2, MaxArgs = 3)]
	public static Option<CallState> Set_Attrib_Ampersand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// This will come in as arg[0] = <attr>, arg[1]: <object> and arg[2] as [value]
		var args = parser.CurrentState.Arguments;
		var enactor = parser.CurrentState.Enactor!.Value.Get(parser.Database).WithoutNone();

		var locate = parser.LocateService.LocateAndNotifyIfInvalid(parser, enactor, enactor, args[1]!.Message!.ToString(), Library.Services.LocateFlags.All);
		
		// Arguments are getting here in an evaluated state, when they should not be.
		if(locate.IsError())
		{
			return new CallState(locate.AsError.Value);
		}
		if(locate.IsNone())
		{
			return new CallState(Errors.ErrorCantSeeThat);
		}

		// TODO: Switch to Clear an attribute! Take note of deeper authorization needed in case of the attribute having leaves.
		var realLocated = locate.WithoutError().WithoutNone();
		var attributePath = args[0]!.Message!.ToString()!.ToUpper().Split('`');
		var contents = args[2]?.Message?.ToString() ?? string.Empty;
		var callerObj = parser.CurrentState.Caller!.Value.Get(parser.Database);
		var callerOwner = callerObj.Object()!.Owner();
		
		parser.Database.SetAttributeAsync(realLocated.Object().DBRef, attributePath, contents, 
			callerOwner);
		
		parser.NotifyService.Notify(parser.CurrentState.Enactor!.Value, $"{realLocated.Object().Name}/{string.Join("`",attributePath)} - Set.");
		
		return new CallState(string.Empty);
	}
}