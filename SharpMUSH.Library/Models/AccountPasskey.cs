namespace SharpMUSH.Library.Models;

/// <summary>
/// A WebAuthn credential (a passkey) that signs in to an account in place of its password.
/// </summary>
/// <param name="AccountId">The account the passkey signs in to.</param>
/// <param name="CredentialId">The authenticator's id for the credential, as it sends it back at sign-in.</param>
/// <param name="PublicKey">The credential's COSE-encoded public key; sign-in assertions are checked against it.</param>
/// <param name="SignCount">The authenticator's signature counter as last seen. Synced passkeys keep it at 0.</param>
/// <param name="Name">What the holder calls it ("Phone", "YubiKey").</param>
/// <param name="Transports">How the browser reached the authenticator (<c>internal</c>, <c>usb</c>, <c>hybrid</c>, ...).</param>
/// <param name="IsBackedUp">Whether the authenticator last said the credential is synced to other devices.</param>
/// <param name="CreatedAt">When it was registered.</param>
/// <param name="LastUsedAt">When it last signed in, or null if it never has.</param>
public sealed record AccountPasskey(
	string AccountId,
	byte[] CredentialId,
	byte[] PublicKey,
	uint SignCount,
	string Name,
	IReadOnlyList<string> Transports,
	bool IsBackedUp,
	DateTimeOffset CreatedAt,
	DateTimeOffset? LastUsedAt);

/// <summary>What <see cref="IAccountStore.AddAccountPasskeyAsync"/> did with a new passkey.</summary>
public enum PasskeyAddOutcome
{
	/// <summary>Stored.</summary>
	Added,

	/// <summary>Not stored: a passkey with the same credential id is already registered, to this account or another.</summary>
	AlreadyRegistered,

	/// <summary>Not stored: the account already holds as many passkeys as it may.</summary>
	AccountFull
}
