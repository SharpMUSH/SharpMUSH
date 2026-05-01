namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A simple Option type: either a value of T or None.
/// Replaces the OneOfBase&lt;T, None&gt; class.
/// </summary>
public union Option<T>(T, None)
{
	public bool IsSome() => Value is T;
	public bool IsNone() => Value is null or None;

	public T AsValue() => (T)Value!;

	public static Option<T> FromOption(T some) => some;

	public bool TryGetValue(out T? value)
	{
		if (Value is T t) { value = t; return true; }
		value = default;
		return false;
	}

	// Backward-compat aliases
	public T    AsT0 => AsValue();
	public bool IsT0 => IsSome();
	public bool IsT1 => IsNone();
}
