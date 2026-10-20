using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Components.Onboarding;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Onboarding;

namespace SharpMUSH.Tests.BUnit.Pages;

public class GuidePackageCommandsTests : BunitContext
{
	private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
	}
	private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new("https://example.test/") };
	}
	private IAccountAuthState Configure(Handler handler)
	{
		Services.AddLocalization();
		var auth = Substitute.For<IAccountAuthState>();
		auth.IsLoggedIn.Returns(true);
		auth.AccountSessionToken.Returns("session-a");
		auth.ActiveCharacter.Returns(new AccountAuthService.CharacterSummary(5, 100, "Alex", ""));
		Services.AddSingleton(auth);
		Services.AddSingleton<IHttpClientFactory>(new Factory(handler));
		return auth;
	}
	private static HttpResponseMessage Reply() => new(HttpStatusCode.OK)
	{
		Content = JsonContent.Create(new[] { new GuideCapability("plus-help", "+help", "Game-defined", "https://github.com/SharpMUSH/SharpMUSH-Packages/blob/abc/plus-help/package.yaml", "plus-help", "1.8.0") })
	};

	[Test]
	public async Task BundledCommandsExplainTheirSourceWithoutAnEmptyReferenceLink()
	{
		using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = JsonContent.Create(new[] { new GuideCapability("plus-help", "+help", "Game-defined", null, "plus-help", "1.8.0") })
		}));
		Configure(handler);
		var page = Render<GuidePackageCommands>();
		await page.Find("button").ClickAsync();
		await Assert.That(page.Markup).Contains("Included with this server");
		await Assert.That(page.FindAll("a").Count).IsEqualTo(0);
		await Assert.That(page.Find("code").TextContent).IsEqualTo("+help");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FailedCheckIsNotPresentedAsAnEmptyResult(bool failed)
	{
		using var handler = new Handler(_ => Task.FromResult(failed
			? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<GuideCapability>()) }));
		Configure(handler);
		var page = Render<GuidePackageCommands>();
		await page.Find("button").ClickAsync();
		await Assert.That(page.Find("[role=status]").TextContent.Contains(failed ? "failed" : "No verified", StringComparison.Ordinal)).IsTrue();
		await Assert.That(page.Find("button").HasAttribute("disabled")).IsFalse();
	}

	[Test]
	public async Task SignedOutReadersDoNotRequestCharacterCapabilities()
	{
		using var handler = new Handler(_ => throw new InvalidOperationException("Must not request capabilities without an account"));
		var auth = Configure(handler);
		auth.IsLoggedIn.Returns(false);
		var page = Render<GuidePackageCommands>();
		await Assert.That(page.FindAll(".guide-packages").Count).IsEqualTo(0);
	}

	[Test]
	public async Task ExplicitCheckUsesCurrentSessionAndShowsVersionWithoutSendingCommand()
	{
		var requests = new List<string>();
		using var handler = new Handler(request =>
		{
			requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath} {request.Headers.Authorization?.Parameter}");
			return Task.FromResult(Reply());
		});
		Configure(handler);
		var page = Render<GuidePackageCommands>();
		await Assert.That(requests.Count).IsEqualTo(0);
		await page.Find("button").ClickAsync();
		await Assert.That(page.FindAll("code").Select(node => node.TextContent)).Contains("+help");
		await Assert.That(page.Markup.Contains("1.8.0", StringComparison.Ordinal)).IsTrue();
		await Assert.That(requests).IsEquivalentTo(new[] { "GET /api/onboarding/capabilities session-a" });
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task LateResponseCannotCrossAuthorityChange(bool account)
	{
		var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var handler = new Handler(_ => reply.Task);
		var auth = Configure(handler);
		var page = Render<GuidePackageCommands>();
		var pending = page.Find("button").ClickAsync();
		await page.InvokeAsync(() =>
		{
			if (account)
			{
				auth.AccountSessionToken.Returns("session-b");
				auth.AuthStateChanged += Raise.Event<Action>();
			}
			else
			{
				auth.ActiveCharacter.Returns(new AccountAuthService.CharacterSummary(6, 200, "Blake", ""));
				auth.ActiveCharacterChanged += Raise.Event<Action>();
			}
		});
		reply.SetResult(Reply());
		await pending;
		await Assert.That(page.FindAll("code").Count).IsEqualTo(0);
	}
}
