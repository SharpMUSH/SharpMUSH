using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions
{
	public partial class Functions
	{
		[SharpFunction(Name = "CTIME", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState CTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ISDAYLIGHT", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState IsDaylight(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "MTIME", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState MTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SECS", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
		public static CallState Secs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SECSCALC", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState SecsCalc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "STARTTIME", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
		public static CallState StartTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "STRINGSECS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState StringSecs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TIME", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Time(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "TIMECALC", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState TimeCalc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "TIMEFMT", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState TimeFmt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "TIMESTRING", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState TimeString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
		
		[SharpFunction(Name = "UPTIME", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.StripAnsi)]
		public static CallState Uptime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "UTCTIME", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
		public static CallState UTCTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "CSECS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState CSecs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "MSECS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState MSecs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ETIME", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState ETime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ETIMEFMT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState ETimeFmt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
	}
}
