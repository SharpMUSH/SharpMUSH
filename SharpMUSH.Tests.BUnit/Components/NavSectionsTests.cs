using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// BUnit tests for the data-driven NavBar sections. A registered Page app's <c>NavPlacement</c> names the
/// sidebar section its link belongs to: a built-in name slots into that group, a novel name forms its own
/// group, and an app whose <c>MinimumRole</c> exceeds the caller is hidden.
/// </summary>
public abstract class NavSectionsTestBase : BunitContext
{
	protected BunitAuthorizationContext Auth { get; }

	protected NavSectionsTestBase()
	{
		Services.AddMudServices();
		// ApplicationNavLinks resolves ApplicationRegistryClient even when Applications is pre-supplied (it
		// only calls it when Applications is null); register a stub so DI is satisfied.
		var factory = Substitute.For<IHttpClientFactory>();
		Services.AddSingleton(new ApplicationRegistryClient(factory, NullLogger<ApplicationRegistryClient>.Instance));
		Auth = AddAuthorization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	protected static PortalApplication App(string slug, string nav, int order,
		PortalRole role = PortalRole.Guest) =>
		new(slug, $"App {slug}", "Apps", "Page", $"http/{slug}/schema", null, null, role.ToString(), nav, [], order);

	/// <summary>
	/// Authorize the caller and stamp a portal role claim (ClaimTypes.Role) that PortalRoleHelper reads, since
	/// SetAuthorized only sets the username.
	/// </summary>
	protected void AuthorizeAs(PortalRole role)
	{
		Auth.SetAuthorized(role.ToString());
		Auth.SetClaims(new Claim(ClaimTypes.Role, role.ToString()));
	}
}

public class ApplicationNavLinksSectionTests : NavSectionsTestBase
{
	[TUnit.Core.Test]
	public async Task BuiltInSection_RendersOnlyItsApps()
	{
		AuthorizeAs(PortalRole.Wizard);
		var apps = new[]
		{
			App("build-app", "Build", 1),
			App("world-app", "World", 1),
		};

		var cut = Render<ApplicationNavLinks>(p => p
			.Add(c => c.Section, "Build")
			.Add(c => c.IsCollapsed, false)
			.Add(c => c.Applications, apps));

		await Assert.That(cut.Markup).Contains("/apps/build-app");
		await Assert.That(cut.Markup).DoesNotContain("/apps/world-app");
	}

	[TUnit.Core.Test]
	public async Task NovelSection_RendersItsApp()
	{
		AuthorizeAs(PortalRole.Wizard);
		var apps = new[] { App("plugin-app", "Plugins", 50) };

		var cut = Render<ApplicationNavLinks>(p => p
			.Add(c => c.Section, "Plugins")
			.Add(c => c.IsCollapsed, false)
			.Add(c => c.Applications, apps));

		await Assert.That(cut.Markup).Contains("/apps/plugin-app");
		await Assert.That(cut.Markup).Contains("App plugin-app");
	}

	[TUnit.Core.Test]
	public async Task MinimumRole_HidesInaccessibleApp()
	{
		// Caller is a Player; a Wizard-only app in this section must not render.
		AuthorizeAs(PortalRole.Player);
		var apps = new[]
		{
			App("player-app", "Build", 1, PortalRole.Player),
			App("wizard-app", "Build", 2, PortalRole.Wizard),
		};

		var cut = Render<ApplicationNavLinks>(p => p
			.Add(c => c.Section, "Build")
			.Add(c => c.IsCollapsed, false)
			.Add(c => c.Applications, apps));

		await Assert.That(cut.Markup).Contains("/apps/player-app");
		await Assert.That(cut.Markup).DoesNotContain("/apps/wizard-app");
	}
}

/// <summary>
/// Pure-logic coverage of <see cref="PortalNavSections"/>: novel-section ordering and role filtering, which
/// the sidebar relies on to place new groups deterministically without a full NavMenu render.
/// </summary>
public class PortalNavSectionsTests
{
	private static PortalApplication App(string slug, string nav, int order,
		PortalRole role = PortalRole.Guest) =>
		new(slug, $"App {slug}", null, "Page", $"http/{slug}/schema", null, null, role.ToString(), nav, [], order);

	[TUnit.Core.Test]
	public async Task NovelSections_ExcludeBuiltIns_OrderByMinOrderThenName()
	{
		var apps = new[]
		{
			App("a", "Build", 0),          // built-in → excluded
			App("b", "Zeta", 30),          // novel, min order 30
			App("c", "Alpha", 10),         // novel, min order 10
			App("d", "Alpha", 99),         // same novel section, does not change min order
			App("e", "Mid", 10),           // novel, ties Alpha at 10 → name tiebreak (Alpha before Mid)
		};

		var sections = PortalNavSections.NovelSections(apps, PortalRole.God);

		await Assert.That(sections).IsEquivalentTo(new[] { "Alpha", "Mid", "Zeta" });
	}

	[TUnit.Core.Test]
	public async Task NovelSections_RespectMinimumRole()
	{
		var apps = new[]
		{
			App("p", "Plugins", 1, PortalRole.Player),
			App("w", "WizardOnly", 1, PortalRole.Wizard),
		};

		// A Player sees only the Player-accessible novel section.
		var asPlayer = PortalNavSections.NovelSections(apps, PortalRole.Player);
		await Assert.That(asPlayer).IsEquivalentTo(new[] { "Plugins" });

		// A Wizard sees both.
		var asWizard = PortalNavSections.NovelSections(apps, PortalRole.Wizard);
		await Assert.That(asWizard).Contains("Plugins");
		await Assert.That(asWizard).Contains("WizardOnly");
	}

	[TUnit.Core.Test]
	public async Task AppsForSection_FiltersAndOrders()
	{
		var apps = new[]
		{
			App("b2", "Build", 2),
			App("b1", "Build", 1),
			App("w1", "World", 1),
		};

		var build = PortalNavSections.AppsForSection(apps, PortalRole.God, "Build");
		await Assert.That(build.Select(a => a.Slug)).IsEquivalentTo(new[] { "b1", "b2" });
	}
}
