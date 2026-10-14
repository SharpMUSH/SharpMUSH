using System.Globalization;
using System.Security.Claims;
using Mediator;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.CodeAnalysis.Catalog;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>Host-only identity resolution. Each operation refreshes identity and permission metadata.</summary>
public sealed class AnalysisSessionFactory(IMediator mediator, IAccountService accounts, IAnalysisCatalog catalog, IOptionsWrapper<SharpMUSHOptions> options) : IAnalysisSessionFactory
{
	private readonly IMediator _mediator = mediator;
	private readonly IAccountService _accounts = accounts;
	private readonly IAnalysisCatalog _catalog = catalog;
	private readonly IOptionsWrapper<SharpMUSHOptions> _options = options;
	public IAnalysisSession CreateSession(ClaimsPrincipal principal)
	{
		var identity = principal.Identities.FirstOrDefault(value => value.IsAuthenticated && value.AuthenticationType == AnalysisTransportSecurity.AuthenticationScheme);
		if (identity is null || !int.TryParse(identity.FindFirst("character_key")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var key) ||
			!long.TryParse(identity.FindFirst("character_creation_time")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var created) ||
			identity.FindFirst(AnalysisIdentity.CredentialVersionClaim)?.Value is not { Length: > 0 } version)
			throw new UnauthorizedAccessException("Authenticated analysis identity is required.");
		return new Session(this, new DBRef(key, created), identity.FindFirst(ClaimTypes.NameIdentifier)?.Value, version);
	}

	private sealed class Session(AnalysisSessionFactory factory, DBRef reference, string? accountId, string credentialVersion) : IAnalysisSession
	{
		public string PrincipalKey { get; } = reference + "/" + accountId;
		public async ValueTask<IMushCodeAnalyzer> CreateAnalyzerAsync(CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var result = await factory._mediator.Send(new GetObjectNodeQuery(reference), cancellationToken);
			if (result is not (AnySharpObject found and SharpPlayer player) || player.Object.DBRef != reference ||
				string.IsNullOrEmpty(player.PasswordHash) || AnalysisIdentity.CredentialVersion(player.PasswordHash) != credentialVersion)
				throw new UnauthorizedAccessException("Analysis identity is no longer available.");
			var account = await factory._accounts.GetAccountForCharacterAsync(reference, cancellationToken);
			if (account is { IsActive: false } || account?.Id != accountId)
				throw new UnauthorizedAccessException("Analysis identity is no longer available.");
			var snapshot = await factory._catalog.GetSnapshotAsync(found, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			return AnalysisCatalogAnalyzer.Create(snapshot, factory._options.CurrentValue);
		}
	}
}
