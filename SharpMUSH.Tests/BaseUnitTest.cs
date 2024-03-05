using Definitions = AntlrCSharp.Implementation.Definitions;
using Serilog;
using System.Collections.Immutable;

namespace AntlrCSharp.Tests
{
	public class BaseUnitTest
	{
		public BaseUnitTest()
		{
			Log.Logger = new LoggerConfiguration()
												.WriteTo.Console()
												.MinimumLevel.Debug()
												.CreateLogger();
		}

		public static Implementation.Parser TestParser() =>
			new(state: new Implementation.Parser.ParserState(
					Registers: ImmutableDictionary<string, MarkupString.MarkupStringModule.MarkupString>.Empty,
					CurrentEvaluation: null,
					Function: null,
					Command: "think",
					Arguments: [],
					Executor: new Definitions.DBRef(1),
					Enactor: new Definitions.DBRef(1),
					Caller: new Definitions.DBRef(1)
				));
	}
}
