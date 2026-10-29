using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Pages;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Real route matching/parameter/query handling; shell and authorization have their own suites.</summary>
public class HelpGuidancePlacementTests : TrackingBunitContext
{
	private void Arrange(string uri)
	{
		HelpApi.Install(this, true, HelpPageTests.Corpora);
		Services.AddEchoLocalizer();
		this.AddAuthorization().SetAuthorized("staff").SetRoles("Wizard");
		Services.GetRequiredService<NavigationManager>().NavigateTo(uri);
	}

	private IRenderedComponent<Router> RenderRoute() => Render<Router>(parameters => parameters
		.Add(p => p.AppAssembly, typeof(Help).Assembly)
		.Add(p => p.NotFoundPage, typeof(NotFound))
		.Add(p => p.Found, (RenderFragment<RouteData>)(route => builder =>
		{
			builder.OpenComponent(0, route.PageType);
			foreach (var value in route.RouteValues) builder.AddAttribute(1, value.Key, value.Value);
			builder.CloseComponent();
		})));

	[Test]
	[Arguments("/help")]
	[Arguments("/help/")]
	[Arguments("/help?q=mail-s#topics")]
	public async Task GuidanceIsACompactCardOnTheActualIndexIncludingItsFilterQuery(string uri)
	{
		Arrange(uri);
		var cut = RenderRoute();
		cut.WaitForAssertion(() => cut.Find(".help-guidance .kit-card"));
		await Assert.That(cut.FindComponents<Help>().Count).IsEqualTo(1);
		await Assert.That(cut.FindAll("h1").Count).IsEqualTo(1);
		await Assert.That(cut.Find(".help-guidance .kit-card-title").TextContent).IsEqualTo("GuideHelpIndexEntry");
		await Assert.That(cut.Find(".help-guidance a[href='/welcome']")).IsNotNull();
		await Assert.That(cut.Find(".help-guidance a[href='/help']")).IsNotNull();
		await Assert.That(cut.FindAll(".help-guidance .kit-banner")).IsEmpty();
		if (uri.Contains("q=", StringComparison.Ordinal))
			await Assert.That(cut.Find(".help-filter").GetAttribute("value")).IsEqualTo("mail-s");
	}

	[Test]
	[Arguments("/help/@MAIL", "@MAIL")]
	[Arguments("/help/getting%20started?q=mail", "getting started")]
	public async Task DeepCommandAndTopicRoutesNeverRenderTheIndexGuidance(string uri, string topic)
	{
		Arrange(uri);
		var cut = RenderRoute();
		cut.WaitForAssertion(() => cut.FindComponent<HelpTopic>());
		await Assert.That(cut.FindComponent<HelpTopic>().Instance.Topic).IsEqualTo(topic);
		await Assert.That(cut.FindComponents<Help>()).IsEmpty();
		await Assert.That(cut.FindAll(".help-guidance, .journey-links")).IsEmpty();
	}

	[Test]
	public async Task AdminTopicHasNoIndexGuidance()
	{
		Arrange("/help/admin/Security");
		var cut = RenderRoute();
		cut.WaitForAssertion(() => cut.FindComponent<HelpAdminTopic>());
		await Assert.That(cut.FindComponent<HelpAdminTopic>().Instance.Topic).IsEqualTo("Security");
		await Assert.That(cut.FindAll(".help-guidance, .journey-links")).IsEmpty();
	}

	[Test]
	public async Task UnmatchedDeeperPathIsNotAnIndexCatchAll()
	{
		Arrange("/help/one/two");
		var cut = RenderRoute();
		await Assert.That(cut.FindComponents<Help>()).IsEmpty();
		await Assert.That(cut.FindComponents<HelpTopic>()).IsEmpty();
		await Assert.That(cut.FindAll(".help-guidance, .journey-links")).IsEmpty();
		await Assert.That(typeof(Help).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()
			.Select(a => a.Template).ToArray()).IsEquivalentTo(new[] { "/help" });
	}

	[Test]
	public async Task MountedIndexDropsGuidanceIfItsLocationMovesToADeepPath()
	{
		Arrange("/help");
		// The product router normally disposes this component. Exercise the explicit guard as well.
		var cut = Render<Help>();
		cut.WaitForAssertion(() => cut.Find(".help-guidance"));
		await cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/help/@MAIL?q=mail"));
		cut.WaitForAssertion(() => { if (cut.FindAll(".help-guidance").Count != 0) throw new InvalidOperationException("index guidance leaked onto a deeper path"); });
	}
}
