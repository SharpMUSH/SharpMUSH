using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Tests.Server.Controllers;

internal sealed record LandingNativeReader(string AccountId, SharpPlayer Character, string Session);
internal sealed record LandingNativePackage(PackageManifest Manifest, WikiPage Page);

internal static class LandingFreshNative
{
	internal static ISharpDatabase Database(ServerWebAppFactory factory) => factory.Services.GetRequiredService<ISharpDatabase>();
	internal static IWikiService Wiki(ServerWebAppFactory factory) => factory.Services.GetRequiredService<IWikiService>();
	internal static IRoleRegistryService Roles(ServerWebAppFactory factory) => factory.Services.GetRequiredService<IRoleRegistryService>();
	internal static IAdministrativeCapabilityService Capabilities(ServerWebAppFactory factory) => factory.Services.GetRequiredService<IAdministrativeCapabilityService>();
	internal static async Task<LandingNativeReader> ReaderAsync(ServerWebAppFactory factory, bool drafts)
	{
		var name = "landingfresh" + Guid.NewGuid().ToString("N")[..12];
		var accounts = factory.Services.GetRequiredService<IAccountService>();
		var account = (await accounts.CreateAccountAsync(name, null, "Fresh-Landing-Account-Password-83!")).Expect<SharpAccount>();
		await Assert.That(Capabilities(factory).GetType()).IsEqualTo(typeof(AdministrativeCapabilityService));
		var id = account.Id ?? throw new InvalidOperationException("Native account has no identity.");
		var db = Database(factory);
		var character = (await db.GetObjectNodeAsync(await db.CreatePlayerAsync(name, "Fresh-Landing-Character-83!", new DBRef(0), new DBRef(0), 0))).Expect<SharpPlayer>();
		await accounts.LinkCharacterAsync(id, character.Object.DBRef);
		await Assert.That((await accounts.GetCharactersAsync(id)).Single().Object.DBRef).IsEqualTo(character.Object.DBRef);
		foreach (var scope in new[] { PortalPermission.WikiRead, PortalPermission.WikiEdit, PortalPermission.WikiDrafts, PortalPermission.WikiAdmin })
			await Roles(factory).SetAccountOverrideAsync(id, scope,
				scope == PortalPermission.WikiRead || scope == PortalPermission.WikiDrafts && drafts ? PermissionState.Allow : PermissionState.Deny);
		var session = await factory.Services.GetRequiredService<IAccountSessionStore>().CreateTokenAsync(id, TimeSpan.FromMinutes(15), "unknown",
			character.Object.Key, character.Object.CreationTime);
		var identity = (await factory.Services.GetRequiredService<IAccountSessionStore>().InspectAsync(session)).Expect<IAccountSessionStore.SessionIdentity>();
		await Assert.That(identity.AccountId).IsEqualTo(id);
		await Assert.That(identity.CharacterKey).IsEqualTo(character.Object.Key);
		await Assert.That(identity.CharacterCreationTime).IsEqualTo(character.Object.CreationTime);
		return new(id, character, session);
	}

	internal static Task<IReadOnlySet<string>> HeldAsync(ServerWebAppFactory factory, LandingNativeReader reader)
		=> Capabilities(factory).GetGrantedScopesAsync(new(reader.AccountId, reader.Character.Object.DBRef, reader.Character.Object.DBRef));

