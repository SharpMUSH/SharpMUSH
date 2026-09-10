using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	// Explicitly scoped to one list-function invocation, including its lambda helper.
	// Failed predicates still follow the existing text/truthiness and short-circuit rules.
	private sealed class ListEvaluationErrors
	{
		private bool _hadErrors;
		public MString Record(CallState? result)
		{
			_hadErrors |= result?.HadErrors == true;
			return result?.Message ?? MarkupText.Empty;
		}
		public async ValueTask<MString> DefaultArgumentAsync(IMUSHCodeParser parser, int index, MString fallback)
		{
			var arguments = parser.CurrentState.Arguments;
			if (arguments.Count - 1 < index || arguments[index.ToString()].Message!.Length == 0) return fallback;
			return Record(await arguments[index.ToString()].GetParsedResultAsync());
		}
		public CallState Complete(CallState result) => _hadErrors ? result with { HadErrors = true } : result;
	}
}
