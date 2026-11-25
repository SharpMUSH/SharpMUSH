using System.Net;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Pages.Admin;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// The portal side of the gallery's moderation options (#2879): the staff lock hides a player's controls, a player
/// whose portrait waits for staff asks for one and sees it waiting, and staff approve requests in the viewer and on
/// the Portrait approvals page.
/// </summary>
public class GalleryModerationViewTests : TrackingBunitContext
{
	/// <summary>Answers by method and path, and keeps every request it was sent.</summary>
	private sealed class Handler : HttpMessageHandler
	{
		public Dictionary<string, string> Bodies { get; } = new(StringComparer.Ordinal);
		public List<string> Sent { get; } = [];
		private readonly List<HttpResponseMessage> _responses = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var key = $"{request.Method} {request.RequestUri!.AbsolutePath.TrimStart('/')}";
			Sent.Add(key);
			var response = Bodies.TryGetValue(key, out var body)
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

	private const string Gallery = "api/profile/Gandalf/gallery";

	private const string TwoImages = """
		[{"assetId":"A1","fileName":"one.png","url":"/files/A1","caption":null,"order":0,"isIcon":true,"official":true},
		 {"assetId":"A2","fileName":"two.png","url":"/files/A2","caption":null,"order":1,"isIcon":false,"portraitPending":true}]
		""";

	public GalleryModerationViewTests()
	{
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance))
			.AddSingleton(sp => new GalleryService(sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<CharacterDirectoryService>()))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
		_api.Bodies[$"GET {Gallery}"] = TwoImages;
	}

	private void Policy(bool approval = false, bool staffOnly = false, bool staff = false) =>
		_api.Bodies["GET api/gallery/policy"] = $$"""
			{"consentText":null,"consentVersion":null,"portraitApproval":{{Json(approval)}},"staffOnly":{{Json(staffOnly)}},
			 "maxImages":50,"maxBytes":null,"officialExempt":false,"isStaff":{{Json(staff)}}}
			""";

	private static string Json(bool value) => value ? "true" : "false";

	private IRenderedComponent<CharacterGalleryWidget> Widget()
	{
		var cut = Render<CharacterGalleryWidget>(p => p.Add(w => w.CharacterName, "Gandalf").Add(w => w.CanEdit, true));
		cut.WaitForAssertion(() => cut.Find("button.gallery-hero"), TimeSpan.FromSeconds(5));
		return cut;
	}

	/// <summary>Opens the viewer on the second image, the one waiting to be the portrait.</summary>
	private static async Task OpenWaiting(IRenderedComponent<CharacterGalleryWidget> cut)
	{
		await cut.Find("button.gallery-small-tile").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".kit-viewer-actions"), TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task TheStaffLock_HidesAPlayersControls()
	{
		Policy(staffOnly: true);
		var cut = Widget();

		await Assert.That(cut.FindAll("button.gallery-add")).IsEmpty();
	}

	[Test]
	public async Task TheStaffLock_LeavesStaffTheirControls()
	{
		Policy(staffOnly: true, staff: true);
		var cut = Widget();

		cut.WaitForAssertion(() => cut.Find("button.gallery-add"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("button.gallery-add")).IsNotEmpty();
	}

	[Test]
	public async Task WithApproval_APlayerSeesTheirRequestWaiting_AndCanWithdrawIt()
	{
		Policy(approval: true);
		var cut = Widget();
		await OpenWaiting(cut);

		await Assert.That(cut.Find(".gallery-portrait-waiting").TextContent).Contains("NavGalleryPortraitWaiting");
		await Assert.That(cut.FindAll("button.gallery-portrait-approve")).IsEmpty();
		await Assert.That(cut.Find("button.gallery-portrait-withdraw").TextContent).Contains("NavGalleryWithdrawPortrait");
	}

	[Test]
	public async Task WithApproval_StaffApproveTheWaitingImage()
	{
		Policy(approval: true, staff: true);
		_api.Bodies[$"POST {Gallery}/A2/portrait"] = TwoImages;
		var cut = Widget();
		await OpenWaiting(cut);

		await cut.Find("button.gallery-portrait-approve").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (!_api.Sent.Contains($"POST {Gallery}/A2/portrait")) throw new InvalidOperationException("not sent");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("button.gallery-portrait-decline")).IsNotEmpty();
	}

	[Test]
	public async Task ThePortraitApprovalsPage_ListsRequests_AndApprovingOneTakesItOff()
	{
		Policy(approval: true, staff: true);
		_api.Bodies["GET api/gallery/portraits"] = """
			[{"character":"Gandalf","objid":"#5:1","submitted":{"assetId":"A2","fileName":"two.png","url":"/files/A2","caption":null,"order":1,"isIcon":false,"portraitPending":true},
			  "current":{"assetId":"A1","fileName":"one.png","url":"/files/A1","caption":null,"order":0,"isIcon":true}}]
			""";
		_api.Bodies[$"POST {Gallery}/A2/portrait"] = TwoImages;
		var auth = AddAuthorization();
		auth.SetAuthorized("staff");
		auth.SetPolicies("media.admin");

		var cut = Render<PortraitApprovals>();
		cut.WaitForAssertion(() => cut.Find(".adm-portraits-card"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".adm-portraits-card").TextContent).Contains("Gandalf");

		await cut.Find("button.adm-portraits-approve").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".kit-empty"), TimeSpan.FromSeconds(5));
		await Assert.That(_api.Sent).Contains($"POST {Gallery}/A2/portrait");
	}
}
