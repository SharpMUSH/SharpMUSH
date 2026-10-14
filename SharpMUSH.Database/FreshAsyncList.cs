using SharpMUSH.Library.Models;

namespace SharpMUSH.Database;

/// <summary>
/// A <see cref="FreshAsyncEnumerable{T}"/> over a read that completes synchronously: every enumeration,
/// and every <see cref="ReadNow"/>, reads afresh.
/// </summary>
public sealed class FreshAsyncList<T>(Func<IReadOnlyList<T>> read) : IAsyncEnumerable<T>, IReadableNow<T>
{
	public IReadOnlyList<T> ReadNow() => read();

	public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
		=> Enumerate(cancellationToken).GetAsyncEnumerator(cancellationToken);

	private async IAsyncEnumerable<T> Enumerate([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
	{
		await ValueTask.CompletedTask;
		foreach (var item in read())
		{
			ct.ThrowIfCancellationRequested();
			yield return item;
		}
	}
}
