using System.Diagnostics.CodeAnalysis;
using SharpMUSH.Library.Models.Diagnostics;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A queue-diagnostics answer, or why the actor could not have it.
/// </summary>
public partial union DiagnosticsResult<T>(T, DiagnosticsError)
{
	/// <summary>
	/// True with the value, or false with the error: a caller can pass the failure straight back and go on
	/// with the value, with no cast in between.
	/// </summary>
	public bool TryGetValue([MaybeNullWhen(false)] out T value, out DiagnosticsError error)
	{
		switch (Value)
		{
			case T success:
				value = success;
				error = default;
				return true;
			case DiagnosticsError held:
				value = default;
				error = held;
				return false;
			default:
				throw new InvalidOperationException($"A default {nameof(DiagnosticsResult<T>)} holds neither case.");
		}
	}
}
