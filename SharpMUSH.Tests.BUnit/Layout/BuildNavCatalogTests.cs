using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Layout;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// The Build &amp; manage gates, declared once for the rail, the section sidebar and the mobile drawer:
/// any-of policies (with the menu's fallback pairs) or a <c>perm</c> claim for snapshots.
/// </summary>
public class BuildNavCatalogTests : BunitContext
{
	private readonly BunitAuthorizationContext _auth;

	public BuildNavCatalogTests() => _auth = AddAuthorization();

	private async Task<List<string>> VisibleHrefs(ClaimsPrincipal? user = null)
	{
		var authorization = Services.GetRequiredService<IAuthorizationService>();
		var principal = user ?? (await Services.GetRequiredService<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>()
			.GetAuthenticationStateAsync()).User;
		return (await BuildNavCatalog.VisibleAsync(authorization, principal)).Select(e => e.Href).ToList();
	}

	[Test]
	public async Task NothingStaffSide_ShowsNothing()
	{
		_auth.SetAuthorized("player");
		await Assert.That(await VisibleHrefs()).IsEmpty();
	}

	[Test]
	[Arguments("queue.inspect")]
	[Arguments("queue.inspect.own")]
	public async Task EitherInspectionScope_ShowsDiagnostics(string policy)
	{
		_auth.SetAuthorized("staff");
		_auth.SetPolicies(policy);
		await Assert.That(await VisibleHrefs()).IsEquivalentTo(new[] { "/admin/diagnostics" });
	}

	[Test]
	[Arguments("jobs.manage.own")]
	[Arguments("jobs.manage")]
	public async Task EitherJobsScope_ShowsJobs(string policy)
	{
		_auth.SetAuthorized("staff");
		_auth.SetPolicies(policy);
		await Assert.That(await VisibleHrefs()).IsEquivalentTo(new[] { "/admin/jobs" });
	}

	[Test]
	public async Task Snapshots_FollowTheirPermClaims()
	{
		_auth.SetAuthorized("builder");
		_auth.SetClaims(new Claim("perm", "snapshots.restore"));
		await Assert.That(await VisibleHrefs()).IsEquivalentTo(new[] { "/admin/snapshots" });
	}

	[Test]
	public async Task EachPolicy_ShowsItsOwnEntry_InMenuOrder()
	{
		_auth.SetAuthorized("wizard");
		_auth.SetPolicies("softcode.use", "config.admin", "players.view");
		await Assert.That(await VisibleHrefs()).IsEquivalentTo(new[] { "/softcode", "/admin", "/admin/config" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}
}
