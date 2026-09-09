using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation;

// Nested calls account for siblings and serialized source still retained by
// their callers. Each lease releases its own accumulator on every exit path.
internal static class RestrictedTextRetention
{
	private static readonly AsyncLocal<Budget?> Active = new();

	internal static Lease? Enter(ParserState state, bool recognizedEntry = false)
	{
		if (!recognizedEntry && Active.Value is null
			&& EvaluationRestrictions.Current is null && state.Restrictions is null) return null;
		var previous = Active.Value;
		var budget = previous ?? new Budget();
		Active.Value = budget;
		return new Lease(budget, previous, state);
	}

	internal sealed class Budget
	{
		private readonly object _gate = new();
		private long _characters;
		public bool TryRetain(long characters)
		{
			lock (_gate)
			{
				if (characters > FunctionLimits.MaxOutputCodeUnits - _characters) return false;
				_characters += characters;
				return true;
			}
		}
		public void Release(long characters) { lock (_gate) _characters -= characters; }
	}

	internal sealed class Lease(Budget budget, Budget? previous, ParserState state) : IDisposable
	{
		private long _characters;
		public void Add(long characters)
		{
			ExecutionBudget.Current?.ThrowIfExceeded();
			if (!budget.TryRetain(characters))
			{
				FunctionLimits.RejectOutput(state);
				throw new RestrictedExpressionException(ErrorMessages.Returns.OutputTooLarge);
			}
			_characters += characters;
		}
		public void Dispose()
		{
			budget.Release(_characters);
			Active.Value = previous;
		}
	}
}
