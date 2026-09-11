namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// The exceptions a union member throws when it is asked for a case the value does not hold, worded
/// once so every union reports a mismatch the same way.
/// </summary>
internal static class UnionCase
{
	public static InvalidOperationException Mismatch<TExpected>(object? value)
		=> new($"Expected {typeof(TExpected).Name}, but the value is {Describe(value)}.");

	public static InvalidOperationException Unexpected(object? value)
		=> new($"Unexpected union case {Describe(value)}.");

	private static string Describe(object? value) => value?.GetType().Name ?? "null";
}
