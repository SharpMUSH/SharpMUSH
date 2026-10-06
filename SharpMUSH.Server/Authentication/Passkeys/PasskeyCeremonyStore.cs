using System.Collections.Concurrent;
using System.Security.Cryptography;
using Fido2NetLib;

namespace SharpMUSH.Server.Authentication.Passkeys;

/// <summary>
/// The challenge a passkey ceremony was started with, held between the options the browser gets and
/// the answer it sends back. Each one is good for one answer and a few minutes.
/// </summary>
/// <remarks>
/// Kept in memory: a ceremony lasts as long as the browser's prompt, and one cut short by a restart is
/// started again by pressing the button again. Taking a ceremony removes it in the same step, so two
/// answers racing for one challenge cannot both be checked against it.
/// </remarks>
public sealed class PasskeyCeremonyStore(TimeProvider time)
{
	/// <summary>How long a ceremony waits for its answer; the browser's own prompt times out sooner.</summary>
	public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

	/// <summary>How many unanswered ceremonies are held at once. The endpoints that start them are rate
	/// limited per address; this caps what many addresses together can make the server keep.</summary>
	public const int Capacity = 10_000;

	/// <summary>A registration started for <paramref name="AccountId"/>.</summary>
	public sealed record Registration(string AccountId, Fido2Configuration RelyingParty, CredentialCreateOptions Options);

	/// <summary>A sign-in, which names no account until the passkey answers.</summary>
	public sealed record SignIn(Fido2Configuration RelyingParty, AssertionOptions Options);

	private readonly ConcurrentDictionary<string, (object Ceremony, DateTimeOffset Expires)> _ceremonies = new(StringComparer.Ordinal);

	/// <summary>Holds <paramref name="ceremony"/> and returns the id the browser sends back with its answer;
	/// null when the store is full.</summary>
	public string? Begin(object ceremony)
	{
		var now = time.GetUtcNow();
		foreach (var (id, held) in _ceremonies)
			if (held.Expires <= now)
				_ceremonies.TryRemove(id, out _);

		if (_ceremonies.Count >= Capacity) return null;

		var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
		_ceremonies[key] = (ceremony, now + Lifetime);
		return key;
	}

	/// <summary>The ceremony under <paramref name="id"/>, removed so it cannot be answered twice; null when it
	/// is unknown, already answered, expired, or of another kind.</summary>
	public T? Take<T>(string? id) where T : class
		=> !string.IsNullOrEmpty(id)
			&& _ceremonies.TryRemove(id, out var held)
			&& held.Expires > time.GetUtcNow()
				? held.Ceremony as T
				: null;
}
