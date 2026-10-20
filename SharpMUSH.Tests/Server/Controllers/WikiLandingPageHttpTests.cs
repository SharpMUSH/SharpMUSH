using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>Initial real HTTP/native evidence; same-request final authority/locale races are separate gates.</summary>
public class WikiLandingPageHttpTests
{
	[ClassDataSource<WikiLandingAccountSessionFactory>(Shared = SharedType.PerTestSession)]
	public required WikiLandingAccountSessionFactory Factory { get; init; }

	[Test]
	public async Task CreateReturnsCanonicalLocationThatCanBeRead()
	{
		var reader = await NewReaderAsync();
		var title = "http_create_route_" + Guid.NewGuid().ToString("N");
		var slug = WikiHelpers.Slugify(title);
		const string markdown = "Created through real HTTP; retrieved through its generated canonical Location.";
		try
		{
			await Roles.SetAccountOverrideAsync(reader.AccountId, PortalPermission.WikiCreate, PermissionState.Allow);
			var held = await Factory.Services.GetRequiredService<IAdministrativeCapabilityService>()
				.GetGrantedScopesAsync(new(reader.AccountId, reader.Character.Object.DBRef, reader.Character.Object.DBRef));
			await Assert.That(held.Contains(PortalPermission.WikiCreate)).IsTrue();
			using var client = await ClientAsync(reader);
			using var created = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(client, "api/wiki",
				new SharpMUSH.Library.API.CreatePageRequest(title, markdown, "main"));
			await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
			var location = created.Headers.Location ?? throw new InvalidOperationException("Real create response omitted Location.");
			var absolute = location.IsAbsoluteUri ? location : new Uri(
				client.BaseAddress ?? throw new InvalidOperationException("Real HTTP client has no base address."), location);
			await Assert.That(absolute.PathAndQuery).IsEqualTo("/api/wiki/ns/main/" + Uri.EscapeDataString(slug));
			using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
			var id = Text(json.RootElement, "Id") ?? throw new InvalidOperationException("Real create DTO omitted ID.");
			await Assert.That(Text(json.RootElement, "Title")).IsEqualTo(title);
			await Assert.That(Text(json.RootElement, "MarkdownSource")).IsEqualTo(markdown);
			var stored = (await Wiki.GetBySlugAsync(slug)).Expect<WikiPage>();
			await Assert.That(stored.Id).IsEqualTo(id);
			// Follow the exact emitted URI: a hand-built GET would not test CreatedAtAction/ActionName.
			var followed = await GetAsync(client, location.ToString());
			await Assert.That(followed.Status).IsEqualTo(HttpStatusCode.OK);
			await Assert.That(Text(followed.Json, "Id")).IsEqualTo(id);
			await Assert.That(Text(followed.Json, "Namespace")).IsEqualTo("main");
			await Assert.That(Text(followed.Json, "Slug")).IsEqualTo(slug);
			await Assert.That(Text(followed.Json, "Title")).IsEqualTo(title);
			await Assert.That(Text(followed.Json, "MarkdownSource")).IsEqualTo(markdown);
		}
		finally
		{
			try
			{
				// The POST may commit before URL generation fails, so clean by the independently known slug.
				if (await Wiki.GetBySlugAsync(slug) is WikiPage survivor)
					(await Wiki.DeleteAsync(survivor.Id, "#1")).Expect<None>();
			}
			finally { await CleanupAsync(null, reader); }
		}
	}

