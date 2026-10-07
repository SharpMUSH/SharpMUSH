using SharpMUSH.Library;
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
using SharpMUSH.Tests.Authentication;
using System.Security.Claims;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// The six <c>/api/wiki</c> controllers, built over one storage instance and one principal.
/// They share <c>WikiControllerBase</c>'s visibility rules, so a test that seeds a draft and then
/// asks a listing endpoint about it has to be asking the same caller.
/// </summary>
internal sealed record WikiEndpoints(
	WikiController Pages,
	WikiBrowseController Browse,
	WikiRevisionsController Revisions,
	WikiTranslationsController Translations,
	WikiAdminController Admin,
	WikiRequirementsController Requirements);

/// <summary>
/// Builds a <see cref="WikiEndpoints"/> set over in-memory storage.
/// </summary>
/// <remarks>
/// The localization service is real and shares the returned storage instance. A substitute would return
/// nulls for every resolved title and turn each localization assertion green without resolving anything.
/// </remarks>
internal static class WikiControllerTestHarness
{
	public static (WikiEndpoints Wiki, WikiStoreService Storage) Build(
		bool authenticated, params string[] scopes) =>
		Build(InMemoryWikiStore.CreateService(), authenticated, "#42", scopes);

	/// <param name="callerDbref">
	/// The <c>character_dbref</c> claim. Draft authorship is compared against it verbatim, so a test
	/// about "the author always sees their own draft" turns on this matching the seeded page's author.
	/// </param>
	public static (WikiEndpoints Wiki, WikiStoreService Storage) Build(
		WikiStoreService storage, bool authenticated, string callerDbref, params string[] scopes)
	{
		var monitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		monitor.CurrentValue.Returns(TestSharpMushOptions.Create());
		var localization = new WikiLocalizationService(
			storage, new WikiLocaleResolver(monitor), NullLogger<WikiLocalizationService>.Instance);
		var cache = Substitute.For<IPrerenderCacheService>();
		// No engine behind these tests: an editor's "name" is its dbref echoed back, so a test can tell
		// whose name a DTO resolved; no dbref resolves to no name, which the DTOs allow.
		var names = Substitute.For<IWikiNameResolver>();
		names.NameOfAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
			.Returns(call => Task.FromResult(call.Arg<string?>() is { Length: > 0 } dbref ? $"name of {dbref}" : null));

		// The real access service over a role registry seeded as a new world is, so an anonymous caller holds
		// what the everyone role grants (wiki.read) and nothing more.
		var access = new WikiAccessService(storage, InMemoryRoleRegistry.Seeded(), new PermissionResolver(), Substitute.For<IAccountStore>());

		var endpoints = new WikiEndpoints(
			new WikiController(storage, localization, access, cache, names, NullLogger<WikiController>.Instance),
			new WikiBrowseController(storage, localization, access, names, NullLogger<WikiBrowseController>.Instance),
			new WikiRevisionsController(storage, localization, access, cache, names, NullLogger<WikiRevisionsController>.Instance),
			new WikiTranslationsController(storage, localization, access, cache, names, NullLogger<WikiTranslationsController>.Instance),
			new WikiAdminController(storage, localization, access, cache, names, NullLogger<WikiAdminController>.Instance),
			new WikiRequirementsController(storage, localization, access, cache, names, NullLogger<WikiRequirementsController>.Instance));

		// An identity without an authentication type reports IsAuthenticated == false.
		var identity = authenticated
			? new ClaimsIdentity(
				new List<Claim> { new(GameHub.CharacterDbrefClaim, callerDbref) }
					// Every account holds the everyone role, so its claims always carry wiki.read, as the real
					// claims transformation's do.
					.Concat(scopes.Append(PortalPermission.WikiRead).Distinct()
						.Select(s => new Claim(PortalPermission.ClaimType, s))),
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
		[wiki.Pages, wiki.Browse, wiki.Revisions, wiki.Translations, wiki.Admin, wiki.Requirements];

	public static (WikiEndpoints Wiki, WikiStoreService Storage) BuildAnonymous() =>
		Build(authenticated: false);

	public static (WikiEndpoints Wiki, WikiStoreService Storage) BuildWithClaims(params string[] scopes) =>
		Build(authenticated: true, scopes);
}
