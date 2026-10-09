using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit;

/// <summary>
/// A <see cref="BunitContext"/> that owns the disposables a test builds by hand.
/// <para>
/// Tests here fake the server by handing components an <see cref="HttpClient"/> over a stub
/// <see cref="HttpMessageHandler"/>. The bUnit service provider disposes what it creates, but not
/// objects a test constructs itself and registers as instances, so those clients were left
/// undisposed — a real finding even though the handlers are in-memory and hold no sockets.
/// </para>
/// <para>
/// Wrap such an object in <see cref="Track{T}"/> and the context disposes it, in reverse order of
/// creation, when the test ends. Disposing an <see cref="HttpClient"/> also disposes its handler,
/// so tracking the client is enough.
/// </para>
/// </summary>
public abstract class TrackingBunitContext : BunitContext
{
	private readonly DisposableTracker _tracker = new();

	/// <summary>
	/// Every component that shows the acting character's data reads it from <see cref="IAccountAuthState"/>,
	/// which the app forwards to <see cref="AccountAuthService"/>. A test that registers an
	/// <see cref="AccountAuthService"/> gets that one, as the app would; a test that registers neither gets
	/// a session nobody is signed in to; a test that registers its own <see cref="IAccountAuthState"/>
	/// overrides this.
	/// <para>
	/// Anything that links to an optional application asks <see cref="ServerInfoService"/> whether the game
	/// has it. Unless a test registers its own, the game is a new one: guests allowed, the default name, and
	/// the applications a first boot installs.
	/// </para>
	/// <para>
	/// An avatar finds a character's picture in the <see cref="CharacterDirectoryService"/> a test registers;
	/// without one, nobody has a picture.
	/// </para>
	/// </summary>
	protected TrackingBunitContext()
	{
		Services.TryAddSingleton<IAccountAuthState>(sp =>
			sp.GetService<AccountAuthService>() ?? Substitute.For<IAccountAuthState>());
		Services.TryAddSingleton<ServerInfoService>(new StubServerInfoService(guestsEnabled: true));
		Services.TryAddSingleton<CommandHistory>();
		Services.TryAddSingleton<ScreenReaderMode>();
		Services.TryAddSingleton<ICharacterPictures>(sp =>
			sp.GetService<CharacterDirectoryService>() ?? (ICharacterPictures)new NoCharacterPictures());
	}

	/// <summary>
	/// Takes ownership of <paramref name="disposable"/> and returns it unchanged. Public so a shared
	/// wiring helper can hand ownership to the context it is setting up.
	/// </summary>
	public T Track<T>(T disposable) where T : IDisposable => _tracker.Track(disposable);

	/// <summary>
	/// The teardown path that actually runs. bUnit's <see cref="BunitContext.DisposeAsync"/> calls
	/// <see cref="DisposeAsyncCore"/> and then <c>Dispose(disposing: false)</c>, and TUnit disposes an
	/// <see cref="IAsyncDisposable"/> test class asynchronously — so releasing only under
	/// <c>disposing: true</c> releases nothing at all. Both overrides are here because either entry
	/// point may be the one used; <see cref="DisposableTracker"/> is idempotent.
	/// </summary>
	protected override async ValueTask DisposeAsyncCore()
	{
		try
		{
			_tracker.Dispose();
		}
		finally
		{
			await base.DisposeAsyncCore();
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_tracker.Dispose();
		}

		base.Dispose(disposing);
	}
}

/// <summary>
/// The same ownership for a plain test class that is not a bUnit context — one that builds a service
/// over a stub handler and exercises it directly. TUnit disposes a test class that implements
/// <see cref="IDisposable"/>, so deriving from this is enough.
/// </summary>
public abstract class TrackingTestContext : IDisposable
{
	private readonly DisposableTracker _tracker = new();

	/// <inheritdoc cref="TrackingBunitContext.Track{T}"/>
	public T Track<T>(T disposable) where T : IDisposable => _tracker.Track(disposable);

	public void Dispose()
	{
		_tracker.Dispose();
		GC.SuppressFinalize(this);
	}
}

/// <summary>Holds disposables and releases them in reverse order of registration.</summary>
public sealed class DisposableTracker : IDisposable
{
	private readonly List<IDisposable> _owned = [];

	public T Track<T>(T disposable) where T : IDisposable
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
	}
}