	private ISharpDatabase Database => Factory.Services.GetRequiredService<ISharpDatabase>();
	private IPackageRegistryService Registry => (IPackageRegistryService)Database;
	private IPackageInstallService Installer => Factory.Services.GetRequiredService<IPackageInstallService>();
	private IWikiService Wiki => Factory.Services.GetRequiredService<IWikiService>();
	private IAccountService Accounts => Factory.Services.GetRequiredService<IAccountService>();
	private IAccountSessionStore Sessions => Factory.Services.GetRequiredService<IAccountSessionStore>();
	private IRoleRegistryService Roles => Factory.Services.GetRequiredService<IRoleRegistryService>();
	private const string SourceTitle = "PRIVATE_LANDING_SOURCE_TITLE";
	private const string CurrentTitle = "CURRENT_LANDING_TITLE";
	private const string FrenchTitle = "ENTREE_FRANCAISE_PRIVEE";
	private const string SourceBody = "LANDING_BODY_MUST_NOT_ENTER_NAVIGATION";
	private sealed record Reader(string AccountId, SharpPlayer Character, string Token);
	private sealed record Installed(PackageManifest Manifest, PackageWikiPageSpec Target, WikiPage Page);
	private sealed record Response(HttpStatusCode Status, string Body, JsonElement Json, bool NoStore, bool Private);

	private static JsonElement Property(JsonElement json, string name)
		=> json.EnumerateObject().Single(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
	private static JsonElement? OptionalProperty(JsonElement json, string name)
	{
		foreach (var property in json.EnumerateObject())
			if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value;
		return null;
	}

	private static JsonElement? Landing(JsonElement navigation)
		=> OptionalProperty(navigation, "LandingPage") is { ValueKind: not JsonValueKind.Null } landing ? landing : null;
	private static string? Text(JsonElement json, string name) => Property(json, name).GetString();

	private async Task<Response> GetAsync(HttpClient client, string path)
	{
		using var response = await client.GetAsync(path);
		var body = await response.Content.ReadAsStringAsync();
		using var json = body.Length == 0 ? null : JsonDocument.Parse(body);
		return new(response.StatusCode, body, json?.RootElement.Clone() ?? default,
			response.Headers.CacheControl?.NoStore == true, response.Headers.CacheControl?.Private == true);
	}

	private async Task<Reader> NewReaderAsync(string grants = "ordinary")
	{
		var name = "landingreader" + Guid.NewGuid().ToString("N")[..12];
		var account = (await Accounts.CreateAccountAsync(name, null, "Fixture-Landing-HTTP-Password-83!")).Expect<SharpAccount>();
		var accountId = account.Id ?? throw new InvalidOperationException("Native account has no ID.");
		var created = await Database.CreatePlayerAsync(name, "Fixture-Landing-Character-83!", new DBRef(0), new DBRef(0), 0);
		var character = (await Database.GetObjectNodeAsync(created)).Expect<SharpPlayer>();
		await Accounts.LinkCharacterAsync(accountId, character.Object.DBRef);
		await Assert.That((await Accounts.GetCharactersAsync(accountId)).Single().Object.DBRef).IsEqualTo(character.Object.DBRef);
		await Roles.SetAccountOverrideAsync(accountId, PortalPermission.WikiRead, PermissionState.Allow);
		await Roles.SetAccountOverrideAsync(accountId, PortalPermission.WikiEdit, grants is "edit" or "both" ? PermissionState.Allow : PermissionState.Deny);
		await Roles.SetAccountOverrideAsync(accountId, PortalPermission.WikiDrafts, grants is "drafts" or "both" ? PermissionState.Allow : PermissionState.Deny);
		await Roles.SetAccountOverrideAsync(accountId, PortalPermission.WikiAdmin, grants == "admin" ? PermissionState.Allow : PermissionState.Deny);
		var held = await Factory.Services.GetRequiredService<IAdministrativeCapabilityService>()
			.GetGrantedScopesAsync(new(accountId, character.Object.DBRef, character.Object.DBRef));
		await Assert.That(held.Contains(PortalPermission.WikiRead)).IsTrue();
		await Assert.That(held.Contains(PortalPermission.WikiDrafts)).IsEqualTo(grants is "drafts" or "both");
		var token = await Sessions.CreateTokenAsync(accountId, TimeSpan.FromMinutes(15), "unknown",
			character.Object.Key, character.Object.CreationTime);
		var identity = (await Sessions.InspectAsync(token)).Expect<IAccountSessionStore.SessionIdentity>();
		await Assert.That(identity.AccountId).IsEqualTo(accountId);
		await Assert.That(identity.CharacterKey).IsEqualTo(character.Object.Key);
		await Assert.That(identity.CharacterCreationTime).IsEqualTo(character.Object.CreationTime);
		return new(accountId, character, token);
	}

	private async Task<HttpClient> ClientAsync(Reader? reader)
	{
		var client = Factory.CreateHttpClient();
		if (reader is null) return client;
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reader.Token);
		// Real HTTP account resolution proves the token's exact bound character is the acting identity.
		var roster = await GetAsync(client, "api/account/characters");
		await Assert.That(roster.Status).IsEqualTo(HttpStatusCode.OK);
		var acting = roster.Json.EnumerateArray().Single(row => Property(row, "IsActing").GetBoolean());
		await Assert.That(Property(acting, "DbrefNumber").GetInt32()).IsEqualTo(reader.Character.Object.Key);
		await Assert.That(Property(acting, "CreationTime").GetInt64()).IsEqualTo(reader.Character.Object.CreationTime);
		return client;
	}

