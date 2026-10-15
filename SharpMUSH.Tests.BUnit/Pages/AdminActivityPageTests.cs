using System.Net;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages.Admin;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Components;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>The staff Activity page: the summary tiles, the heatmap, the drifting list, cohorts and rooms.</summary>
public class AdminActivityPageTests : TrackingBunitContext
{
	/// <summary>Answers each report by its path, ignoring the query, else 404.</summary>
	private sealed class Handler : HttpMessageHandler
	{
		public Dictionary<string, string> Bodies { get; } = new(StringComparer.Ordinal);
		private readonly List<HttpResponseMessage> _responses = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath.TrimStart('/');
			var response = Bodies.TryGetValue(path, out var body)
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
				: new HttpResponseMessage(HttpStatusCode.NotFound);
			_responses.Add(response);
			return Task.FromResult(response);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				foreach (var response in _responses) response.Dispose();
				_responses.Clear();
			}

			base.Dispose(disposing);
		}
	}

	private readonly Handler _api = new();

	public AdminActivityPageTests()
	{
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>()
			.AddSingleton<AdminActivityService>();
		JSInterop.Mode = JSRuntimeMode.Loose;
		var auth = AddAuthorization();
		auth.SetAuthorized("staff");
		auth.SetPolicies("players.view");
	}

	private static string Grid(double busiest)
	{
		var rows = Enumerable.Range(0, 7).Select(weekday =>
			"[" + string.Join(",", Enumerable.Range(0, 24).Select(hour => weekday == 4 && hour == 21 ? busiest : 0)) + "]");
		return "[" + string.Join(",", rows) + "]";
	}

	private void Reports(string scenes)
	{
		_api.Bodies["api/admin/activity/summary"] = $$"""
			{"asOf":"2026-10-15","days":30,"approved":40,"posed":18,"posedBefore":21,"poses":900,"posesBefore":1000,
			 "scenes":{{scenes}},"scenesBefore":{{scenes}},"newApprovals":3,"roomsUsed":9,"roomsDead":4,"drifting":1,
			 "peak":{"weekday":4,"hour":21,"players":6.5},"zone":"UTC"}
			""";
		_api.Bodies["api/admin/activity/hours"] = $$"""{"asOf":"2026-10-15","days":30,"zone":"UTC","players":{{Grid(6.5)}}}""";
		_api.Bodies["api/admin/activity/drifting"] = """
			{"asOf":"2026-10-15","days":30,"baseline":60,"minimumDays":4,"total":1,
			 "players":[{"objid":"#7:1","name":"Arden","lastPose":"2026-09-12T20:00:00+00:00","recentDays":0,"baselineDays":31,
			   "recentPoses":0,"baselinePoses":400}]}
			""";
		_api.Bodies["api/admin/activity/cohorts"] = """
			{"asOf":"2026-10-15","days":30,"undated":2,
			 "cohorts":[{"month":"2026-10","approved":2,"active":2,"retained":[null,null,null,null,null,null]},
			   {"month":"2026-07","approved":4,"active":3,"retained":[1,0.75,null,null,null,null]}]}
			""";
		_api.Bodies["api/admin/activity/rooms"] = $$"""
			{"asOf":"2026-10-15","days":30,"deadDays":90,"usedTotal":1,"deadTotal":1,
			 "used":[{"objid":"#20:1","name":"Harbour Market","poses":800,"days":27,"people":20,"scenes":{{scenes}},"lastPose":"2026-10-15T21:00:00+00:00"}],
			 "dead":[{"objid":"#21:1","name":"Bell Tower","poses":0,"days":0,"people":0,"scenes":{{scenes}},"lastPose":null}]}
			""";
	}

	private IRenderedComponent<MudHarness> RenderPage() =>
		Render<MudHarness>(p => p.AddChildContent<Activity>());

	[Test]
	public async Task DrawsEveryReport()
	{
		Reports("12");
		var cut = RenderPage();

		cut.WaitForAssertion(() => cut.Find("a.adm-activity-link"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find("a.adm-activity-link").GetAttribute("href")).IsEqualTo("/admin/characters/7");
		await Assert.That(cut.FindAll(".adm-activity-heat-row [role=cell]").Count).IsEqualTo(7 * 24);
		await Assert.That(cut.Markup).Contains("AdmActivityScenes");
		await Assert.That(cut.Markup).Contains("Harbour Market").And.Contains("Bell Tower").And.Contains("2026-07");
		await Assert.That(cut.Markup).Contains("AdmActivityUndated(2)");
	}

	[Test]
	public async Task WithoutTheScenePluginShowsNoSceneNumbers()
	{
		Reports("null");
		var cut = RenderPage();

		cut.WaitForAssertion(() => cut.Find("a.adm-activity-link"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Markup).DoesNotContain("AdmActivityScenes");
	}
}
