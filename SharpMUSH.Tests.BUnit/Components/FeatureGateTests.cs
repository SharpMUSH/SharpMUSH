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

	private sealed class SwitchableServerInfo() : ServerInfoService(null!)
	{
		public bool Scenes { get; set; }

		public override Task<bool> HasFeatureAsync(string feature) =>
			Task.FromResult(Scenes && feature == GameFeatures.Scenes);
	}
}