	internal static async Task<LandingNativePackage> InstallAsync(ServerWebAppFactory factory)
	{
		var id = "landing-race-" + Guid.NewGuid().ToString("N")[..12];
		var manifest = new PackageManifestService().ParseManifest($$"""
			format: 1.14
			package: {{id}}
			version: 1.0.0
			locale: en
			wiki_landing_page: main:{{id}}_secret_entry
			wiki:
			  - page: main:{{id}}_secret_entry
			    title: SECRET_SOURCE_TITLE_BEFORE_AWAIT
			    markdown: SECRET_SOURCE_BODY_BEFORE_AWAIT
			  - page: main:{{id}}_reference
			    title: Public reference
			    markdown: Public collection member.
			""").Expect<ParsedPackageManifest>().Manifest;
		(await factory.Services.GetRequiredService<IPackageInstallService>().ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", id + "/", "commit", "main"),
			new Dictionary<string, string>(), []))).Expect<PackageApplyResult>();
		var owner = (await ((IPackageRegistryService)Database(factory)).GetPackageWikiPageAsync(id, "main", manifest.WikiPages[0].Page)).Expect<PackageWikiPageRecord>();
		var page = (await Wiki(factory).GetByIdAsync(owner.PageId)).Expect<WikiPage>();
		await Assert.That(page.Slug).IsEqualTo(manifest.WikiPages[0].Page);
		return new(manifest, page);
	}

	internal static async Task<string> OAuthAsync(ServerWebAppFactory factory, LandingNativeReader reader)
	{
		var oauth = factory.Services.GetRequiredService<IOAuthService>();
		const string redirect = "https://landing-freshness.example.test/callback";
		const string verifier = "a-landing-freshness-verifier-of-at-least-forty-three-characters-83";
		var registered = (await oauth.RegisterClientAsync(new OAuthClientDraft("Landing freshness", null, null, [redirect],
			[DelegatedScopes.Content]), false)).Expect<RegisteredOAuthClient>();
		var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		var authorization = (await oauth.DecideAsync(new("code", registered.Client.Id, redirect, "content", "state", challenge, "S256"),
			reader.AccountId, reader.Character.Object.DBRef, true)).Expect<AuthorizationRedirect>();
		var code = new Uri(authorization.Location).Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
			.Single(pair => pair[0] == "code")[1];
		var tokens = (await oauth.TokenAsync(new Dictionary<string, string>
		{
			["grant_type"] = "authorization_code", ["code"] = Uri.UnescapeDataString(code),
			["redirect_uri"] = redirect, ["client_id"] = registered.Client.Id, ["code_verifier"] = verifier
		}, new ClientCredentials(null, null))).Expect<IssuedTokens>();
		var access = (await oauth.ValidateAccessTokenAsync(tokens.AccessToken)).Expect<DelegatedAccess>();
		await Assert.That(access.Token.Character).IsEqualTo(reader.Character.Object.DBRef);
		await Assert.That(access.Token.Scopes.Contains(DelegatedScopes.Content)).IsTrue();
		return tokens.AccessToken;
	}

	internal static HttpClient Client(ServerWebAppFactory factory, string bearer)
	{
		var client = factory.CreateHttpClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
		return client;
	}

	internal static async Task CleanupAsync(ServerWebAppFactory factory, LandingNativePackage? package, LandingNativeReader reader)
	{
		if (package is not null)
		{
			var registry = (IPackageRegistryService)Database(factory);
			if (await registry.GetInstalledPackageAsync(package.Manifest.Name) is InstalledPackageRecord)
				(await factory.Services.GetRequiredService<IPackageInstallService>().UninstallAsync(package.Manifest.Name)).Expect<Success>();
			foreach (var target in package.Manifest.WikiPages)
				if (await Wiki(factory).GetBySlugAsync(target.Page, target.Namespace) is WikiPage survivor)
					(await Wiki(factory).DeleteAsync(survivor.Id, "#1")).Expect<None>();
		}
		await factory.Services.GetRequiredService<IAccountSessionStore>().RevokeAsync(reader.Session);
	}
}

internal sealed record LandingSelected(LocalizedWikiPage Page, HttpContext Context, DelegatedApp? App,
	IReadOnlySet<string>? PrimedMemo, bool IncludeDrafts);

internal sealed class LandingLocalizationHold(bool delegated)
{
	internal bool Delegated { get; } = delegated;
	internal TaskCompletionSource<LandingSelected> Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	internal int Calls;
	internal IReadOnlySet<string>? MemoAfterMutation { get; set; }
	internal HttpContext? ReturnedContext { get; set; }
}

internal sealed class LandingHoldRegistry
{
	private readonly ConcurrentDictionary<string, LandingLocalizationHold> _holds = new(StringComparer.Ordinal);
	internal LandingLocalizationHold Arm(string id, bool delegated = false)
	{
		var hold = new LandingLocalizationHold(delegated);
		if (!_holds.TryAdd(id, hold)) throw new InvalidOperationException("A localization hold already owns this page.");
		return hold;
	}
	internal bool TryGet(string id, out LandingLocalizationHold? hold) => _holds.TryGetValue(id, out hold);
	internal void Remove(string id) => _holds.TryRemove(id, out _);
}

