using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Layout;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// The Build &amp; manage gates, declared once for the rail, the section sidebar, the mobile drawer and the
/// overview: any-of policies, a role for snapshots and accounts; and the overview, offered only when there is a staff page to put on it.
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
	public async Task StaffInspection_ShowsDiagnostics()
	{
		_auth.SetAuthorized("staff");
		_auth.SetPolicies("queue.inspect");
		await Assert.That(await VisibleHrefs()).IsEquivalentTo(new[] { "/admin/diagnostics" });
	}

	[Test]
	public async Task StaffJobs_ShowsJobs()
	{
		_auth.SetAuthorized("staff");
		_auth.SetPolicies("jobs.manage");
		await Assert.That(await VisibleHrefs()).IsEquivalentTo(new[] { "/admin/jobs" });
	}

	[Test]
	public async Task Snapshots_AreForWizards()
	{
		_auth.SetAuthorized("wizard");
		_auth.SetRoles("Wizard");
		await Assert.That(await VisibleHrefs()).Contains("/admin/snapshots");
	}

	/// <summary>
	/// What every player holds for their own objects (snapshots, their own recurring jobs and queue) is not
	/// a staff page: none of the three is offered to a player.
	/// </summary>
	[Test]
	public async Task APlayersOwnScopes_ShowNoStaffPages()
	{
		_auth.SetAuthorized("player");
		_auth.SetRoles("Player");
		_auth.SetPolicies("jobs.manage.own", "queue.inspect.own", "queue.control.own", "snapshots.capture", "snapshots.restore");
		_auth.SetClaims(
			new Claim("perm", "jobs.manage.own"), new Claim("perm", "queue.inspect.own"),
			new Claim("perm", "snapshots.capture"), new Claim("perm", "snapshots.restore"));
		await Assert.That(await VisibleHrefs()).IsEmpty();
	}

	[Test]
	public async Task EachPolicy_ShowsItsOwnEntry_InMenuOrder()
	{
		_auth.SetAuthorized("wizard");
		_auth.SetPolicies("softcode.use", "config.admin", "players.view");
		await Assert.That(await VisibleHrefs()).IsEquivalentTo(new[] { "/admin", "/softcode", "/admin/characters", "/admin/guests", "/admin/suggestions", "/admin/messages", "/admin/config" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>A builder's own tools are not a staff area: they get no overview, so the rail opens their first tool.</summary>
	[Test]
	public async Task BuildToolsAlone_GetNoOverview()
	{
		_auth.SetAuthorized("builder");
		_auth.SetPolicies("softcode.use", "jobs.manage");
		await Assert.That(await VisibleHrefs()).IsEquivalentTo(new[] { "/softcode", "/admin/jobs" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	[Arguments("Wizard")]
	[Arguments("God")]
	public async Task Accounts_FollowTheWizardRole(string role)
	{
		_auth.SetAuthorized("wizard");
		_auth.SetRoles(role);
		await Assert.That(await VisibleHrefs()).IsEquivalentTo(new[] { "/admin", "/admin/snapshots", "/admin/accounts" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>Every staff group has a heading in the resx, and the catalogue routes no two entries to one page.</summary>
	[Test]
	public async Task EveryGroupHasAHeading_AndEveryEntryItsOwnPage()
	{
		foreach (var group in BuildNavCatalog.All.Select(e => e.Group).Distinct())
			await Assert.That(BuildNavCatalog.GroupLabelKey(group)).IsNotNull().Because(group.ToString());
		await Assert.That(BuildNavCatalog.All.Select(e => e.Href).Distinct().Count()).IsEqualTo(BuildNavCatalog.All.Count);
	}

	/// <summary>
	/// The overview page's own gate: typing /admin refuses a player and a builder with only Build tools,
	/// the same viewers no link sends there.
	/// </summary>
	[Test]
	public async Task TheOverviewPolicy_AdmitsExactlyThoseTheOverviewLinkIsOfferedTo()
	{
		static ClaimsPrincipal User(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

		await Assert.That(BuildNavCatalog.MayOpenOverview(User())).IsFalse();
		await Assert.That(BuildNavCatalog.MayOpenOverview(User(new Claim("perm", "softcode.use"), new Claim("perm", "jobs.manage.own")))).IsFalse();
		await Assert.That(BuildNavCatalog.MayOpenOverview(User(new Claim("perm", "players.view")))).IsTrue();
		await Assert.That(BuildNavCatalog.MayOpenOverview(User(new Claim(ClaimTypes.Role, "Wizard")))).IsTrue();

		var gate = typeof(SharpMUSH.Client.Pages.Admin.Dashboard).GetCustomAttributes(typeof(AuthorizeAttribute), true)
			.Cast<AuthorizeAttribute>().Single();
		await Assert.That(gate.Policy).IsEqualTo(BuildNavCatalog.OverviewPolicy);
	}
}