	private async Task<Installed> InstallAsync(Reader? adoptAsAuthor = null)
	{
		var id = "landing-http-" + Guid.NewGuid().ToString("N")[..12];
		var manifest = new PackageManifestService().ParseManifest($$"""
			format: 1.14
			package: {{id}}
			version: 1.0.0
			locale: en
			wiki_landing_page: main:{{id}}_secret_entrance
			wiki:
			  - page: main:{{id}}_secret_entrance
			    title: {{SourceTitle}}
			    markdown: {{SourceBody}}
			  - page: main:{{id}}_public_reference
			    title: Public reference keeps this collection visible
			    markdown: Unrelated visible reference.
			""").Expect<ParsedPackageManifest>().Manifest;
		var target = manifest.WikiPages[0];
		WikiPage? existing = null;
		if (adoptAsAuthor is not null)
		{
			existing = (await Wiki.CreateAsync(target.Title, target.Markdown!, adoptAsAuthor.Character.Object.DBRef.ToString(),
				sourceLocale: "en", slug: target.Page)).Expect<WikiPage>();
			await Assert.That((await Installer.PlanAsync(manifest)).Wiki!.Single(change => change.Page == target.Page).Action)
				.IsEqualTo(PackageWikiAction.Adopt);
		}
		(await Installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", id + "/", "commit", "main"),
			new Dictionary<string, string>(), []))).Expect<PackageApplyResult>();
		var owner = (await Registry.GetPackageWikiPageAsync(id, target.NamespaceName, target.Page)).Expect<PackageWikiPageRecord>();
		var page = (await Wiki.GetByIdAsync(owner.PageId)).Expect<WikiPage>();
		await Assert.That(page.Namespace).IsEqualTo(target.NamespaceName);
		await Assert.That(page.Slug).IsEqualTo(target.Page);
		if (existing is not null) await Assert.That(page.Id).IsEqualTo(existing.Id);
		var binding = Property(JsonSerializer.SerializeToElement((await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>()), "WikiLandingPage");
		await Assert.That(Text(binding, "PageId")).IsEqualTo(page.Id);
		await Assert.That(Text(binding, "Namespace")).IsEqualTo(target.NamespaceName);
		await Assert.That(Text(binding, "Slug")).IsEqualTo(target.Page);
		return new(manifest, target, page);
	}

	private static string Navigation(Installed installed, string lang = "en")
		=> "api/wiki/navigation?collection=" + Uri.EscapeDataString("package:" + installed.Manifest.Name) + "&lang=" + Uri.EscapeDataString(lang);
	private static string Article(WikiPage page, string? expected, string lang = "en")
		=> "api/wiki/ns/" + Uri.EscapeDataString(page.Namespace) + "/" + Uri.EscapeDataString(page.Slug)
			+ "?lang=" + Uri.EscapeDataString(lang) + (expected is null ? "" : "&expectedPageId=" + Uri.EscapeDataString(expected));

	private static async Task AssertLandingAsync(Response response, WikiPage page, string title, string locale, bool fallback)
	{
		await Assert.That(response.Status).IsEqualTo(HttpStatusCode.OK);
		var landing = Landing(response.Json) ?? throw new InvalidOperationException("Expected the actual installed landing descriptor.");
		await Assert.That(Text(landing, "PageId")).IsEqualTo(page.Id);
		await Assert.That(Text(landing, "Namespace")).IsEqualTo(page.Namespace);
		await Assert.That(Text(landing, "Slug")).IsEqualTo(page.Slug);
		await Assert.That(Text(landing, "Title")).IsEqualTo(title);
		await Assert.That(Text(landing, "Locale")).IsEqualTo(locale);
		await Assert.That(Property(landing, "IsFallback").GetBoolean()).IsEqualTo(fallback);
		await Assert.That(landing.EnumerateObject().Select(property => property.Name.ToLowerInvariant()))
			.IsEquivalentTo(new[] { "pageid", "namespace", "slug", "title", "locale", "isfallback" });
		await Assert.That(response.Body).DoesNotContain(SourceBody);
		await Assert.That(response.NoStore).IsTrue();
	}

	private static async Task AssertPrivateAbsenceAsync(Response response, Installed installed)
	{
		await Assert.That(response.Status).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(Landing(response.Json)).IsNull();
		await Assert.That(response.Body).DoesNotContain(installed.Page.Id);
		await Assert.That(response.Body).DoesNotContain(installed.Page.Slug);
		await Assert.That(response.Body).DoesNotContain(SourceTitle);
		await Assert.That(response.Body).DoesNotContain(FrenchTitle);
		await Assert.That(response.Body).DoesNotContain(SourceBody);
	}

	private async Task CleanupAsync(Installed? installed, Reader? reader)
	{
		if (installed is not null)
		{
			if (await Registry.GetInstalledPackageAsync(installed.Manifest.Name) is InstalledPackageRecord)
				(await Installer.UninstallAsync(installed.Manifest.Name)).Expect<Success>();
			foreach (var target in installed.Manifest.WikiPages)
				if (await Wiki.GetBySlugAsync(target.Page, target.Namespace) is WikiPage survivor)
					(await Wiki.DeleteAsync(survivor.Id, "#1")).Expect<None>();
		}
		if (reader is not null) await Sessions.RevokeAsync(reader.Token);
	}

	private static async Task AssertNondisclosingLandingRefusalAsync(Response response, Installed installed)
	{
		await Assert.That(response.Status).IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That(response.Private).IsTrue();
		await Assert.That(response.NoStore).IsTrue();
		foreach (var secret in new[] { installed.Page.Id, installed.Page.Slug, SourceTitle, FrenchTitle, SourceBody })
			await Assert.That(response.Body).DoesNotContain(secret);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task PresentedUnissuedAppTokenNeverFallsBackToAnonymousForLandingReads(bool navigation)
	{
		// Recognition is prefix-only; validity is decided by the genuine OAuth handler/store.
		// This fixed synthetic value is never issued or persisted by any fixture setup.
		const string unissued = "smat_landing_http_fixed_never_issued_access_token_7c68c7c9459c4b43";
		Installed? installed = null;
		try
		{
			installed = await InstallAsync();
			using var client = await ClientAsync(null);
			// An actual anonymous request succeeds before credentials are presented. A public target
			// makes accidental anonymous fallback observable, rather than relying on private-page denial.
			var anonymous = await GetAsync(client, Article(installed.Page, null));
			await Assert.That(anonymous.Status).IsEqualTo(HttpStatusCode.OK);
			await Assert.That(Text(anonymous.Json, "Id")).IsEqualTo(installed.Page.Id);
			await Assert.That(Text(anonymous.Json, "Title")).IsEqualTo(SourceTitle);
			client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", unissued);
			var refused = await GetAsync(client, navigation ? Navigation(installed) : Article(installed.Page, installed.Page.Id));
			await AssertNondisclosingLandingRefusalAsync(refused, installed);
			await Assert.That(refused.Body).DoesNotContain(unissued);
			client.DefaultRequestHeaders.Authorization = null;
			var ordinary = await GetAsync(client, Article(installed.Page, null));
			await Assert.That(ordinary.Status).IsEqualTo(HttpStatusCode.OK);
			await Assert.That(Text(ordinary.Json, "Id")).IsEqualTo(installed.Page.Id);
		}
		finally { await CleanupAsync(installed, null); }
	}

	[Test]
	[Arguments("empty")]
	[Arguments("whitespace")]
	[Arguments("overlong")]
	[Arguments("control")]
	public async Task PresentMalformedExpectedIdentityIsRefusedWithoutReflectionAndOmittedConstraintStillWorks(string shape)
	{
		var expected = shape switch
		{
			"empty" => string.Empty,
			"whitespace" => " \t ",
			"overlong" => "EXPECTED_PAGE_ID_MUST_NOT_BE_REFLECTED_" + new string('x', 1025),
			"control" => "EXPECTED_PAGE_ID_CONTROL_MUST_NOT_BE_REFLECTED_\u0001",
			_ => throw new ArgumentOutOfRangeException(nameof(shape))
		};
		Installed? installed = null;
		try
		{
			installed = await InstallAsync();
			using var client = await ClientAsync(null);
			var ordinary = await GetAsync(client, Article(installed.Page, null));
			await Assert.That(ordinary.Status).IsEqualTo(HttpStatusCode.OK);
			await Assert.That(Text(ordinary.Json, "Id")).IsEqualTo(installed.Page.Id);
			// Article adds the key whenever expected is non-null, including the empty string.
			// Uri escaping keeps whitespace/control data inside the query, not a request/header seam.
			var refused = await GetAsync(client, Article(installed.Page, expected));
			await AssertNondisclosingLandingRefusalAsync(refused, installed);
			await Assert.That(refused.Body.ToLowerInvariant()).DoesNotContain("expectedpageid");
			await Assert.That(refused.Body).DoesNotContain("EXPECTED_PAGE_ID_MUST_NOT_BE_REFLECTED_");
			await Assert.That(refused.Body).DoesNotContain("EXPECTED_PAGE_ID_CONTROL_MUST_NOT_BE_REFLECTED_");
			var stillOrdinary = await GetAsync(client, Article(installed.Page, null));
			await Assert.That(stillOrdinary.Status).IsEqualTo(HttpStatusCode.OK);
			await Assert.That(Text(stillOrdinary.Json, "Id")).IsEqualTo(installed.Page.Id);
		}
		finally { await CleanupAsync(installed, null); }
	}

	[Test]
	[Arguments(false, "en", "en", false)]
	[Arguments(true, "fr", "fr", false)]
	[Arguments(false, "ja", "en", true)]
	public async Task NativeInstalledLandingUsesCurrentAuthorizedLocalizedTitle(bool authenticated, string lang, string locale, bool fallback)
	{
		Reader? reader = authenticated ? await NewReaderAsync() : null;
		Installed? installed = null;
		try
		{
			installed = await InstallAsync();
			var current = (await Wiki.SetTitleAsync(installed.Page.Id, CurrentTitle)).Expect<WikiPage>();
			(await Wiki.UpsertTranslationAsync(current.Id, "fr", FrenchTitle, "Translated body is not navigation metadata.", "#1", null, true, null)).Expect<WikiTranslation>();
			using var client = await ClientAsync(reader);
			await AssertLandingAsync(await GetAsync(client, Navigation(installed, lang)), current, locale == "fr" ? FrenchTitle : CurrentTitle, locale, fallback);
		}
		finally { await CleanupAsync(installed, reader); }
	}

	[Test]
	[Arguments("unowned")]
	[Arguments("deleted")]
	[Arguments("recreated")]
	[Arguments("restricted")]
	[Arguments("draft")]
	public async Task InvalidOrDeniedTargetHasNoDescriptorWhileItsCollectionRemainsVisible(string state)
	{
		var reader = await NewReaderAsync();
		Installed? installed = null;
		try
		{
			installed = await InstallAsync();
			using var client = await ClientAsync(reader);
			var issued = await GetAsync(client, Navigation(installed));
			if (state == "unowned") await Registry.RemovePackageWikiPageAsync(installed.Manifest.Name, installed.Page.Namespace, installed.Page.Slug);
			else if (state is "deleted" or "recreated")
			{
				(await Wiki.DeleteAsync(installed.Page.Id, "#1")).Expect<None>();
				if (state == "recreated")
				{
					var replacement = (await Wiki.CreateAsync("Unrelated replacement", "Replacement body", "#1", sourceLocale: "en", slug: installed.Page.Slug)).Expect<WikiPage>();
					await Assert.That(replacement.Id == installed.Page.Id).IsFalse();
				}
			}
			else if (state == "restricted")
				(await Wiki.SetRequirementsAsync(WikiRuleTarget.ForPage(installed.Page.Id),
					new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = [PortalPermission.WikiEdit] }, "#1")).Expect<None>();
			else (await Wiki.SetMetadataAsync(installed.Page.Id, installed.Page.Categories, false)).Expect<WikiPage>();
			var denied = await GetAsync(client, Navigation(installed));
			// Run the actual negative request before checking the positive capability, for useful RED evidence.
			await AssertLandingAsync(issued, installed.Page, SourceTitle, "en", false);
			await AssertPrivateAbsenceAsync(denied, installed);
			if (state is "restricted" or "draft")
			{
				var article = await GetAsync(client, Article(installed.Page, installed.Page.Id));
				await Assert.That(article.Status).IsEqualTo(HttpStatusCode.NotFound);
				await Assert.That(article.Body).DoesNotContain(SourceTitle);
				await Assert.That(article.Body).DoesNotContain(SourceBody);
			}
		}
		finally { await CleanupAsync(installed, reader); }
	}

	[Test]
	[Arguments("ordinary", false, false)]
	[Arguments("edit", false, false)]
	[Arguments("drafts", false, true)]
	[Arguments("admin", false, false)]
	[Arguments("both", false, true)]
	[Arguments("ordinary", true, true)]
	public async Task DraftLandingUsesActualSessionDraftGrantOrExactAuthorship(string grants, bool ownDraft, bool visible)
	{
		var reader = await NewReaderAsync(grants);
		Installed? installed = null;
		try
		{
			installed = await InstallAsync(ownDraft ? reader : null);
			(await Wiki.SetMetadataAsync(installed.Page.Id, installed.Page.Categories, false)).Expect<WikiPage>();
			using var client = await ClientAsync(reader);
			var response = await GetAsync(client, Navigation(installed));
			if (visible) await AssertLandingAsync(response, installed.Page, SourceTitle, "en", false);
			else await AssertPrivateAbsenceAsync(response, installed);
		}
		finally { await CleanupAsync(installed, reader); }
	}

	[Test]
	[Arguments("en")]
	[Arguments("fr")]
	public async Task CanonicalArticleHonorsExpectedIdentityAndOldIdentityNeverHealsAReplacement(string lang)
	{
		var reader = await NewReaderAsync();
		Installed? installed = null;
		try
		{
			installed = await InstallAsync();
			(await Wiki.UpsertTranslationAsync(installed.Page.Id, "fr", FrenchTitle, "PRIVATE_TRANSLATED_BODY", "#1", null, true, null)).Expect<WikiTranslation>();
			using var client = await ClientAsync(reader);
			var correct = await GetAsync(client, Article(installed.Page, installed.Page.Id, lang));
			await Assert.That(correct.Status).IsEqualTo(HttpStatusCode.OK);
			await Assert.That(Text(correct.Json, "Id")).IsEqualTo(installed.Page.Id);
			await Assert.That(Text(correct.Json, "Title")).IsEqualTo(lang == "fr" ? FrenchTitle : SourceTitle);
			var unconstrained = await GetAsync(client, Article(installed.Page, null, lang));
			await Assert.That(unconstrained.Status).IsEqualTo(HttpStatusCode.OK);
			var wrong = await GetAsync(client, Article(installed.Page, "wiki_page/999999999", lang));
			(await Wiki.DeleteAsync(installed.Page.Id, "#1")).Expect<None>();
			var replacement = (await Wiki.CreateAsync("REPLACEMENT_PRIVATE_TITLE", "REPLACEMENT_PRIVATE_BODY", "#1", sourceLocale: "en", slug: installed.Page.Slug)).Expect<WikiPage>();
			(await Wiki.UpsertTranslationAsync(replacement.Id, "fr", "REPLACEMENT_PRIVATE_FRENCH", "REPLACEMENT_PRIVATE_FRENCH_BODY", "#1", null, true, null)).Expect<WikiTranslation>();
			await Assert.That(replacement.Id == installed.Page.Id).IsFalse();
			var old = await GetAsync(client, Article(replacement, installed.Page.Id, lang));
			var fresh = await GetAsync(client, Article(replacement, replacement.Id, lang));
			await Assert.That(fresh.Status).IsEqualTo(HttpStatusCode.OK);
			await Assert.That(Text(fresh.Json, "Id")).IsEqualTo(replacement.Id);
			foreach (var withheld in new[] { wrong, old })
			{
				await Assert.That(withheld.Status).IsEqualTo(HttpStatusCode.NotFound);
				foreach (var secret in new[] { SourceTitle, SourceBody, FrenchTitle, "PRIVATE_TRANSLATED_BODY", "REPLACEMENT_PRIVATE_TITLE", "REPLACEMENT_PRIVATE_BODY", "REPLACEMENT_PRIVATE_FRENCH" })
					await Assert.That(withheld.Body).DoesNotContain(secret);
			}
		}
		finally { await CleanupAsync(installed, reader); }
	}

	[Test]
	public async Task SameRealAccountSessionSeesPersistedGrantLossOnSubsequentNavigationAndExactArticleRequests()
	{
		var reader = await NewReaderAsync("edit");
		Installed? installed = null;
		try
		{
			installed = await InstallAsync();
			(await Wiki.SetRequirementsAsync(WikiRuleTarget.ForPage(installed.Page.Id),
				new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = [PortalPermission.WikiEdit] }, "#1")).Expect<None>();
			using var client = await ClientAsync(reader);
			var issued = await GetAsync(client, Navigation(installed));
			var readable = await GetAsync(client, Article(installed.Page, installed.Page.Id));
			await Assert.That(readable.Status).IsEqualTo(HttpStatusCode.OK);
			await Roles.SetAccountOverrideAsync(reader.AccountId, PortalPermission.WikiEdit, PermissionState.Deny);
			var current = await Factory.Services.GetRequiredService<IAdministrativeCapabilityService>()
				.GetGrantedScopesAsync(new(reader.AccountId, reader.Character.Object.DBRef, reader.Character.Object.DBRef));
			await Assert.That(current.Contains(PortalPermission.WikiRead)).IsTrue();
			await Assert.That(current.Contains(PortalPermission.WikiEdit)).IsFalse();
			var denied = await GetAsync(client, Navigation(installed));
			var hidden = await GetAsync(client, Article(installed.Page, installed.Page.Id));
			await AssertLandingAsync(issued, installed.Page, SourceTitle, "en", false);
			await AssertPrivateAbsenceAsync(denied, installed);
			await Assert.That(hidden.Status).IsEqualTo(HttpStatusCode.NotFound);
			await Assert.That(hidden.Body).DoesNotContain(SourceTitle);
			await Assert.That(hidden.Body).DoesNotContain(SourceBody);
		}
		finally { await CleanupAsync(installed, reader); }
	}
}

/// <summary>Existing isolated-host and AccountSession scheme configuration pattern; no auth substitute.</summary>
public sealed class WikiLandingAccountSessionFactory : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
	protected override Action<IServiceCollection>? ConfigureTestServices => services =>
		services.PostConfigure<AuthenticationOptions>(options =>
		{
			options.DefaultScheme = AccountSessionAuthenticationHandler.SchemeName;
			options.DefaultAuthenticateScheme = AccountSessionAuthenticationHandler.SchemeName;
			options.DefaultChallengeScheme = AccountSessionAuthenticationHandler.SchemeName;
		});
}
