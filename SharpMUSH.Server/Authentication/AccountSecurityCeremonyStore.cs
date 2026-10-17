using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
namespace SharpMUSH.Server.Authentication;

/// <summary>One-use session and epoch bindings for a verified account-security ceremony.</summary>
public sealed class AccountSecurityCeremonyStore(TimeProvider time) : IAccountCredentialRevocation
{
	public sealed record Binding(string AccountId, string SessionDigest, long Epoch, AccountSecurityOperation Operation, AccountProofAuthority? Authority = null);
	private readonly Lock sync = new();
	private readonly Dictionary<string, (Binding Binding, DateTimeOffset Expiry)> held = new(StringComparer.Ordinal);
	public bool Begin(string id, string accountId, string session, long epoch, AccountSecurityOperation operation, AccountProofAuthority? authority = null)
	{
		lock (sync)
		{
			foreach (var key in held.Where(p => p.Value.Expiry <= time.GetUtcNow()).Select(p => p.Key).ToArray()) held.Remove(key);
			if (held.Count >= 5000) return false;
			held[id] = (new(accountId, AccountSessionDigest.Compute(session), epoch, operation, authority), time.GetUtcNow().AddMinutes(5));
			return true;
		}
	}
	public Binding? Take(string? id, string accountId, string session)
	{
		if (id is null) return null;
		lock (sync)
		{
			if (!held.TryGetValue(id, out var entry) || entry.Expiry <= time.GetUtcNow() || entry.Binding.AccountId != accountId
			 || entry.Binding.SessionDigest != AccountSessionDigest.Compute(session)) return null;
			held.Remove(id); return entry.Binding;
		}
	}
	public void RevokeAccount(string accountId) => Revoke(b => b.AccountId == accountId);
	public void RevokeSession(string token) => Revoke(b => b.SessionDigest == AccountSessionDigest.Compute(token));
	public void RevokeCharacter(DBRef character) { }
	private void Revoke(Func<Binding, bool> match)
	{ lock (sync) foreach (var key in held.Where(p => match(p.Value.Binding)).Select(p => p.Key).ToArray()) held.Remove(key); }
}
