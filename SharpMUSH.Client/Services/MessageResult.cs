using System.Diagnostics.CodeAnalysis;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The result of a call, or the message to show the user because it failed.
/// </summary>
public partial union MessageResult<T>(T, string)
{
	/// <summary>
	/// True with the value, or false with the message: a caller can pass the failure straight back and go on
	/// with the value, with no cast in between.
	/// </summary>
	public bool TryGetValue([MaybeNullWhen(false)] out T value, [MaybeNullWhen(true)] out string message)
	{
		switch (Value)
		{
			case T success:
				value = success;
				message = default;
				return true;
			case string held:
				value = default;
				message = held;
				return false;
			default:
				throw new InvalidOperationException($"A default {nameof(MessageResult<T>)} holds neither case.");
		}
	}
}
