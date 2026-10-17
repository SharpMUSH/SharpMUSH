using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
namespace SharpMUSH.Implementation.Handlers.Database;
/// <summary>Typed nonsecret settings; the expanded-data provider owns serialization.</summary>
public sealed class ExternalProviderSettingsStore(IExpandedDataStore store) : IExternalProviderSettingsStore
{
	public async ValueTask<ExternalProviderSettings> ReadAsync(string provider, CancellationToken cancellationToken = default)
	 => await store.GetExpandedServerData<ExternalProviderSettings>("external-signin-" + provider, cancellationToken) ?? new();
	public ValueTask WriteAsync(string provider, ExternalProviderSettings settings, CancellationToken cancellationToken = default)
	 => store.SetExpandedServerData("external-signin-" + provider, settings, cancellationToken);
}
public sealed class ExternalProviderSettingsQueryHandler(IExternalProviderSettingsStore store) : IQueryHandler<GetExternalProviderSettingsQuery, ExternalProviderSettings>
{
	public ValueTask<ExternalProviderSettings> Handle(GetExternalProviderSettingsQuery query, CancellationToken cancellationToken)
	 => store.ReadAsync(query.Provider, cancellationToken);
}
public sealed class ExternalProviderSettingsCommandHandler(IExternalProviderSettingsStore store) : ICommandHandler<SaveExternalProviderSettingsCommand>
{
	public async ValueTask<Unit> Handle(SaveExternalProviderSettingsCommand command, CancellationToken cancellationToken)
	{ await store.WriteAsync(command.Provider, command.Settings, cancellationToken); return Unit.Value; }
}
public sealed class ExternalProviderAccountCountQueryHandler(IAccountStore accounts, IExternalIdentityStore identities) : IQueryHandler<GetExternalProviderAccountCountQuery, int>
{
	public async ValueTask<int> Handle(GetExternalProviderAccountCountQuery query, CancellationToken cancellationToken)
	{
		var count = 0;
		foreach (var account in await accounts.GetAllAccountsAsync(cancellationToken))
			if ((await identities.GetExternalIdentitiesAsync(account.Id!, cancellationToken)).Any(identity => identity.Key.Registration == query.Registration)) count++;
		return count;
	}
}
