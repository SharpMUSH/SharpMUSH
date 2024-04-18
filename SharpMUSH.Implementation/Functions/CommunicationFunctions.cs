using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions
{
	public partial class Functions
	{
		[SharpFunction(Name = "EMIT", MinArgs = 1, MaxArgs = 1, Flags= FunctionFlags.Regular)]
		public static CallState emit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "LEMIT", MinArgs = 1, MaxArgs = 1, Flags= FunctionFlags.Regular)]
		public static CallState lemit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "MESSAGE", MinArgs = 3, MaxArgs = 14, Flags = FunctionFlags.Regular)]
		public static CallState message(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "NSEMIT", MinArgs = 1, MaxArgs = 1, Flags= FunctionFlags.Regular)]
		public static CallState nsemit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "NSLEMIT", MinArgs = 1, MaxArgs = 1, Flags= FunctionFlags.Regular)]
		public static CallState nslemit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "NSOEMIT", MinArgs = 2, MaxArgs = 2, Flags= FunctionFlags.Regular)]
		public static CallState nsoemit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "NSPEMIT", MinArgs = 2, MaxArgs = 2, Flags= FunctionFlags.Regular)]
		public static CallState nspemit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "NSPROMPT", MinArgs = 2, MaxArgs = 2, Flags= FunctionFlags.Regular)]
		public static CallState nsprompt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "NSREMIT", MinArgs = 2, MaxArgs = 2, Flags= FunctionFlags.Regular)]
		public static CallState nsremit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "NSZEMIT", MinArgs = 2, MaxArgs = 2, Flags= FunctionFlags.Regular)]
		public static CallState nzemit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "OEMIT", MinArgs = 2, MaxArgs = 2, Flags= FunctionFlags.Regular)]
		public static CallState oemit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "PEMIT", MinArgs = 2, MaxArgs = 2, Flags= FunctionFlags.Regular)]
		public static CallState pemit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "PROMPT", MinArgs = 2, MaxArgs = 2, Flags= FunctionFlags.Regular)]
		public static CallState prompt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "REMIT", MinArgs = 2, MaxArgs = 2, Flags= FunctionFlags.Regular)]
		public static CallState remit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "ZEMIT", MinArgs = 2, MaxArgs = 2, Flags= FunctionFlags.Regular)]
		public static CallState zemit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "CBUFFER", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState cinfo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "CDESC", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState cdesc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "CMSGS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState cmsg(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		[SharpFunction(Name = "CUSERS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState cusers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
	}
}
