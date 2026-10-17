using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public sealed class NativeLoginPolicy(IMediator mediator, IOptionsWrapper<SharpMUSHOptions> configuration) : INativeLoginPolicy
{
	public async ValueTask<bool> CanConnectAsync(DBRef character, string ip, string hostName, CancellationToken cancellationToken = default)
	{
		if (SitelockMatcher.IsBlocked(configuration.CurrentValue.SitelockRules.Rules, ip, hostName, SitelockMatcher.ConnectFlag)) return false;
		if (await mediator.Send(new GetObjectNodeQuery(character), cancellationToken) is not (AnySharpObject and SharpPlayer player)) return false;
		return configuration.CurrentValue.Net.Logins || await new AnySharpObject(player).IsWizard();
	}
}
