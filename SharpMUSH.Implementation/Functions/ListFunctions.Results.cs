using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	// Explicitly scoped to one list-function invocation, including its lambda helper.
	// Failed predicates still follow the existing text/truthiness and short-circuit rules.
	private sealed class ListEvaluationErrors
	{
		private int _hadErrors;
		public MString Record(CallState? result)
		{
			// SortBy records comparator results concurrently; failure is monotonic.
			if (result?.HadErrors == true) Interlocked.Exchange(ref _hadErrors, 1);
			return result?.Message ?? MarkupText.Empty;
		}
		public async ValueTask<MString> DefaultArgumentAsync(IMUSHCodeParser parser, int index, MString fallback)
		{
			var arguments = parser.CurrentState.Arguments;
			if (arguments.Count - 1 < index || arguments[index.ToString()].Message.Length == 0) return fallback;
			return Record(await arguments[index.ToString()].GetParsedResultAsync());
		}
		/// <summary>
		/// An output separator: <paramref name="fallback"/> only when the argument is left out. One given empty
		/// joins the results with nothing, as PennMUSH reads it, unless <paramref name="whenEmpty"/> says what
		/// an empty one stands for.
		/// </summary>
		public async ValueTask<MString> OutputSeparatorAsync(IMUSHCodeParser parser, int index, MString fallback,
			MString? whenEmpty = null)
		{
			var arguments = parser.CurrentState.Arguments;
			if (arguments.Count - 1 < index) return fallback;
			if (whenEmpty is not null && arguments[index.ToString()].Message.Length == 0) return whenEmpty;
			return Record(await arguments[index.ToString()].GetParsedResultAsync());
		}
		public CallState Complete(CallState result) => Volatile.Read(ref _hadErrors) != 0 ? result with { HadErrors = true } : result;
	}
}
