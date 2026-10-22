using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.Shared;
using System.Net;
using System.Text;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// A game renamed with <c>mud_name</c> is renamed in every page title and header: they all name the game
/// through <c>AppTitle</c>, which <see cref="GameNamedLocalizer"/> answers with the server's name.
/// </summary>
public class GameNamedLocalizerTests
{
	private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) =>
			new(handler, disposeHandler: false) { BaseAddress = new Uri("https://localhost/") };
	}

	private static (GameNamedLocalizer Localizer, ServerInfoService ServerInfo) Build(Func<HttpResponseMessage> answer)
	{
		var serverInfo = new ServerInfoService(new SingleClientFactory(new CapturingHttpHandler(answer)));
		var factory = new ServiceCollection().AddSharedResourceLocalization().AddLogging().BuildServiceProvider()
			.GetRequiredService<IStringLocalizerFactory>();
		return (new GameNamedLocalizer(factory, serverInfo), serverInfo);
	}

	private static HttpResponseMessage Named(string name) => new(HttpStatusCode.OK)
	{
		Content = new StringContent($$"""{"guestsEnabled":false,"mudName":"{{name}}"}""", Encoding.UTF8, "application/json")
	};

	[Test]
	public async Task AppTitleIsTheGamesName()
	{
		using var _ = CultureScope.For("en");
		var (loc, serverInfo) = Build(() => Named("Shadows of Ravenmoor"));

		await Assert.That(loc["AppTitle"].Value).IsEqualTo("SharpMUSH")
			.Because("the server has not answered yet");
		await serverInfo.GameNameAsync();

		await Assert.That(loc["AppTitle"].Value).IsEqualTo("Shadows of Ravenmoor");
		await Assert.That(loc["AuthSetupWelcome", loc["AppTitle"]].Value).Contains("Shadows of Ravenmoor");
		await Assert.That(loc["Settings"].Value).IsEqualTo("Settings");
	}

	[Test]
	public async Task AnUnansweredServerLeavesTheResourceValue()
	{
		using var _ = CultureScope.For("en");
		var (loc, serverInfo) = Build(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

		await serverInfo.GameNameAsync();

		await Assert.That(loc["AppTitle"].Value).IsEqualTo("SharpMUSH");
	}
}
