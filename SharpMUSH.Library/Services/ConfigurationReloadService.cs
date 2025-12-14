using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Service to signal configuration reloads using the proper Microsoft change token pattern.
/// This follows the Microsoft.Extensions.Options best practices for runtime configuration changes.
/// </summary>
public class ConfigurationReloadService : IOptionsChangeTokenSource<SharpMUSHOptions>
{
	private ConfigurationReloadToken _changeToken = new();

	public string? Name => Options.DefaultName;

	public IChangeToken GetChangeToken() => _changeToken;

	/// <summary>
	/// Signals that the configuration has changed and should be reloaded.
	/// This is the proper way to notify IOptionsMonitor consumers of configuration changes.
	/// </summary>
	public void SignalChange()
	{
		var previousToken = Interlocked.Exchange(ref _changeToken, new ConfigurationReloadToken());
		previousToken.SignalChange();
	}

	private class ConfigurationReloadToken : IChangeToken
	{
		private CancellationTokenSource _cts = new();

		public bool HasChanged => _cts.IsCancellationRequested;
		public bool ActiveChangeCallbacks => true;

		public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
		{
			return _cts.Token.Register(callback, state);
		}

		public void SignalChange()
		{
			_cts.Cancel();
		}
	}
}
