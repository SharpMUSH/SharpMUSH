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
		IAdministrativeCapabilityService Capabilities)
		Build()
	{
		var accountSvc = Substitute.For<IAccountService>();
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();

		accountSvc.GetCharactersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<IReadOnlyList<SharpPlayer>>((IReadOnlyList<SharpPlayer>)[]));
		capabilities.GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string>());
		capabilities.GetContextAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(PermissionContext.None);

		var cache = new FusionCache(new Microsoft.Extensions.Options.OptionsWrapper<FusionCacheOptions>(new FusionCacheOptions()));

		var svc = new AccountClaimsService(capabilities, cache,
			new AccountClaimsInvalidator(cache), NullLogger<AccountClaimsService>.Instance);

		return (svc, accountSvc, capabilities);
	}

	[Test]
	public async ValueTask ComputeAccountRoleAsync_RepeatedCalls_HitUnderlyingServiceOnce()
	{
		var (svc, _, capabilities) = Build();

		await svc.ComputeAccountRoleAsync("accounts/1");
		await svc.ComputeAccountRoleAsync("accounts/1");

		await capabilities.Received(1).GetContextAsync(Arg.Is<CapabilityActor>(a => a.AccountId == "accounts/1"), Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask ComputeAccountRoleAsync_AfterInvalidate_HitsUnderlyingServiceAgain()
	{
		var (svc, _, capabilities) = Build();

		await svc.ComputeAccountRoleAsync("accounts/1");
		await svc.ComputeAccountRoleAsync("accounts/1");
		await capabilities.Received(1).GetContextAsync(Arg.Is<CapabilityActor>(a => a.AccountId == "accounts/1"), Arg.Any<CancellationToken>());

		await svc.InvalidateAsync("accounts/1");

		await svc.ComputeAccountRoleAsync("accounts/1");
		await capabilities.Received(2).GetContextAsync(Arg.Is<CapabilityActor>(a => a.AccountId == "accounts/1"), Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask InvalidateAsync_ClearsBothRoleAndScopeCacheEntries()
	{
		var (svc, _, capabilities) = Build();

		await svc.ComputeAccountRoleAsync("accounts/1");
		await svc.ComputeGrantedScopesAsync("accounts/1");
		await capabilities.Received(1).GetContextAsync(Arg.Is<CapabilityActor>(a => a.AccountId == "accounts/1"), Arg.Any<CancellationToken>());
		await capabilities.Received(1).GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>());

		await svc.InvalidateAsync("accounts/1");

		await svc.ComputeAccountRoleAsync("accounts/1");
		await svc.ComputeGrantedScopesAsync("accounts/1");

		await capabilities.Received(2).GetContextAsync(Arg.Is<CapabilityActor>(a => a.AccountId == "accounts/1"), Arg.Any<CancellationToken>());
		await capabilities.Received(2).GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask ComputeAccountRoleAsync_DifferentAccounts_AreCachedIndependently()
	{
		var (svc, _, capabilities) = Build();

		await svc.ComputeAccountRoleAsync("accounts/1");
		await svc.ComputeAccountRoleAsync("accounts/2");

		await capabilities.Received(1).GetContextAsync(Arg.Is<CapabilityActor>(a => a.AccountId == "accounts/1"), Arg.Any<CancellationToken>());
		await capabilities.Received(1).GetContextAsync(Arg.Is<CapabilityActor>(a => a.AccountId == "accounts/2"), Arg.Any<CancellationToken>());
	}
}
