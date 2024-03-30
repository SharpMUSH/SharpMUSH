using SharpMUSH.Implementation.Definitions;

namespace SharpMUSH.Implementation.Functions
{
	public partial class Functions
	{
		[SharpFunction(Name = "HTML", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
		public static CallState html(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "TAG", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
		public static CallState tag(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ENDTAG", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState endtag(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "TAGWRAP", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState tagwrap(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "WSJSON", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState websocket_json(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "WSHTML", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState websocket_html(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
	}
}