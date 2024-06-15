using Antlr4.Runtime;
using OneOf.Monads;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Commands
{
	public static partial class Commands
	{
		[SharpCommand(Name = "]", Behavior = Definitions.CommandBehavior.SingleToken | Definitions.CommandBehavior.NoParse, MinArgs = 1, MaxArgs = 1)]
		public static Option<CallState> NoParse(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			parser.NotifyService.Notify(parser.CurrentState.Enactor!.Value, parser.CurrentState.Arguments[0].ToString());

			return new CallState(string.Empty);
		}

		[SharpCommand(Name = "&", Behavior = Definitions.CommandBehavior.SingleToken | Definitions.CommandBehavior.NoParse | Definitions.CommandBehavior.EqSplit, MinArgs = 2, MaxArgs = 3)]
		public static Option<CallState> Set_Attrib_Ampersand(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			// This will come in as arg[0] = <attr>, arg[1]: <object> and arg[2] as [value]
			var args = parser.CurrentState.Arguments;
			var enactor = parser.CurrentState.Enactor!.Value.Get(parser.Database).WithoutNone();

			var locate = Functions.Functions.Locate(parser, enactor, enactor, args[1]!.Message!.ToString(), Functions.Functions.LocateFlags.All);
				
			if(locate.IsError())
			{
				// TODO: Notify
				return new CallState(locate.AsT5.Value);
			}
			if(locate.IsNone())
			{
				// TODO: Notify
				return new CallState(locate.AsT5.Value);
			}

			var realLocated = locate.WithoutError().WithoutNone();
			var attributePath = args[0]!.Message!.ToString()!.Split('`');
			var contents = args[2]?.Message?.ToString() ?? string.Empty;
			var callerObj = parser.CurrentState.Caller!.Value.Get(parser.Database);
			var callerOwner = callerObj.Object()!.Owner();
			
			parser.Database.SetAttributeAsync(realLocated.Object().DBRef, attributePath, contents, 
				callerOwner);
			
			parser.NotifyService.Notify(parser.CurrentState.Enactor!.Value, "Set!");
			
			return new CallState(string.Empty);
		}
	}
}
