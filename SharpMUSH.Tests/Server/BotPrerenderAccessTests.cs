using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Middleware;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Authentication;
using SharpMUSH.Tests.Wiki;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// A crawler gets a cached prerender only while an anonymous reader may still open the page: a read
/// requirement set after the page was cached (from <c>@wiki/require</c>, which does not clear the cache)
/// takes it out at once.
/// </summary>
public class BotPrerenderAccessTests
{
	[Test]
	public async Task ACachedPageClosedSinceIsNotServed()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var page = (await wiki.CreateAsync("Dragons", "# body", "#1")).Expect<WikiPage>();
		page = (await wiki.SetMetadataAsync(page.Id, [], published: true)).Expect<WikiPage>();
		await wiki.SetRequirementsAsync(WikiRuleTarget.ForPage(page.Id),
			new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = [PortalPermission.WikiAdmin] }, "#1");

		var cache = Substitute.For<IPrerenderCacheService>();
		cache.Get(Arg.Any<string>()).Returns("<html>cached before the requirement</html>");
		var passedOn = false;
		var middleware = new BotPrerenderMiddleware(_ =>
		{
			passedOn = true;
			return Task.CompletedTask;
		}, Services(wiki), cache, NullLogger<BotPrerenderMiddleware>.Instance);

		var context = new DefaultHttpContext();
		context.Items[BotDetectionMiddleware.BotFlagKey] = true;
		context.Request.Path = "/wiki/main/dragons";
		context.Response.Body = new MemoryStream();
		await middleware.InvokeAsync(context);

		await Assert.That(passedOn).IsTrue();
		await Assert.That(context.Response.Body.Length).IsEqualTo(0);
		cache.DidNotReceive().Get(Arg.Any<string>());
	}

	private static IServiceScopeFactory Services(WikiStoreService wiki)
	{
		var monitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		monitor.CurrentValue.Returns(TestSharpMushOptions.Create());
		var services = new ServiceCollection();
		services.AddSingleton<IWikiService>(wiki);
		services.AddSingleton<IWikiLocalizationService>(new WikiLocalizationService(
			wiki, new WikiLocaleResolver(monitor), NullLogger<WikiLocalizationService>.Instance));
		services.AddSingleton<IWikiAccessService>(new WikiAccessService(wiki, InMemoryRoleRegistry.Seeded(), new PermissionResolver()));
		return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
	}
}
