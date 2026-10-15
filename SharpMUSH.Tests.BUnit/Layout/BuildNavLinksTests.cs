using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>The drawer's Build &amp; manage links follow the game's applications without a reload.</summary>
public class BuildNavLinksTests : BunitContext
{
	[Test]
	public async Task PoseTypes_AppearOnceTheSceneSystemIsTurnedOn()
	{
		Services.AddMudServices();
		JSInterop.Mode = JSRuntimeMode.Loose;
		var auth = AddAuthorization();
		auth.SetAuthorized("staff");
		auth.SetPolicies("layout.admin");
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		List<string> features = [];
		var serverInfo = new StubServerInfoService(guestsEnabled: true, features: features);
		Services.AddSingleton<ServerInfoService>(serverInfo);

		var cut = Render<BuildNavLinks>(p => p.Add(c => c.Group, BuildNavCatalog.Group.Content));
		await Assert.That(cut.FindAll("a[href='/admin/scenes/types']").Count).IsEqualTo(0);

		features.Add("scenes");
		serverInfo.Refresh();

		cut.WaitForState(() => cut.FindAll("a[href='/admin/scenes/types']").Count == 1);
		await Assert.That(cut.FindAll("a[href='/admin/scenes/types']").Count).IsEqualTo(1);
	}
}
