using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Shared;

/// <summary>
/// A settable <see cref="IAccountAuthState"/>. It is "hydrated" from construction, so
/// <see cref="InitAsync"/> does nothing.
/// </summary>
public sealed class FakeAccountAuthState : IAccountAuthState
{
	private bool? _isLoggedIn;

	/// <summary>Follows <see cref="AccountSessionToken"/> unless a test sets it outright.</summary>
	public bool IsLoggedIn
	{
		get => _isLoggedIn ?? AccountSessionToken is not null;
		set => _isLoggedIn = value;
	}

	public string? AccountSessionToken { get; set; }
	public string? Username { get; set; }
	public string? Role { get; set; }
	public IReadOnlyList<string> Permissions { get; set; } = [];
	public bool ExplicitlyLoggedOut { get; set; }
	public AccountAuthService.CharacterSummary? ActiveCharacter { get; set; }

	public event Action? AuthStateChanged;
	public event Action? ActiveCharacterChanged;

	/// <summary>What <see cref="GetDebugOttAsync"/> answers.</summary>
	public AccountAuthService.DebugOttResponse? NextDebugOtt { get; set; }

	/// <summary>How many times something actually called through for a debug OTT.</summary>
	public int DebugOttCallCount { get; private set; }

	public Task InitAsync() => Task.CompletedTask;

	/// <summary>What <see cref="RenewSessionAsync"/> answers.</summary>
	public string? RenewedToken { get; set; }

	public Task<string?> RenewSessionAsync(string rejectedToken) => Task.FromResult(RenewedToken);

	public Task<AccountAuthService.DebugOttResponse?> GetDebugOttAsync()
	{
		DebugOttCallCount++;
		return Task.FromResult(NextDebugOtt);
	}

	/// <summary>Raises both change events, as the real state does after a login or a character switch.</summary>
	public void Fire()
	{
		AuthStateChanged?.Invoke();
		ActiveCharacterChanged?.Invoke();
	}
}
