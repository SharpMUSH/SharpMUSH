using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Logging;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Server.Authentication.Passkeys;

/// <summary>
/// Registering passkeys to an account and signing in with them (WebAuthn, through Fido2NetLib). Passkeys
/// sit beside the account's password; they replace typing it, not having it.
/// </summary>
/// <remarks>
/// <para>Every passkey is a discoverable credential, so sign-in asks for no name first: the browser
/// offers the passkeys it holds for this site, and the one picked says whose it is. User verification
/// (a fingerprint, face or PIN) is required, so a passkey alone is as strong as a password and a device.</para>
///
/// <para>An account's WebAuthn user handle is its account id. The handle a passkey answers with is
/// checked against the account that registered it.</para>
/// </remarks>
public sealed class PasskeyService(
	IAccountStore accounts,
	PasskeyRelyingParty relyingParty,
	PasskeyCeremonyStore ceremonies,
	TimeProvider time,
	ILogger<PasskeyService> logger)
{
	/// <summary>The longest name a passkey may be given.</summary>
	public const int MaxNameLength = 64;

	/// <summary>How many passkeys one account may hold.</summary>
	public const int MaxPerAccount = 20;

	private const string NotVerified = "That passkey could not be verified.";
	private const string CeremonyGone = "The passkey prompt expired. Try again.";
	private const string Busy = "The server is handling too many passkey requests. Try again shortly.";

	/// <summary>The options a browser passes to <c>navigator.credentials</c>, and the id it answers with.</summary>
	public sealed record Challenge(string CeremonyId, JsonElement Options);

	/// <summary>The id the portal names a passkey by: its credential id, base64url-encoded.</summary>
	public static string IdOf(AccountPasskey passkey) => Base64Url.EncodeToString(passkey.CredentialId);

	/// <summary>The credential id behind <paramref name="id"/>, or null when it is not base64url.</summary>
	public static byte[]? CredentialIdOf(string id)
	{
		try
		{
			return Base64Url.DecodeFromChars(id);
		}
		catch (FormatException)
		{
			return null;
		}
	}

	private static byte[] UserHandle(string accountId) => Encoding.UTF8.GetBytes(accountId);

	/// <summary>Starts registering a new passkey to <paramref name="account"/>.</summary>
	public async ValueTask<Result<Challenge>> BeginRegistrationAsync(SharpAccount account, HttpRequest request,
		CancellationToken ct = default)
	{
		if (relyingParty.For(request) is not Fido2Configuration config)
			return new Error<string>("Passkeys are not available from this address.");

		var existing = await accounts.GetAccountPasskeysAsync(account.Id!, ct);
		if (existing.Count >= MaxPerAccount)
			return new Error<string>($"An account can hold at most {MaxPerAccount} passkeys. Remove one first.");

		var options = new Fido2(config, null).RequestNewCredential(new RequestNewCredentialParams
		{
			User = new Fido2User { Id = UserHandle(account.Id!), Name = account.Username, DisplayName = account.Username },
			ExcludeCredentials = existing.Select(p => new PublicKeyCredentialDescriptor(p.CredentialId)).ToList(),
			AuthenticatorSelection = new AuthenticatorSelection
			{
				ResidentKey = ResidentKeyRequirement.Required,
				UserVerification = UserVerificationRequirement.Required,
			},
			AttestationPreference = AttestationConveyancePreference.None,
			Extensions = new AuthenticationExtensionsClientInputs { CredProps = true },
		});

		return ceremonies.Begin(new PasskeyCeremonyStore.Registration(account.Id!, config, options)) is { } id
			? new Challenge(id, Parse(options.ToJson()))
			: new Error<string>(Busy);
	}

	/// <summary>
	/// Checks the browser's answer to <see cref="BeginRegistrationAsync"/> and stores the passkey under
	/// <paramref name="name"/>. The ceremony must have been started by the same account.
	/// </summary>
	public async ValueTask<Result<AccountPasskey>> CompleteRegistrationAsync(string accountId, string? ceremonyId,
		string? name, JsonElement credential, CancellationToken ct = default)
	{
		if (ceremonies.Take<PasskeyCeremonyStore.Registration>(ceremonyId) is not { } ceremony
			|| ceremony.AccountId != accountId)
			return new Error<string>(CeremonyGone);

		var label = string.IsNullOrWhiteSpace(name) ? "Passkey" : name.Trim();
		if (label.Length > MaxNameLength)
			return new Error<string>($"A passkey's name can be at most {MaxNameLength} characters.");

		if (Deserialize<AuthenticatorAttestationRawResponse>(credential) is not { } attestation)
			return new Error<string>(NotVerified);

		RegisteredPublicKeyCredential registered;
		try
		{
			registered = await new Fido2(ceremony.RelyingParty, null).MakeNewCredentialAsync(new MakeNewCredentialParams
			{
				AttestationResponse = attestation,
				OriginalOptions = ceremony.Options,
				IsCredentialIdUniqueToUserCallback = async (args, token) =>
					await accounts.GetAccountPasskeyAsync(args.CredentialId, token) is null,
			}, ct);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The answer is the browser's to make up: malformed CBOR or keys surface as whatever the
			// parser throws, not only as Fido2VerificationException, and each is a refusal.
			logger.LogInformation("Passkey registration for {AccountId} failed verification: {Message}",
				LogSanitizer.Sanitize(accountId), ex.Message);
			return new Error<string>(NotVerified);
		}

		var passkey = new AccountPasskey(accountId, registered.Id, registered.PublicKey, registered.SignCount, label,
			registered.Transports?.Select(TransportName).ToList() ?? [], registered.IsBackedUp, time.GetUtcNow(), null);

		if (!await accounts.AddAccountPasskeyAsync(passkey, ct))
			return new Error<string>("That passkey is already registered.");

		logger.LogInformation("Account {AccountId} registered a passkey", LogSanitizer.Sanitize(accountId));
		return passkey;
	}

	/// <summary>Starts a passkey sign-in, which names no account: the passkey the visitor picks does.</summary>
	public Result<Challenge> BeginSignIn(HttpRequest request)
	{
		if (relyingParty.For(request) is not Fido2Configuration config)
			return new Error<string>("Passkeys are not available from this address.");

		var options = new Fido2(config, null).GetAssertionOptions(new GetAssertionOptionsParams
		{
			AllowedCredentials = [],
			UserVerification = UserVerificationRequirement.Required,
		});

		return ceremonies.Begin(new PasskeyCeremonyStore.SignIn(config, options)) is { } id
			? new Challenge(id, Parse(options.ToJson()))
			: new Error<string>(Busy);
	}

	/// <summary>
	/// Checks the browser's answer to <see cref="BeginSignIn"/>, and answers with the account whose passkey
	/// signed it. Whether that account may sign in now (it may be disabled or banned) is the caller's to ask.
	/// </summary>
	public async ValueTask<Result<SharpAccount>> CompleteSignInAsync(string? ceremonyId, JsonElement credential,
		CancellationToken ct = default)
	{
		if (ceremonies.Take<PasskeyCeremonyStore.SignIn>(ceremonyId) is not { } ceremony)
			return new Error<string>(CeremonyGone);

		if (Deserialize<AuthenticatorAssertionRawResponse>(credential) is not { RawId: { Length: > 0 } credentialId } assertion)
			return new Error<string>(NotVerified);

		if (await accounts.GetAccountPasskeyAsync(credentialId, ct) is not { } passkey)
		{
			logger.LogInformation("Passkey sign-in with an unknown credential");
			return new Error<string>("This passkey is not registered here. It may have been removed from the account.");
		}

		VerifyAssertionResult verified;
		try
		{
			verified = await new Fido2(ceremony.RelyingParty, null).MakeAssertionAsync(new MakeAssertionParams
			{
				AssertionResponse = assertion,
				OriginalOptions = ceremony.Options,
				StoredPublicKey = passkey.PublicKey,
				StoredSignatureCounter = passkey.SignCount,
				IsUserHandleOwnerOfCredentialIdCallback = (args, _) =>
					Task.FromResult(args.UserHandle.AsSpan().SequenceEqual(UserHandle(passkey.AccountId))),
			}, ct);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogInformation("Passkey sign-in for {AccountId} failed verification: {Message}",
				LogSanitizer.Sanitize(passkey.AccountId), ex.Message);
			return new Error<string>(NotVerified);
		}

		await accounts.RecordAccountPasskeyUseAsync(passkey.CredentialId, verified.SignCount, verified.IsBackedUp,
			time.GetUtcNow(), ct);

		return await accounts.GetAccountByIdAsync(passkey.AccountId, ct) is { } account
			? account
			: new Error<string>(NotVerified);
	}

	/// <summary>The account's passkeys, oldest first.</summary>
	public ValueTask<IReadOnlyList<AccountPasskey>> ListAsync(string accountId, CancellationToken ct = default)
		=> accounts.GetAccountPasskeysAsync(accountId, ct);

	/// <summary>Renames one of the account's passkeys; false when it holds no such passkey.</summary>
	public ValueTask<bool> RenameAsync(string accountId, byte[] credentialId, string name, CancellationToken ct = default)
		=> accounts.RenameAccountPasskeyAsync(accountId, credentialId, name, ct);

	/// <summary>Removes one of the account's passkeys; false when it holds no such passkey.</summary>
	public ValueTask<bool> RemoveAsync(string accountId, byte[] credentialId, CancellationToken ct = default)
		=> accounts.RemoveAccountPasskeyAsync(accountId, credentialId, ct);

	private static JsonElement Parse(string json)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}

	/// <summary>The browser's credential, or null when it is not one.</summary>
	private static T? Deserialize<T>(JsonElement credential) where T : class
	{
		try
		{
			return credential.ValueKind == JsonValueKind.Object ? credential.Deserialize<T>() : null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>The WebAuthn name of a transport (<c>usb</c>, <c>internal</c>, <c>hybrid</c>, ...).</summary>
	private static string TransportName(AuthenticatorTransport transport)
		=> JsonSerializer.Serialize(transport).Trim('"');
}
