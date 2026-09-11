using System.Diagnostics.CodeAnalysis;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The body an API call returned, or the <see cref="ApiFailure"/> that stopped it.
/// </summary>
public partial union ApiResult<T>(T, ApiFailure)
{
	/// <summary>
	/// True with the value, or false with the failure: a caller can pass the failure straight back and go on
	/// with the value, with no cast in between.
	/// </summary>
	public bool TryGetValue([MaybeNullWhen(false)] out T value, [MaybeNullWhen(true)] out ApiFailure failure)
	{
		switch (Value)
		{
			case T success:
				value = success;
				failure = default;
				return true;
			case ApiFailure held:
				value = default;
				failure = held;
				return false;
			default:
				throw new InvalidOperationException($"A default {nameof(ApiResult<T>)} holds neither case.");
		}
	}
}
