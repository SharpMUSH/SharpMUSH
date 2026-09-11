using System.Diagnostics.CodeAnalysis;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A value, or the message explaining why there is none. An operation with nothing to return on
/// success is a <c>Result&lt;Success&gt;</c>.
/// </summary>
public partial union Result<T>(T, Error<string>)
{
	/// <summary>
	/// True with the value, or false with the message: a caller can pass the failure straight back and go on
	/// with the value, with no cast in between.
	/// </summary>
	public bool TryGetValue([MaybeNullWhen(false)] out T value, out Error<string> error)
	{
		switch (Value)
		{
			case T success:
				value = success;
				error = default;
				return true;
			case Error<string> held:
				value = default;
				error = held;
				return false;
			default:
				throw new InvalidOperationException($"A default {nameof(Result<T>)} holds neither case.");
		}
	}
}
