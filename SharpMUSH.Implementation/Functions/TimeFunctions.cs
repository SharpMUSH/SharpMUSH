using SharpMUSH.Implementation.Definitions;

namespace SharpMUSH.Implementation.Functions
{
	public partial class Functions
	{
		[SharpFunction(Name = "CTIME", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState CTime(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ISDAYLIGHT", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState IsDaylight(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "MTIME", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState MTime(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SECS", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
		public static CallState Secs(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SECSCALC", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState SecsCalc(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "STARTTIME", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
		public static CallState StartTime(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "STRINGSECS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState StringSecs(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TIME", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Time(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "TIMECALC", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState TimeCalc(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "TIMEFMT", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState TimeFmt(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "TIMESTRING", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState TimeString(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "UPTIME", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.StripAnsi)]
		public static CallState Uptime(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "UTCTIME", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
		public static CallState UTCTime(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "CSECS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState CSecs(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "MSECS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState MSecs(Parser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
	}
}
