using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services.Imports;

/// <summary>
/// The portal account that claimed the game, as an import carries it into the world it builds: its sign-in
/// methods, its account roles and overrides, and whether the setup wizard was still open. Sessions, remembered
/// logins and app grants stay behind; the account signs in again on the promoted world.
/// </summary>
public sealed record PortalAdministrator(
	string Username,
	string? Email,
	string? PasswordHash,
	bool AllowCharacterPasswordAccountLogin,
	bool MustChangePassword,
	IReadOnlyList<AccountPasskey> Passkeys,
	IReadOnlyList<PortalAdministratorIdentity> Identities,
	IReadOnlyList<string> Roles,
	IReadOnlyDictionary<string, PermissionState> Overrides,
	bool WizardPending);

/// <summary>An external sign-in identity of the <see cref="PortalAdministrator"/>.</summary>
public sealed record PortalAdministratorIdentity(ExternalIdentityKey Key, string? Label);

/// <summary>Takes the <see cref="PortalAdministrator"/> from the live world and gives it to an import's world.</summary>
public sealed class PortalAdministratorTransfer(IServerStateStore state, IAccountStore accounts,
	IExternalIdentityStore identities, IRoleRegistryService roles, IExpandedObjectDataService serverData)
{
	private static readonly DBRef God = new(1);

	/// <summary>
	/// The account linked to #1 once setup has been claimed, or null when there is nothing to carry: the game is
	/// unclaimed, or the account cannot sign in. The new world then makes its own unclaimed administrator.
	/// </summary>
	public async Task<PortalAdministrator?> CaptureAsync(CancellationToken cancellationToken = default)
	{
		if (!(await state.GetServerStateAsync(cancellationToken)).SetupCompleted
			|| await accounts.GetAccountForCharacterAsync(God, cancellationToken) is not { IsActive: true, Id: { } id } account)
			return null;
		var passkeys = await accounts.GetAccountPasskeysAsync(id, cancellationToken);
		var external = await identities.GetExternalIdentitiesAsync(id, cancellationToken);
		if (string.IsNullOrEmpty(account.PasswordHash) && passkeys.Count == 0 && external.Count == 0) return null;
		return new PortalAdministrator(account.Username, account.Email, account.PasswordHash,
			account.AllowCharacterPasswordAccountLogin, account.MustChangePassword, passkeys,
			external.Select(identity => new PortalAdministratorIdentity(identity.Key, identity.Label)).ToList(),
			(await roles.GetRolesForAccountAsync(id, cancellationToken)).Select(role => role.Slug).ToList(),
			await roles.GetAccountOverridesAsync(id, cancellationToken),
			(await serverData.GetExpandedServerDataAsync<SetupWizardState>())?.Pending ?? false);
	}

	/// <summary>
	/// Gives <paramref name="administrator"/> to the world behind <paramref name="services"/>, linked to its #1 and
	/// with setup claimed, so the new world neither makes an unclaimed administrator nor offers the claim again.
	/// The imported #1 keeps its own password. A role the new world does not have is left off, and said so.
	/// </summary>
	public static async Task RestoreAsync(PortalAdministrator administrator, IServiceProvider services,
		ConversionResult result, CancellationToken cancellationToken)
	{
		var accounts = services.GetRequiredService<IAccountStore>();
		var identities = services.GetRequiredService<IExternalIdentityStore>();
		var roles = services.GetRequiredService<IRoleRegistryService>();
		var account = await accounts.CreateAccountAsync(administrator.Username, administrator.Email,
			administrator.PasswordHash ?? string.Empty, cancellationToken);
		var id = account.Id!;
		if (!administrator.AllowCharacterPasswordAccountLogin)
			await accounts.UpdateAccountCharacterPasswordLoginAsync(id, false, cancellationToken);
		if (administrator.MustChangePassword)
			await accounts.UpdateAccountMustChangePasswordAsync(id, true, cancellationToken);
		foreach (var passkey in administrator.Passkeys)
			if (await accounts.AddAccountPasskeyAsync(passkey with { AccountId = id }, int.MaxValue, cancellationToken) != PasskeyAddOutcome.Added)
				throw new InvalidOperationException($"The administrator's passkey '{passkey.Name}' could not be carried into the imported world.");
		foreach (var identity in administrator.Identities)
			if (await identities.AttachExternalIdentityAsync(id, identity.Key, identity.Label, cancellationToken) is Error<string> error)
				throw new InvalidOperationException($"The administrator's {identity.Key.Registration} sign-in could not be carried into the imported world: {error.Value}");

		var known = (await roles.GetRolesAsync(cancellationToken)).Select(role => role.Slug).ToHashSet(StringComparer.Ordinal);
		foreach (var slug in administrator.Roles)
		{
			if (known.Contains(slug)) await roles.AssignRoleToAccountAsync(id, slug);
			else result.Warnings.Add($"The imported world has no role '{slug}', so {administrator.Username} does not hold it.");
		}
		foreach (var (scope, permission) in administrator.Overrides)
			await roles.SetAccountOverrideAsync(id, scope, permission);

		if (await services.GetRequiredService<IObjectStore>().GetObjectNodeAsync(God, cancellationToken) is AnySharpObject and SharpPlayer)
			await accounts.LinkCharacterToAccountAsync(id, God, cancellationToken);
		else
			result.Warnings.Add($"#1 is not a player in the imported world, so {administrator.Username} has no character there.");

		await services.GetRequiredService<IServerStateStore>().SetServerSetupCompletedAsync(true, cancellationToken);
		await services.GetRequiredService<IExpandedObjectDataService>()
			.SetExpandedServerDataAsync(new SetupWizardState { Pending = administrator.WizardPending });
	}
}
