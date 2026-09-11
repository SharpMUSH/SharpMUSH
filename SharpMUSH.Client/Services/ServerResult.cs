using System.Diagnostics.CodeAnalysis;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// What the server answered, or <see cref="Error"/> when it could not be asked. Callers that can say
/// why a call failed use <see cref="ApiResult{T}"/> instead.
/// </summary>
public partial union ServerResult<T>(T, Error)
{
	/// <summary>
	/// True with the value, or false with the error: a caller can pass the failure straight back and go on
	/// with the value, with no cast in between.
	/// </summary>
	public bool TryGetValue([MaybeNullWhen(false)] out T value, out Error error)
	{
		switch (Value)
		{
			case T success:
				value = success;
				error = default;
				return true;
			case Error held:
				value = default;
				error = held;
				return false;
			default:
				throw new InvalidOperationException($"A default {nameof(ServerResult<T>)} holds neither case.");
		}
	}
}
