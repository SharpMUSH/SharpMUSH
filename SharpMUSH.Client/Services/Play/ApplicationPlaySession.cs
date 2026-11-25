using SharpMUSH.Library.Models.Portal.Play;

namespace SharpMUSH.Client.Services.Play;

/// <summary>
/// The play session one application sees: only the packages it declares. Disposing it, when the application's
/// page goes, ends every subscription made through it, so an application that forgets to unsubscribe leaks
/// nothing.
/// </summary>
public sealed class ApplicationPlaySession : IPlaySession, IDisposable
{
	private readonly PlaySession _session;
	private readonly string _application;
	private readonly PackagePatterns _declared;
	private readonly Func<Func<Task>, Task>? _dispatch;
	private readonly List<IDisposable> _subscriptions = [];
	private readonly Lock _lock = new();
	private bool _disposed;

	internal ApplicationPlaySession(PlaySession session, string application, PackagePatterns declared,
		Func<Func<Task>, Task>? dispatch)
	{
		_session = session;
		_application = application;
		_declared = declared;
		_dispatch = dispatch;
	}

	/// <inheritdoc/>
	public PlayState State => _session.State;

	/// <inheritdoc/>
	public IDisposable Subscribe(PlaySubscription subscription, Func<PlayEvent, Task> handler)
	{
		var inner = _session.Subscribe(subscription, handler, _declared, _dispatch, _application);
		lock (_lock)
		{
			if (_disposed)
			{
				inner.Dispose();
				return inner;
			}

			_subscriptions.Add(inner);
		}

		return inner;
	}

	/// <inheritdoc/>
	public Task ConnectAsync() => _session.ConnectAsync();

	public void Dispose()
	{
		IDisposable[] subscriptions;
		lock (_lock)
		{
			_disposed = true;
			subscriptions = [.. _subscriptions];
			_subscriptions.Clear();
		}

		foreach (var subscription in subscriptions) subscription.Dispose();
	}
}
