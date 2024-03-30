using SharpMUSH.Implementation.Definitions;

namespace SharpMUSH.Implementation.Functions
{
	public partial class Functions
	{
		[SharpFunction(Name = "SQL", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState SQL(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SQLESCAPE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState SQLEscape(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "MAPSQL", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
		public static CallState MapSQL(MUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
	}
}
