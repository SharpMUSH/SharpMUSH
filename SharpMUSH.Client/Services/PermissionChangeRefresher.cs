namespace SharpMUSH.Client.Services;

/// <summary>
/// Reads this tab's role and grants again when the server says they may have changed
/// (<see cref="ConnectionStateService.OnPermissionsChanged"/>), so a character just approved, made a wizard or
/// stripped of a power sees the portal follow without a page reload. Notices that arrive while a read is out
/// are folded into one more read after it, so a burst of changes costs at most two requests.
/// </summary>
/// <remarks>
/// The notice rides the game hub, which pages otherwise open only when a character connects through them, so
/// this opens it whenever the tab holds an account session (opening it again is a no-op).
/// </remarks>
public sealed class PermissionChangeRefresher : IDisposable
{
	private readonly ConnectionStateService _connection;
	private readonly Func<Task> _reload;
	private readonly ILogger<PermissionChangeRefresher> _logger;
	private readonly Lock _gate = new();
	private Task? _running;
	private bool _again;

	public PermissionChangeRefresher(ConnectionStateService connection, AccountAuthService auth,
		ILogger<PermissionChangeRefresher> logger)
		: this(connection, auth, auth.ReloadAuthorityAsync, logger)
	{
	}

	/// <param name="auth">The tab's session; the hub is opened while it is signed in.</param>
	/// <param name="reload">Reads the tab's authority again; <see cref="AccountAuthService.ReloadAuthorityAsync"/>.</param>
	public PermissionChangeRefresher(ConnectionStateService connection, IAccountAuthState auth, Func<Task> reload,
		ILogger<PermissionChangeRefresher> logger)
	{
		_connection = connection;
		_auth = auth;
		_reload = reload;
		_logger = logger;
		_connection.OnPermissionsChanged += Changed;
		_auth.AuthStateChanged += SessionChanged;
		SessionChanged();
	}

	private readonly IAccountAuthState _auth;

	private void SessionChanged()
	{
		if (_auth.IsLoggedIn) _ = _connection.ConnectAsync();
	}

	/// <summary>Completes once every read asked for so far has finished.</summary>
	public Task Idle
	{
		get
		{
			lock (_gate) return _running ?? Task.CompletedTask;
		}
	}

	private void Changed()
	{
		lock (_gate)
		{
			if (_running is not null)
			{
				_again = true;
				return;
			}

			_running = RunAsync();
		}
	}

	private async Task RunAsync()
	{
		// Leave Changed before the first read, so _running is set before this can clear it.
		await Task.Yield();
		while (true)
		{
			try
			{
				await _reload();
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Could not reload permissions after the server said they changed");
			}

			lock (_gate)
			{
				if (!_again)
				{
					_running = null;
					return;
				}

				_again = false;
			}
		}
	}

	public void Dispose()
	{
		_connection.OnPermissionsChanged -= Changed;
		_auth.AuthStateChanged -= SessionChanged;
	}
}
