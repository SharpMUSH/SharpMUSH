using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Layout;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// The Build &amp; manage gates, declared once for the rail, the section sidebar, the mobile drawer and the
/// overview: any-of policies (with the menu's fallback pairs), a <c>perm</c> claim for snapshots, a role
/// for accounts; and the overview, offered only when there is a staff page to put on it.
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
		await Assert.That(await VisibleHrefs()).IsEquivalentTo(new[] { "/admin", "/softcode", "/admin/characters", "/admin/guests", "/admin/suggestions", "/admin/config" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>A builder's own tools are not a staff area: they get no overview, so the rail opens their first tool.</summary>
	[Test]
	public async Task BuildToolsAlone_GetNoOverview()
	{
		_auth.SetAuthorized("builder");
		_auth.SetPolicies("softcode.use", "jobs.manage.own");
		await Assert.That(await VisibleHrefs()).IsEquivalentTo(new[] { "/softcode", "/admin/jobs" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	[Arguments("Wizard")]
	[Arguments("God")]
	public async Task Accounts_FollowTheWizardRole(string role)
	{
		_auth.SetAuthorized("wizard");
		_auth.SetRoles(role);
		await Assert.That(await VisibleHrefs()).IsEquivalentTo(new[] { "/admin", "/admin/accounts" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>Every staff group has a heading in the resx, and the catalogue routes no two entries to one page.</summary>
	[Test]
	public async Task EveryGroupHasAHeading_AndEveryEntryItsOwnPage()
	{
		foreach (var group in BuildNavCatalog.All.Select(e => e.Group).Distinct())
			await Assert.That(BuildNavCatalog.GroupLabelKey(group)).IsNotNull().Because(group.ToString());
		await Assert.That(BuildNavCatalog.All.Select(e => e.Href).Distinct().Count()).IsEqualTo(BuildNavCatalog.All.Count);
	}
}
