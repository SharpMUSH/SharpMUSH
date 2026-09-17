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
/// The five <c>/api/wiki</c> controllers, built over one storage instance and one principal.
/// They share <c>WikiControllerBase</c>'s visibility rules, so a test that seeds a draft and then
/// asks a listing endpoint about it has to be asking the same caller.
/// </summary>
internal sealed record WikiEndpoints(
	WikiController Pages,
	WikiBrowseController Browse,
	WikiRevisionsController Revisions,
	WikiTranslationsController Translations,
	WikiAdminController Admin);

/// <summary>
/// Builds a <see cref="WikiEndpoints"/> set over in-memory storage.
/// </summary>
/// <remarks>
/// The localization service is real and shares the returned storage instance. A substitute would return
/// nulls for every resolved title and turn each localization assertion green without resolving anything.
/// </remarks>
internal static class WikiControllerTestHarness
{
	public static (WikiEndpoints Wiki, InMemoryWikiService Storage) Build(
		bool authenticated, params string[] scopes) =>
		Build(new InMemoryWikiService(new WikiMarkdigPipeline()), authenticated, "#42", scopes);

	/// <param name="callerDbref">
	/// The <c>character_dbref</c> claim. Draft authorship is compared against it verbatim, so a test
	/// about "the author always sees their own draft" turns on this matching the seeded page's author.
	/// </param>
	public static (WikiEndpoints Wiki, InMemoryWikiService Storage) Build(
		InMemoryWikiService storage, bool authenticated, string callerDbref, params string[] scopes)
	{
		var monitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		monitor.CurrentValue.Returns(TestSharpMushOptions.Create());
		var localization = new WikiLocalizationService(
			storage, new WikiLocaleResolver(monitor), NullLogger<WikiLocalizationService>.Instance);
		var cache = Substitute.For<IPrerenderCacheService>();

		var endpoints = new WikiEndpoints(
			new WikiController(storage, localization, cache, NullLogger<WikiController>.Instance),
			new WikiBrowseController(storage, localization, NullLogger<WikiBrowseController>.Instance),
			new WikiRevisionsController(storage, localization, cache, NullLogger<WikiRevisionsController>.Instance),
			new WikiTranslationsController(storage, localization, cache, NullLogger<WikiTranslationsController>.Instance),
			new WikiAdminController(storage, localization, cache, NullLogger<WikiAdminController>.Instance));

		// An identity without an authentication type reports IsAuthenticated == false.
		var identity = authenticated
			? new ClaimsIdentity(
				new List<Claim> { new(GameHub.CharacterDbrefClaim, callerDbref) }
					.Concat(scopes.Select(s => new Claim(PortalPermission.ClaimType, s))),
				"test")
			: new ClaimsIdentity();

		// One principal, but a ControllerContext each: every controller needs its own Response for
		// the headers a listing writes, and sharing one would let a test read another's X-Total-Count.
		var user = new ClaimsPrincipal(identity);
		foreach (var controller in Controllers(endpoints))
		{
			controller.ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext { User = user }
			};
		}

		return (endpoints, storage);
	}

	private static IEnumerable<ControllerBase> Controllers(WikiEndpoints wiki) =>
		[wiki.Pages, wiki.Browse, wiki.Revisions, wiki.Translations, wiki.Admin];

	public static (WikiEndpoints Wiki, InMemoryWikiService Storage) BuildAnonymous() =>
		Build(authenticated: false);

	public static (WikiEndpoints Wiki, InMemoryWikiService Storage) BuildWithClaims(params string[] scopes) =>
		Build(authenticated: true, scopes);
}
