namespace SharpMUSH.Client.Services;

/// <summary>
/// Re-runs a component's load when what the server shows the viewer changes. Mail is the acting
/// character's mailbox, and a scene list holds only the private scenes that character may see, so a
/// component that reads either once goes stale when the tab switches character — and, for scene
/// lists, when this tab starts a scene (<see cref="SceneService.Changed"/>).
/// </summary>
/// <remarks>
/// <para>A component holds one from construction, calls <see cref="Watch"/> once it has its services,
/// and disposes it with itself.</para>
/// <para>Reads land in any order, and an answer for the previous character arriving after the new
/// one's must not overwrite it. <see cref="LoadAsync{T}"/> applies a read only if no load has started
/// since; a load of several steps takes a ticket with <see cref="Begin"/> and checks
/// <see cref="IsCurrent"/> before each write.</para>
/// </remarks>
public sealed class ViewerScopedReload : IDisposable
{
	private IAccountAuthState? _auth;
	private SceneService? _scenes;
	private Action? _reload;
	private int _generation;

	/// <summary>
	/// Runs <paramref name="reload"/> whenever the acting character changes and, given
	/// <paramref name="scenes"/>, whenever the scene lists change. Typically
	/// <c>() =&gt; _ = InvokeAsync(async () =&gt; { await LoadAsync(); StateHasChanged(); })</c>.
	/// </summary>
	public void Watch(IAccountAuthState auth, Action reload, SceneService? scenes = null)
	{
		Unwatch();
		(_auth, _reload, _scenes) = (auth, reload, scenes);
		_auth.ActiveCharacterChanged += _reload;
		if (_scenes is not null) _scenes.Changed += _reload;
	}

	/// <summary>Starts a load; its writes are wanted only while <see cref="IsCurrent"/> holds for the ticket.</summary>
	public int Begin() => Interlocked.Increment(ref _generation);

	/// <summary>True when no load has started since the one holding <paramref name="ticket"/>.</summary>
	public bool IsCurrent(int ticket) => Volatile.Read(ref _generation) == ticket;

	/// <summary>Reads, then applies the answer unless a newer load started meanwhile.</summary>
	public async Task LoadAsync<T>(Func<Task<T>> read, Action<T> apply)
	{
		var ticket = Begin();
		var value = await read();
		if (IsCurrent(ticket)) apply(value);
	}

	/// <summary>
	/// Stops the reloads and retires every load in flight: a read answering after its component is gone
	/// writes nothing.
	/// </summary>
	public void Dispose()
	{
		Begin();
		Unwatch();
	}

	private void Unwatch()
	{
		if (_auth is not null && _reload is not null) _auth.ActiveCharacterChanged -= _reload;
		if (_scenes is not null && _reload is not null) _scenes.Changed -= _reload;
		(_auth, _reload, _scenes) = (null, null, null);
	}
}
