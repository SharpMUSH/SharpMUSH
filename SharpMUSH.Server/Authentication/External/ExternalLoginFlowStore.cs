using System.Buffers.Text;
using System.Security.Cryptography;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Authentication.External;

public enum ExternalFlowPurpose { Login, Link, Reauthenticate }
public sealed record ExternalFlowStart(string TransactionId, string BrowserSecret);
public sealed record ExternalFlowTransaction(string Id, string Provider, string Generation, string BrowserDigest,
	string ReturnPath, bool Remember, ExternalFlowPurpose Purpose, string? AccountId, string? SessionDigest,
	long CredentialEpoch, AccountSecurityOperation? Operation, ExternalIdentityKey? ExpectedIdentity, string? AuthenticatedIdentityId = null, AccountProofAuthority? SourceAuthority = null);
public sealed record ExternalFlowHandoff(ExternalFlowTransaction Transaction, ExternalIdentityKey Key, string? Label);

/// <summary>Bounded, browser-bound one-use server transactions; no provider tokens are retained.</summary>
public sealed class ExternalLoginFlowStore(TimeProvider time, int capacity = 5_000) : IAccountCredentialRevocation
{
	public static readonly TimeSpan TransactionLifetime = TimeSpan.FromMinutes(5);
	public static readonly TimeSpan HandoffLifetime = TimeSpan.FromSeconds(60);
	public static readonly TimeSpan SignupLifetime = TimeSpan.FromMinutes(5);
	private sealed record Held(object Value, DateTimeOffset Expires);
	private readonly Lock _sync = new();
	private readonly Dictionary<string, Held> _held = new(StringComparer.Ordinal);

	public Result<ExternalFlowStart> Begin(string provider, string generation, string returnPath, bool remember,
		ExternalFlowPurpose purpose, string? accountId = null, string? sessionDigest = null, long credentialEpoch = 0,
		AccountSecurityOperation? operation = null, ExternalIdentityKey? expectedIdentity = null, AccountProofAuthority? sourceAuthority = null)
	{
		if (!ExternalProviderDefinition.IsKnown(provider) || !AllowedReturnPath(returnPath))
			return new Error<string>("Invalid sign-in provider or return route.");
		lock (_sync)
		{
			Sweep();
			if (_held.Count >= capacity) return Busy();
			var id = Random();
			var browser = Random();
			_held.Add("t:" + id, new Held(new ExternalFlowTransaction(id, provider, generation, AccountSessionDigest.Compute(browser),
				returnPath, remember, purpose, accountId, sessionDigest, credentialEpoch, operation, expectedIdentity, SourceAuthority: sourceAuthority), time.GetUtcNow() + TransactionLifetime));
			return new ExternalFlowStart(id, browser);
		}
	}

	public Found<ExternalFlowTransaction> ReadRemote(string? id, string? browserSecret) => Remote(id, browserSecret, false);

	public Found<ExternalFlowTransaction> TakeRemote(string? id, string? browserSecret) => Remote(id, browserSecret, true);

	private Found<ExternalFlowTransaction> Remote(string? id, string? browserSecret, bool consume)
	{
		if (string.IsNullOrEmpty(id) || id.Length > 128 || string.IsNullOrEmpty(browserSecret)) return new NotFound();
		lock (_sync)
		{
			Sweep();
			if (!_held.TryGetValue("t:" + id, out var held) || held.Value is not ExternalFlowTransaction transaction
				|| transaction.BrowserDigest != AccountSessionDigest.Compute(browserSecret)) return new NotFound();
			if (consume) _held.Remove("t:" + id);
			return transaction;
		}
	}

	public Result<string> HandOff(ExternalFlowTransaction transaction, ExternalIdentityKey key, string? label)
		=> Hold("h:", new ExternalFlowHandoff(transaction, key, label), HandoffLifetime);

	public Found<ExternalFlowHandoff> TakeHandoff(string? cookie, Func<string, string?> browserCookie)
		=> ReadHandoff("h:", cookie, browserCookie, consume: true);

	public Result<string> BeginSignup(ExternalFlowHandoff handoff) => Hold("s:", handoff, SignupLifetime);
	public Found<ExternalFlowHandoff> ReadSignup(string? cookie, Func<string, string?> browserCookie)
		=> ReadHandoff("s:", cookie, browserCookie, consume: false);

	public void FinishSignup(string cookie)
	{
		lock (_sync) _held.Remove("s:" + AccountSessionDigest.Compute(cookie));
	}

	private Result<string> Hold(string kind, ExternalFlowHandoff value, TimeSpan lifetime)
	{
		lock (_sync)
		{
			Sweep();
			if (_held.Count >= capacity) return Busy();
			var cookie = Random();
			_held.Add(kind + AccountSessionDigest.Compute(cookie), new Held(value, time.GetUtcNow() + lifetime));
			return cookie;
		}
	}

	private Found<ExternalFlowHandoff> ReadHandoff(string kind, string? cookie, Func<string, string?> browserCookie, bool consume)
	{
		if (string.IsNullOrEmpty(cookie) || cookie.Length > 128) return new NotFound();
		lock (_sync)
		{
			Sweep();
			var key = kind + AccountSessionDigest.Compute(cookie);
			if (!_held.TryGetValue(key, out var held) || held.Value is not ExternalFlowHandoff handoff
				|| browserCookie(handoff.Transaction.Id) is not { } browser
				|| AccountSessionDigest.Compute(browser) != handoff.Transaction.BrowserDigest) return new NotFound();
			if (consume) _held.Remove(key);
			return handoff;
		}
	}

	public void RevokeAccount(string accountId) => Revoke(transaction => transaction.AccountId == accountId);
	public void RevokeSession(string sessionToken)
	{
		var digest = AccountSessionDigest.Compute(sessionToken);
		Revoke(transaction => transaction.SessionDigest == digest);
	}
	public void RevokeCharacter(DBRef character) { }

	private void Revoke(Func<ExternalFlowTransaction, bool> matches)
	{
		lock (_sync)
			foreach (var key in _held.Where(pair => matches(pair.Value.Value switch
			{
				ExternalFlowTransaction transaction => transaction,
				ExternalFlowHandoff handoff => handoff.Transaction,
				_ => throw new InvalidOperationException("Unexpected external-flow state.")
			})).Select(pair => pair.Key).ToArray()) _held.Remove(key);
	}

	private void Sweep()
	{
		var now = time.GetUtcNow();
		foreach (var key in _held.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToArray()) _held.Remove(key);
	}

	public static bool AllowedReturnPath(string? path)
	{
		if (string.IsNullOrEmpty(path) || path.Length > 2048 || path.Any(char.IsControl) || path.Contains('\\') || path.Contains('?') || path.Contains('#')) return false;
		string decoded;
		try { decoded = Uri.UnescapeDataString(path); }
		catch (UriFormatException) { return false; }
		if (!decoded.StartsWith('/') || decoded.StartsWith("//", StringComparison.Ordinal) || decoded.Contains('\\') || decoded.Any(char.IsControl) || decoded.Split('/').Any(segment => segment is "." or "..")) return false;
		return decoded == "/" || new[] { "/account", "/characters", "/play", "/scenes", "/wiki", "/admin", "/apps" }
			.Any(prefix => decoded == prefix || decoded.StartsWith(prefix + "/", StringComparison.Ordinal));
	}

	private static string Random() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
	private static Error<string> Busy() => new("The server is handling too many sign-in requests. Try again shortly.");
}
