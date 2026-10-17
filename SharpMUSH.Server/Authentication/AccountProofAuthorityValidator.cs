using Mediator;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication.External;
namespace SharpMUSH.Server.Authentication;

public sealed class AccountProofAuthorityValidator(IMediator mediator, ExternalProviderCatalog providers) : IAccountProofAuthorityValidator
{
	public async Task<bool> IsCurrentAsync(string accountId, AccountProofAuthority authority, CancellationToken ct = default)
	{
		if (authority.Method is "password" or "passkey") return true; // Their removals advance the account epoch transactionally.
		if (authority.Method != "external" || authority.IdentityId is null || authority.ProviderGeneration is null) return false;
		var identity = (await mediator.Send(new GetExternalIdentitiesQuery(accountId), ct)).FirstOrDefault(identity => identity.Id == authority.IdentityId);
		if (identity is null) return false;
		foreach (var definition in ExternalProviderDefinition.All)
			if (await providers.EffectiveAsync(definition.Id, ct) is EffectiveExternalProvider current
					&& current.Settings.RegistrationId == identity.Key.Registration && current.Generation == authority.ProviderGeneration) return true;
		return false;
	}
}
