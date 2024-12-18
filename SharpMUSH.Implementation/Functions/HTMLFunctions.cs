using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "HTML", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
	public static ValueTask<CallState> html(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: This probably needs to be more complex than that.
		return new ValueTask<CallState>(new CallState(
			MModule.concat(
				MModule.concat(
					MModule.single("<"),
					parser.CurrentState.Arguments["1"].Message),
				MModule.single(">"))));
	}

	[SharpFunction(Name = "TAG", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> tag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "ENDTAG", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> endtag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "TAGWRAP", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> tagwrap(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "WSJSON", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> websocket_json(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "WSHTML", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> websocket_html(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}