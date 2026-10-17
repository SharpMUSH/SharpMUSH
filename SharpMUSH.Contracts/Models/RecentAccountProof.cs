namespace SharpMUSH.Library.Models;

/// <summary>The one account-security operation a fresh proof authorizes.</summary>
public enum AccountSecurityOperation
{
	LinkIdentity, UnlinkIdentity, AccountPassword, Email, EnrollPasskey, RemovePasskey,
	CharacterPassword, NativeLogin, LegacyFallback, StaffRecovery
}

/// <summary>A short-lived bearer grant returned once to the initiating browser.</summary>
public sealed record RecentAccountProof(string Grant, DateTimeOffset ExpiresAt);
