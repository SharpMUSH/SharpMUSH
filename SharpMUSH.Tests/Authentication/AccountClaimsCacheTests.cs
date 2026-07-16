using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Authentication;

/// <summary>
/// Unit tests for <see cref="AccountClaimsService"/>'s FusionCache wrapping: repeated calls for
/// the same account should hit the underlying <see cref="IAccountService"/>/<see cref="IRoleRegistryService"/>
/// dependencies only once within the cache TTL, and <see cref="AccountClaimsService.InvalidateAsync"/>
/// should clear both the role and scope cache entries for that account.
/// </summary>
public class AccountClaimsCacheTests
{
	private static (
		AccountClaimsService Service,
		IAccountService AccountSvc,
		IRoleRegistryService RoleRegistry)
		Build()
	{
		var accountSvc = Substitute.For<IAccountService>();
		var roleDerivation = Substitute.For<IRoleDerivationService>();
		var roleRegistry = Substitute.For<IRoleRegistryService>();
		var permissionResolver = Substitute.For<IPermissionResolver>();

		accountSvc.GetCharactersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<IReadOnlyList<SharpPlayer>>((IReadOnlyList<SharpPlayer>)[]));
		roleRegistry.GetRolesAsync().Returns(Task.FromResult<IReadOnlyList<SharpRole>>([]));
		roleRegistry.GetRolesForAccountAsync(Arg.Any<string>()).Returns(Task.FromResult<IReadOnlyList<SharpRole>>([]));
		permissionResolver.Resolve(Arg.Any<IEnumerable<SharpRole>>()).Returns(new HashSet<string>());

		var cache = new FusionCache(new Microsoft.Extensions.Options.OptionsWrapper<FusionCacheOptions>(new FusionCacheOptions()));

		var svc = new AccountClaimsService(accountSvc, roleDerivation, roleRegistry, permissionResolver, cache,
			NullLogger<AccountClaimsService>.Instance);

		return (svc, accountSvc, roleRegistry);
	}

	[Test]
	public async ValueTask ComputeAccountRoleAsync_RepeatedCalls_HitUnderlyingServiceOnce()
	{
		var (svc, accountSvc, _) = Build();

		await svc.ComputeAccountRoleAsync("accounts/1", PortalRole.Player);
		await svc.ComputeAccountRoleAsync("accounts/1", PortalRole.Player);

		await accountSvc.Received(1).GetCharactersAsync("accounts/1", Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask ComputeAccountRoleAsync_AfterInvalidate_HitsUnderlyingServiceAgain()
	{
		var (svc, accountSvc, _) = Build();

		await svc.ComputeAccountRoleAsync("accounts/1", PortalRole.Player);
		await svc.ComputeAccountRoleAsync("accounts/1", PortalRole.Player);
		await accountSvc.Received(1).GetCharactersAsync("accounts/1", Arg.Any<CancellationToken>());

		await svc.InvalidateAsync("accounts/1");

		await svc.ComputeAccountRoleAsync("accounts/1", PortalRole.Player);
		await accountSvc.Received(2).GetCharactersAsync("accounts/1", Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask InvalidateAsync_ClearsBothRoleAndScopeCacheEntries()
	{
		var (svc, accountSvc, roleRegistry) = Build();

		await svc.ComputeAccountRoleAsync("accounts/1", PortalRole.Player);
		await svc.ComputeGrantedScopesAsync("accounts/1", PortalRole.Player);
		await accountSvc.Received(1).GetCharactersAsync("accounts/1", Arg.Any<CancellationToken>());
		await roleRegistry.Received(1).GetRolesAsync();

		await svc.InvalidateAsync("accounts/1");

		await svc.ComputeAccountRoleAsync("accounts/1", PortalRole.Player);
		await svc.ComputeGrantedScopesAsync("accounts/1", PortalRole.Player);

		await accountSvc.Received(2).GetCharactersAsync("accounts/1", Arg.Any<CancellationToken>());
		await roleRegistry.Received(2).GetRolesAsync();
	}

	[Test]
	public async ValueTask ComputeAccountRoleAsync_DifferentAccounts_AreCachedIndependently()
	{
		var (svc, accountSvc, _) = Build();

		await svc.ComputeAccountRoleAsync("accounts/1", PortalRole.Player);
		await svc.ComputeAccountRoleAsync("accounts/2", PortalRole.Player);

		await accountSvc.Received(1).GetCharactersAsync("accounts/1", Arg.Any<CancellationToken>());
		await accountSvc.Received(1).GetCharactersAsync("accounts/2", Arg.Any<CancellationToken>());
	}
}