/// <summary>Forwards to real localization first, then returns that exact old result after the test releases it.</summary>
internal sealed class HeldLandingLocalization(IWikiLocalizationService inner, LandingHoldRegistry holds,
	IHttpContextAccessor contexts) : IWikiLocalizationService
{
	public string DefaultLocale => inner.DefaultLocale;
	public string SourceLocaleOf(WikiPage page) => inner.SourceLocaleOf(page);
	public Task<Found<LocalizedWikiPage>> GetLocalizedBySlugAsync(string slug, WikiNamespace ns, string? locale, bool drafts)
		=> inner.GetLocalizedBySlugAsync(slug, ns, locale, drafts);
	public async Task<LocalizedWikiPage> LocalizeAsync(WikiPage page, string? locale, bool drafts)
	{
		var selected = await inner.LocalizeAsync(page, locale, drafts);
		if (!holds.TryGet(page.Id, out var hold) || hold is null) return selected;
		Interlocked.Increment(ref hold.Calls);
		var context = contexts.HttpContext ?? throw new InvalidOperationException("Real localization ran outside its HTTP request.");
		var app = OAuthBearerAuthenticationHandler.App(context.User);
		IReadOnlySet<string>? memo = null;
		if (hold.Delegated)
		{
			if (app is null) throw new InvalidOperationException("The real OAuth handler did not authenticate the held read.");
			memo = await context.RequestServices.GetRequiredService<DelegatedPermissions>().GrantedAsync(
				context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, app);
		}
		hold.Captured.TrySetResult(new(selected, context, app, memo, drafts));
		await hold.Release.Task;
		hold.ReturnedContext = contexts.HttpContext;
		if (hold.Delegated)
			hold.MemoAfterMutation = await context.RequestServices.GetRequiredService<DelegatedPermissions>()
				.GrantedAsync(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, app!);
		return selected;
	}
	public Task<IReadOnlyList<LocalizedWikiPage>> LocalizeAllAsync(IReadOnlyList<WikiPage> pages, string? locale, bool drafts)
		=> inner.LocalizeAllAsync(pages, locale, drafts);
	public Task<IReadOnlyList<WikiTranslationSummary>> GetVisibleTranslationsAsync(string page, bool drafts) => inner.GetVisibleTranslationsAsync(page, drafts);
	public Task<IReadOnlyList<string>> GetVisibleLocalesAsync(WikiPage page, bool drafts) => inner.GetVisibleLocalesAsync(page, drafts);
	public Task<IReadOnlyDictionary<string, string>> GetCategoryNamesAsync(string? locale, WikiVisibility visibility) => inner.GetCategoryNamesAsync(locale, visibility);
}

public sealed class WikiLandingFreshnessFactory : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
	protected override Action<IServiceCollection>? ConfigureTestServices => services =>
	{
		services.PostConfigure<AuthenticationOptions>(options =>
		{
			options.DefaultScheme = AccountSessionAuthenticationHandler.SchemeName;
			options.DefaultAuthenticateScheme = AccountSessionAuthenticationHandler.SchemeName;
			options.DefaultChallengeScheme = AccountSessionAuthenticationHandler.SchemeName;
		});
		services.AddSingleton<LandingHoldRegistry>();
		var original = services.Single(service => service.ServiceType == typeof(IWikiLocalizationService));
		services.Remove(original);
		services.Add(ServiceDescriptor.Describe(typeof(IWikiLocalizationService), provider =>
		{
			var inner = original.ImplementationInstance as IWikiLocalizationService
				?? (original.ImplementationFactory?.Invoke(provider) as IWikiLocalizationService)
				?? (IWikiLocalizationService)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!);
			if (inner is not WikiLocalizationService) throw new InvalidOperationException("Freshness tests require the actual production localization implementation.");
			return new HeldLandingLocalization(inner, provider.GetRequiredService<LandingHoldRegistry>(), provider.GetRequiredService<IHttpContextAccessor>());
		}, original.Lifetime));
	};
}
