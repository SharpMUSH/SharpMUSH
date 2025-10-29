using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "sql", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> SQL(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "sqlescape", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> SQLEscape(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "mapsql", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> MapSQL(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}