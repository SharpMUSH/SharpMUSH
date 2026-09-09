using System.Collections.Frozen;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>Immutable, evaluation-local operation permissions. Object data access is a separate
/// capability; the initial restricted-expression profile grants none.</summary>
public sealed class EvaluationRestrictions
{
	private static readonly AsyncLocal<EvaluationRestrictions?> Ambient = new();
	private readonly FrozenSet<string> _operations;
	public const string Error = "#-1 RESTRICTED EXPRESSION";
	public static EvaluationRestrictions? Current => Ambient.Value;
	public bool AllowObjectDataAccess => false;

	public EvaluationRestrictions(IEnumerable<string> operations)
		=> _operations = operations.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	public bool Allows(string? operation) => operation is not null && _operations.Contains(operation);

	public IDisposable Enter()
	{
		var previous = Current;
		Ambient.Value = previous is null ? this : new EvaluationRestrictions(_operations.Intersect(previous._operations, StringComparer.OrdinalIgnoreCase));
		return new Scope(previous);
	}

	/// <summary>Checks the resolved core operation, never its mutable alias or display name.</summary>
	public static void Demand(FunctionDefinition definition, EvaluationRestrictions? state = null)
	{
		if ((Current is { } current && !current.Allows(definition.RestrictedOperation))
			|| (state is not null && !state.Allows(definition.RestrictedOperation)))
			throw new RestrictedExpressionException();
	}

	/// <summary>Object and attribute evaluation is independent of operation permission.</summary>
	public static void DemandObjectDataAccess(EvaluationRestrictions? state = null)
	{
		if (Current is { AllowObjectDataAccess: false } || state is { AllowObjectDataAccess: false })
			throw new RestrictedExpressionException();
	}

	public static void DemandSubstitution(string symbol, EvaluationRestrictions? state = null)
	{
		if (Current is null && state is null) return;
		if (symbol.Length == 1 && (char.IsAsciiDigit(symbol[0]) || "bBrRtT%".Contains(symbol[0]))) return;
		throw new RestrictedExpressionException();
	}

	private sealed class Scope(EvaluationRestrictions? previous) : IDisposable
	{
		public void Dispose() => Ambient.Value = previous;
	}
}

/// <summary>A denied expression aborts before evaluating arguments or invoking the operation.</summary>
public sealed class RestrictedExpressionException(string error = EvaluationRestrictions.Error) : Exception(error);
