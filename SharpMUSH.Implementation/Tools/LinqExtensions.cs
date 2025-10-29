namespace SharpMUSH.Implementation.Tools;

internal static class LinqExtensions
{
	public static IEnumerable<(T?, T)> Pairwise<T>(this IEnumerable<T> source)
	{
		var previous = default(T);
		using var it = source.GetEnumerator();

		if (it.MoveNext())
			previous = it.Current;

		while (it.MoveNext())
			yield return (previous, previous = it.Current);
	}
	
	public static async IAsyncEnumerable<(T?, T)> Pairwise<T>(this IAsyncEnumerable<T> source)
	{
		var previous = default(T);
		await using var it = source.GetAsyncEnumerator();

		if (await it.MoveNextAsync())
			previous = it.Current;

		while (await it.MoveNextAsync())
			yield return (previous, previous = it.Current);
	}
}