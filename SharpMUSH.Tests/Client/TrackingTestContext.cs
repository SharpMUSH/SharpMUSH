namespace SharpMUSH.Tests.Client;

/// <summary>
/// Ownership for disposables a test builds by hand — chiefly the <see cref="HttpClient"/> instances
/// these tests hand to services over a stub handler. A service outlives the helper that built its
/// client, so <c>using</c> at the creation site would dispose too early; the test class owns it
/// instead and releases it when TUnit disposes the class.
/// </summary>
/// <remarks>
/// Deliberately duplicated from the identically-named type in SharpMUSH.Tests.BUnit rather than
/// shared: the only project both reference is SharpMUSH.Tests.Infrastructure, and pulling that
/// (Testcontainers and friends) into the bUnit project would weigh down a suite that runs in
/// seconds without a container.
/// </remarks>
public abstract class TrackingTestContext : IDisposable
{
	private readonly List<IDisposable> _owned = [];

	/// <summary>Takes ownership of <paramref name="disposable"/> and returns it unchanged.</summary>
	protected T Track<T>(T disposable) where T : IDisposable
	{
		_owned.Add(disposable);
		return disposable;
	}

	public void Dispose()
	{
		// Reverse order: a tracked client may wrap a separately tracked handler.
		for (var i = _owned.Count - 1; i >= 0; i--)
		{
			_owned[i].Dispose();
		}

		_owned.Clear();
		GC.SuppressFinalize(this);
	}
}
