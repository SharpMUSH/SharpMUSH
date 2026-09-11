using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// A value a controller action goes on to use, or the response it should return straight away instead.
/// </summary>
public partial union ValueOrResponse<T>(T, ActionResult)
{
	/// <summary>
	/// True with the value, or false with the response: a caller can pass the failure straight back and go on
	/// with the value, with no cast in between.
	/// </summary>
	public bool TryGetValue([MaybeNullWhen(false)] out T value, [MaybeNullWhen(true)] out ActionResult response)
	{
		switch (Value)
		{
			case T success:
				value = success;
				response = default;
				return true;
			case ActionResult held:
				value = default;
				response = held;
				return false;
			default:
				throw new InvalidOperationException($"A default {nameof(ValueOrResponse<T>)} holds neither case.");
		}
	}
}
