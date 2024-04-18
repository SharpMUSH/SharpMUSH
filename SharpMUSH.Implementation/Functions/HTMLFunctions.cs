using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions
{
	public partial class Functions
	{
		[SharpFunction(Name = "HTML", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
		public static CallState html(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "TAG", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
		public static CallState tag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ENDTAG", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState endtag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "TAGWRAP", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState tagwrap(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "WSJSON", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState websocket_json(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "WSHTML", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState websocket_html(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
	}
}