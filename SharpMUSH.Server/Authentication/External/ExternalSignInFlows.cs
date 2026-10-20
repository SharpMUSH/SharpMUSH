using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace SharpMUSH.Server.Authentication.External;

/// <summary>
/// The sign-ins through other services under way: sent off to the service and waiting for it to send the browser
/// back, or back and waiting for the portal to finish them. Each is good for a few minutes.
/// </summary>
/// <remarks>
/// Kept in memory, as passkey ceremonies are: one cut short by a restart is started again by pressing the button
/// again. Every flow holds the hash of the binding the tab that started it keeps, and only that binding finishes it,
/// so a link to a finished sign-in sent to someone else signs no one in.
/// </remarks>
public sealed class ExternalSignInFlows(TimeProvider time)
{
	/// <summary>How long a flow waits at each step. A service's own sign-in page can take a while: a new account, a code.</summary>
	public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

	/// <summary>How many flows are held at once. Starting one is rate limited per address; this caps all of them together.</summary>
	public const int Capacity = 10_000;

	/// <summary>A flow sent off to the service's sign-in page.</summary>
	/// <param name="LinkAccountId">The account that asked to link the service, or null for a sign-in.</param>
	/// <param name="ReturnOrigin">The portal the browser goes back to.</param>
	public sealed record Pending(string Provider, string Verifier, byte[] BindingHash, string? LinkAccountId,
		string RedirectUri, string ReturnOrigin);

	/// <summary>A flow the service answered: who the person is there, or why it failed.</summary>
	public sealed record Returned(string Provider, byte[] BindingHash, string? LinkAccountId, ExternalIdentity? Identity,
		string? Failure);

	private readonly ConcurrentDictionary<string, (object Flow, DateTimeOffset Expires)> _flows = new(StringComparer.Ordinal);

	/// <summary>The hash a flow keeps of a tab's binding.</summary>
	public static byte[] HashBinding(string binding) => SHA256.HashData(Encoding.UTF8.GetBytes(binding));

	/// <summary>Whether <paramref name="binding"/> is the one the flow was started with.</summary>
	public static bool Binds(byte[] bindingHash, string? binding)
		=> !string.IsNullOrEmpty(binding) && CryptographicOperations.FixedTimeEquals(bindingHash, HashBinding(binding));

	/// <summary>Holds <paramref name="flow"/> and returns its id; null when the store is full.</summary>
	public string? Add(object flow)
	{
		var now = time.GetUtcNow();
		foreach (var (id, held) in _flows)
			if (held.Expires <= now)
				_flows.TryRemove(id, out _);

		if (_flows.Count >= Capacity) return null;

		var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
		_flows[key] = (flow, now + Lifetime);
		return key;
	}

	/// <summary>The flow under <paramref name="id"/>, removed; null when unknown, expired or of another kind.</summary>
	public T? Take<T>(string? id) where T : class
		=> !string.IsNullOrEmpty(id)
			&& _flows.TryRemove(id, out var held)
			&& held.Expires > time.GetUtcNow()
				? held.Flow as T
				: null;

	/// <summary>The flow under <paramref name="id"/>, left in place; null when unknown, expired or of another kind.</summary>
	public T? Peek<T>(string? id) where T : class
		=> !string.IsNullOrEmpty(id)
			&& _flows.TryGetValue(id, out var held)
			&& held.Expires > time.GetUtcNow()
				? held.Flow as T
				: null;

	/// <summary>Drops the flow under <paramref name="id"/>.</summary>
	public void Remove(string id) => _flows.TryRemove(id, out _);
}

/// <summary>Who a service says someone is.</summary>
/// <param name="Subject">The service's unchanging id for them.</param>
/// <param name="Name">The name it gives them.</param>
/// <param name="Email">Their email, only when the service says it is verified.</param>
public sealed record ExternalIdentity(string Subject, string Name, string? Email);
