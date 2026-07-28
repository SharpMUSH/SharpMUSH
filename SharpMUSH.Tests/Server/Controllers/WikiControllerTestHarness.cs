using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;
using System.Security.Claims;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// Builds a <see cref="WikiController"/> over in-memory storage for the locale and translation tests.
/// </summary>
/// <remarks>
/// The localization service is real and shares the returned storage instance. A substitute would return
/// nulls for every resolved title and turn each localization assertion green without resolving anything.
/// </remarks>
internal static class WikiControllerTestHarness
{
	public static (WikiController Controller, InMemoryWikiService Storage) Build(
		bool authenticated, params string[] scopes)
	{
		var storage = new InMemoryWikiService(new WikiMarkdigPipeline());
		var monitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		monitor.CurrentValue.Returns(TestSharpMushOptions.Create());
		var localization = new WikiLocalizationService(
			storage, new WikiLocaleResolver(monitor), NullLogger<WikiLocalizationService>.Instance);

		var controller = new WikiController(
			storage,
			localization,
			Substitute.For<IPrerenderCacheService>(),
			NullLogger<WikiController>.Instance);

		// An identity without an authentication type reports IsAuthenticated == false.
		var identity = authenticated
			? new ClaimsIdentity(
				new List<Claim> { new(GameHub.CharacterDbrefClaim, "#42") }
					.Concat(scopes.Select(s => new Claim(PortalPermission.ClaimType, s))),
				"test")
			: new ClaimsIdentity();

		controller.ControllerContext = new ControllerContext
		{
			HttpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(identity)
			}
		};

		return (controller, storage);
	}

	public static (WikiController Controller, InMemoryWikiService Storage) BuildAnonymous() =>
		Build(authenticated: false);

	public static (WikiController Controller, InMemoryWikiService Storage) BuildWithClaims(params string[] scopes) =>
		Build(authenticated: true, scopes);
}
