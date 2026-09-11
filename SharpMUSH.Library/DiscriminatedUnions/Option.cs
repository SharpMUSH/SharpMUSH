using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A value of <typeparamref name="T"/>, or <see cref="None"/>.
/// </summary>
/// <remarks>
/// A class rather than a <c>union</c> struct: it is part of the plugin contract and is handed around as
/// <c>Option&lt;T&gt;?</c>, which for a struct would mean <see cref="Nullable{T}"/> and a different
/// <c>Value</c>.
/// </remarks>
[Union]
public sealed class Option<T> : IUnion
{
	public Option(T value) => Value = value;
	public Option(None value) => Value = value;

	public object? Value { get; }

	public override bool Equals(object? obj) => obj is Option<T> other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsSome() => Value is T;
	public bool IsNone() => Value is None;
	public T AsValue() => Value is T value ? value : throw UnionCase.Mismatch<T>(Value);

	public static Option<T> FromOption(T some) => new(some);

	public bool TryGetValue([MaybeNullWhen(false)] out T value)
	{
		if (Value is T some)
		{
			value = some;
			return true;
		}

		value = default;
		return false;
	}
}
