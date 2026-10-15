using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Setup;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// <see cref="FeatureGate"/> is what keeps the portal from linking to an application the game does not
/// have: the content renders only while the server says the game has it.
/// </summary>
public class FeatureGateTests : TrackingBunitContext
{
	private static readonly RenderFragment Link = b => b.AddMarkupContent(0, "<a href=\"/scenes\">Scenes</a>");
	private static readonly RenderFragment Notice = b => b.AddMarkupContent(0, "<p class=\"off\">not enabled</p>");

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task RendersItsContent_OnlyWhileTheGameHasTheApplication(bool scenes)
	{
		Services.AddSingleton<ServerInfoService>(
			new StubServerInfoService(guestsEnabled: true, features: scenes ? [GameFeatures.Scenes] : []));

		var cut = Render<FeatureGate>(p => p
			.Add(x => x.Feature, GameFeatures.Scenes)
			.Add(x => x.ChildContent, Link)
			.Add(x => x.Otherwise, Notice));

		await Assert.That(cut.FindAll("a[href='/scenes']").Count).IsEqualTo(scenes ? 1 : 0);
		await Assert.That(cut.FindAll("p.off").Count).IsEqualTo(scenes ? 0 : 1);
	}

	[Test]
	public async Task FollowsTheGame_WhenTheApplicationsChange()
	{
		var info = new SwitchableServerInfo { Scenes = true };
		Services.AddSingleton<ServerInfoService>(info);

		var cut = Render<FeatureGate>(p => p
			.Add(x => x.Feature, GameFeatures.Scenes)
			.Add(x => x.ChildContent, Link));
		await Assert.That(cut.FindAll("a[href='/scenes']").Count).IsEqualTo(1);

		info.Scenes = false;
		info.Refresh();

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll("a[href='/scenes']").Count != 0)
				throw new InvalidOperationException("the link is still there");
		});
	}

	private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) =>
			new(handler, disposeHandler: false) { BaseAddress = new Uri("https://localhost/") };
	}

	/// <summary>
	/// The server was away when the gate first asked, so it hid the link; once another reader gets an answer
	/// listing the application, the gate shows it without a reload.
	/// </summary>
	[Test]
	public async Task AfterAFailedFirstRead_ShowsTheApplicationOnceTheServerAnswers()
	{
		var answers = new Queue<Func<HttpResponseMessage>>([
			() => new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable),
			() => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
			{
				Content = new StringContent("""{"guestsEnabled":true,"mudName":"Elsewhere","features":["scenes"]}""",
					System.Text.Encoding.UTF8, "application/json")
			}]);
		using var handler = new SharpMUSH.Tests.Shared.CapturingHttpHandler(() => answers.Dequeue()());
		var info = new ServerInfoService(new SingleClientFactory(handler));
		Services.AddSingleton(info);

		var cut = Render<FeatureGate>(p => p
			.Add(x => x.Feature, GameFeatures.Scenes)
			.Add(x => x.ChildContent, Link));
		cut.WaitForAssertion(() => { if (handler.Requests.Count != 1) throw new InvalidOperationException("not asked yet"); });
		await Assert.That(cut.FindAll("a[href='/scenes']").Count).IsEqualTo(0);

		await info.GameNameAsync();

		cut.WaitForAssertion(() => cut.Find("a[href='/scenes']"));
	}

	private sealed class SwitchableServerInfo() : ServerInfoService(null!)
	{
		public bool Scenes { get; set; }

		public override Task<bool> HasFeatureAsync(string feature) =>
			Task.FromResult(Scenes && feature == GameFeatures.Scenes);
	}
}
