using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Server.Controllers;

[NotInParallel]
public class WikiLandingFreshnessTests
{
	[ClassDataSource<WikiLandingFreshnessFactory>(Shared = SharedType.PerTestSession)]
	public required WikiLandingFreshnessFactory Factory { get; init; }
	private const string SelectedTitle = "SECRET_FRENCH_TITLE_BEFORE_AWAIT";
	private const string SelectedBody = "SECRET_FRENCH_BODY_BEFORE_AWAIT";

	private static JsonElement? Landing(JsonElement json)
	{
		foreach (var property in json.EnumerateObject())
			if (property.Name.Equals("LandingPage", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind != JsonValueKind.Null)
				return property.Value;
		return null;
	}
	private static string? Text(JsonElement json, string key)
		=> json.EnumerateObject().Single(property => property.Name.Equals(key, StringComparison.OrdinalIgnoreCase)).Value.GetString();
	private static bool HasLocaleMetadata(JsonElement json) => json.ValueKind switch
	{
		JsonValueKind.Object => json.EnumerateObject().Any(property => property.Name.Equals("Locale", StringComparison.OrdinalIgnoreCase)
			|| property.Name.Equals("RequestedLocale", StringComparison.OrdinalIgnoreCase)
			|| property.Name.Equals("AvailableLocales", StringComparison.OrdinalIgnoreCase) || HasLocaleMetadata(property.Value)),
		JsonValueKind.Array => json.EnumerateArray().Any(HasLocaleMetadata),
		_ => false
	};
	private static string Path(LandingNativePackage package, bool article, string locale)
		=> article
			? "api/wiki/ns/main/" + Uri.EscapeDataString(package.Page.Slug) + "?expectedPageId=" + Uri.EscapeDataString(package.Page.Id) + "&lang=" + Uri.EscapeDataString(locale)
			: "api/wiki/navigation?collection=" + Uri.EscapeDataString("package:" + package.Manifest.Name) + "&lang=" + Uri.EscapeDataString(locale);

	private static async Task<LandingSelected> CapturedAsync(LandingLocalizationHold hold, Task<HttpResponseMessage> response)
	{
		var first = await Task.WhenAny(hold.Captured.Task, response).WaitAsync(TimeSpan.FromSeconds(30));
		if (first == response && !hold.Captured.Task.IsCompleted)
			throw new InvalidOperationException("The actual endpoint completed without the required targeted localization preparation.");
		return await hold.Captured.Task;
	}

	private static async Task AssertWithheldAsync(HttpResponseMessage response, LandingNativePackage package, bool article)
	{
		var body = await response.Content.ReadAsStringAsync();
		await Assert.That(response.StatusCode).IsEqualTo(article ? HttpStatusCode.NotFound : HttpStatusCode.OK);
		if (!article)
		{
			using var json = JsonDocument.Parse(body);
			await Assert.That(Landing(json.RootElement)).IsNull();
		}
		if (body.Length > 0)
		{
			using var json = JsonDocument.Parse(body);
			await Assert.That(HasLocaleMetadata(json.RootElement)).IsFalse();
		}
		foreach (var secret in new[] { package.Page.Id, package.Page.Slug, "SECRET_SOURCE_TITLE_BEFORE_AWAIT", "SECRET_SOURCE_BODY_BEFORE_AWAIT",
			SelectedTitle, SelectedBody, "SECRET_CHANGED_TRANSLATION", "SECRET_REPLACEMENT_TITLE", "SECRET_NEW_PREFERRED_TITLE", "SECRET_NEW_SOURCE_TITLE" })
			await Assert.That(body).DoesNotContain(secret);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task UnchangedRealPreparationReturnsItsAuthorizedSelectionExactlyOnce(bool article)
	{
		var reader = await LandingFreshNative.ReaderAsync(Factory, drafts: false);
		LandingNativePackage? package = null;
		LandingLocalizationHold? hold = null;
		Task<HttpResponseMessage>? request = null;
		try
		{
			package = await LandingFreshNative.InstallAsync(Factory);
			(await LandingFreshNative.Wiki(Factory).UpsertTranslationAsync(package.Page.Id, "fr", SelectedTitle, SelectedBody, "#1", null, true, null)).Expect<WikiTranslation>();
			hold = Factory.Services.GetRequiredService<LandingHoldRegistry>().Arm(package.Page.Id);
			using var client = LandingFreshNative.Client(Factory, reader.Session);
			request = client.GetAsync(Path(package, article, "fr"));
			var selected = await CapturedAsync(hold, request);
			await Assert.That(selected.Page.Title).IsEqualTo(SelectedTitle);
			await Assert.That(selected.Page.Locale).IsEqualTo("fr");
			hold.Release.TrySetResult();
			using var response = await request;
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
			await Assert.That(response.Headers.CacheControl?.NoStore == true).IsTrue();
			using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
			var result = article ? json.RootElement : Landing(json.RootElement) ?? throw new InvalidOperationException("Unchanged native landing was withheld.");
			await Assert.That(Text(result, article ? "Id" : "PageId")).IsEqualTo(package.Page.Id);
			await Assert.That(Text(result, "Title")).IsEqualTo(SelectedTitle);
			await Assert.That(Text(result, "Locale")).IsEqualTo("fr");
			await Assert.That(hold.Calls).IsEqualTo(1);
			await Assert.That(ReferenceEquals(selected.Context, hold.ReturnedContext)).IsTrue();
			var resolverType = typeof(SharpMUSH.Server.Controllers.WikiController).Assembly.GetType("SharpMUSH.Server.Services.WikiLandingReaderResolver")
				?? throw new InvalidOperationException("The selected fresh-reader resolver has no production registration type.");
			using var firstScope = Factory.Services.CreateScope();
			using var secondScope = Factory.Services.CreateScope();
			var firstResolver = firstScope.ServiceProvider.GetRequiredService(resolverType);
			await Assert.That(ReferenceEquals(firstResolver, firstScope.ServiceProvider.GetRequiredService(resolverType))).IsTrue();
			await Assert.That(ReferenceEquals(firstResolver, secondScope.ServiceProvider.GetRequiredService(resolverType))).IsFalse();
		}
		finally
		{
			hold?.Release.TrySetResult();
			try { if (request is not null) (await request).Dispose(); }
			finally
			{
				if (package is not null) Factory.Services.GetRequiredService<LandingHoldRegistry>().Remove(package.Page.Id);
				await LandingFreshNative.CleanupAsync(Factory, package, reader);
			}
		}
	}

	[Test]
	[Arguments("grant-read", false)]
	[Arguments("grant-read", true)]
	[Arguments("requirement", false)]
	[Arguments("requirement", true)]
	[Arguments("draft-grant", false)]
	[Arguments("draft-grant", true)]
	[Arguments("translation-unpublish", false)]
	[Arguments("translation-unpublish", true)]
	[Arguments("translation-delete", false)]
	[Arguments("translation-delete", true)]
	[Arguments("translation-recreate", false)]
	[Arguments("translation-recreate", true)]
	[Arguments("translation-title", false)]
	[Arguments("translation-title", true)]
	[Arguments("translation-revision", false)]
	[Arguments("translation-revision", true)]
	[Arguments("preferred-candidate", false)]
	[Arguments("preferred-candidate", true)]
	[Arguments("source-fallback", false)]
	[Arguments("source-fallback", true)]
	[Arguments("source-title", false)]
	[Arguments("source-title", true)]
	[Arguments("source-body", false)]
	[Arguments("source-body", true)]
	[Arguments("source-publication", false)]
	[Arguments("source-publication", true)]
	[Arguments("default-locale", false)]
	[Arguments("default-locale", true)]
	[Arguments("source-replace", false)]
	[Arguments("source-replace", true)]
	[Arguments("draft-eligibility-gain", false)]
	[Arguments("draft-eligibility-gain", true)]
	[Arguments("ownership-loss", false)]
	[Arguments("binding-clear", false)]
	[Arguments("character-unlink", false)]
	[Arguments("character-unlink", true)]
	[Arguments("account-disable", false)]
	[Arguments("account-disable", true)]
	public async Task NativeMutationDuringOneHeldLocalizationWithholdsOldSelectionWithoutRetry(string change, bool article)
	{
		var reader = await LandingFreshNative.ReaderAsync(Factory, drafts: change == "draft-grant");
		LandingNativePackage? package = null;
		LandingLocalizationHold? hold = null;
		Task<HttpResponseMessage>? request = null;
		var writer = Factory.Services.GetRequiredService<IConfigOptionWriter>();
		var defaultLocale = (await writer.CurrentAsync()).Wiki.DefaultLocale;
		try
		{
			package = await LandingFreshNative.InstallAsync(Factory);
			var wiki = LandingFreshNative.Wiki(Factory);
			WikiTranslation? translation = null;
			if (change != "source-fallback")
				translation = (await wiki.UpsertTranslationAsync(package.Page.Id, "fr", SelectedTitle, SelectedBody, "#1", null,
					change is not ("draft-grant" or "draft-eligibility-gain"), null)).Expect<WikiTranslation>();
			var locale = change == "preferred-candidate" ? "fr-CA" : change == "default-locale" ? "ja" : "fr";
			hold = Factory.Services.GetRequiredService<LandingHoldRegistry>().Arm(package.Page.Id);
			using var client = LandingFreshNative.Client(Factory, reader.Session);
			request = client.GetAsync(Path(package, article, locale));
			var selected = await CapturedAsync(hold, request);
			await Assert.That(selected.Page.Page.Id).IsEqualTo(package.Page.Id);
			var sourceSelected = change is "source-fallback" or "default-locale" or "draft-eligibility-gain";
			await Assert.That(selected.Page.Locale).IsEqualTo(sourceSelected ? "en" : "fr");
			await Assert.That(selected.Page.Title).IsEqualTo(sourceSelected ? package.Page.Title : SelectedTitle);
			await MutateAsync(change, package, reader, translation);
			if (change is "ownership-loss" or "binding-clear" or "character-unlink" or "account-disable")
			{
				// The source and served translation survive unchanged: only binding/ownership or the
				// already-authenticated reader's native authority changed while this one request waited.
				var live = (await wiki.GetByIdAsync(package.Page.Id)).Expect<WikiPage>();
				await Assert.That(JsonSerializer.Serialize(live)).IsEqualTo(JsonSerializer.Serialize(package.Page));
				await Assert.That(live.Published).IsTrue();
				await Assert.That((await wiki.GetTranslationAsync(package.Page.Id, "fr")).Expect<WikiTranslation>()).IsEqualTo(translation);
				await Assert.That(selected.Context.User.Identity?.IsAuthenticated == true).IsTrue();
				if (change is "ownership-loss" or "binding-clear")
				{
					var held = await LandingFreshNative.HeldAsync(Factory, reader);
					await Assert.That(held.Contains(PortalPermission.WikiRead)).IsTrue();
					await Assert.That(await Factory.Services.GetRequiredService<IWikiAccessService>().CanSeeAsync(
						WikiReader.From(held, reader.Character.Object.DBRef.ToString()), live)).IsTrue();
				}
			}
			// The forwarding decorator still returns the same selected old row, rather than manufacturing
			// an empty localization result or fetching a replacement to make the privacy assertion pass.
			await Assert.That(selected.Page.Title).IsEqualTo(sourceSelected ? package.Page.Title : SelectedTitle);
			if (change is "draft-grant" or "draft-eligibility-gain")
			{
				var live = (await wiki.GetByIdAsync(package.Page.Id)).Expect<WikiPage>();
				await Assert.That(live.Published).IsTrue();
				var current = await LandingFreshNative.HeldAsync(Factory, reader);
				await Assert.That(current.Contains(PortalPermission.WikiRead)).IsTrue();
				await Assert.That(current.Contains(PortalPermission.WikiDrafts)).IsEqualTo(change == "draft-eligibility-gain");
				await Assert.That(await Factory.Services.GetRequiredService<IWikiAccessService>().CanSeeAsync(
					WikiReader.From(current, reader.Character.Object.DBRef.ToString()), live)).IsTrue();
			}
			hold.Release.TrySetResult();
			using var response = await request;
			await AssertWithheldAsync(response, package, article);
			await Assert.That(hold.Calls).IsEqualTo(1);
			await Assert.That(ReferenceEquals(selected.Context, hold.ReturnedContext)).IsTrue();
		}
		finally
		{
			hold?.Release.TrySetResult();
			try { if (request is not null) (await request).Dispose(); }
			finally
			{
				if (package is not null) Factory.Services.GetRequiredService<LandingHoldRegistry>().Remove(package.Page.Id);
				if (change == "default-locale") (await writer.SetAsync(nameof(WikiOptions.DefaultLocale), defaultLocale)).Expect<SharpMUSHOptions>();
				await LandingFreshNative.CleanupAsync(Factory, package, reader);
			}
		}
	}

	private async Task MutateAsync(string change, LandingNativePackage package, LandingNativeReader reader, WikiTranslation? original)
	{
		var wiki = LandingFreshNative.Wiki(Factory);
		var roles = LandingFreshNative.Roles(Factory);
		switch (change)
		{
			case "grant-read":
				await roles.SetAccountOverrideAsync(reader.AccountId, PortalPermission.WikiRead, PermissionState.Deny);
				await Assert.That((await LandingFreshNative.HeldAsync(Factory, reader)).Contains(PortalPermission.WikiRead)).IsFalse();
				break;
			case "requirement":
				(await wiki.SetRequirementsAsync(WikiRuleTarget.ForPage(package.Page.Id),
					new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = [PortalPermission.WikiEdit] }, "#1")).Expect<None>();
				await Assert.That((await wiki.GetRequirementsAsync()).For(WikiRuleTarget.ForPage(package.Page.Id))!.For(WikiAction.Read)).IsEquivalentTo(new[] { PortalPermission.WikiEdit });
				break;
			case "draft-grant":
			case "draft-eligibility-gain":
				await roles.SetAccountOverrideAsync(reader.AccountId, PortalPermission.WikiDrafts,
					change == "draft-grant" ? PermissionState.Deny : PermissionState.Allow);
				break;
			case "translation-delete":
				(await wiki.DeleteTranslationAsync(package.Page.Id, "fr", "#1")).Expect<None>();
				await Assert.That(await wiki.GetTranslationAsync(package.Page.Id, "fr") is NotFound).IsTrue();
				break;
			case "translation-recreate":
				(await wiki.DeleteTranslationAsync(package.Page.Id, "fr", "#1")).Expect<None>();
				var replacement = (await wiki.UpsertTranslationAsync(package.Page.Id, "fr", "SECRET_CHANGED_TRANSLATION", "New incarnation body", "#1", null, true, null)).Expect<WikiTranslation>();
				await Assert.That(replacement.RevisionNumber).IsEqualTo(original!.RevisionNumber);
				await Assert.That(replacement.Title == original.Title).IsFalse();
				break;
			case "translation-title":
			case "translation-revision":
			case "translation-unpublish":
				var rewritten = (await wiki.UpsertTranslationAsync(package.Page.Id, "fr", change == "translation-title" ? "SECRET_CHANGED_TRANSLATION" : SelectedTitle,
					"Revised translation body", "#1", null, change != "translation-unpublish", original!.RevisionNumber)).Expect<WikiTranslation>();
				await Assert.That(rewritten.RevisionNumber).IsEqualTo(original.RevisionNumber + 1);
				await Assert.That(rewritten.Published).IsEqualTo(change != "translation-unpublish");
				break;
			case "preferred-candidate":
				(await wiki.UpsertTranslationAsync(package.Page.Id, "fr-CA", "SECRET_NEW_PREFERRED_TITLE", "Preferred body", "#1", null, true, null)).Expect<WikiTranslation>();
				await Assert.That((await wiki.GetTranslationAsync(package.Page.Id, "fr")).Expect<WikiTranslation>()).IsEqualTo(original);
				await Assert.That((await wiki.GetTranslationsAsync(package.Page.Id)).Select(row => row.Locale)).IsEquivalentTo(new[] { "fr", "fr-CA" });
				break;
			case "source-fallback":
				(await wiki.UpsertTranslationAsync(package.Page.Id, "fr", "SECRET_NEW_PREFERRED_TITLE", "New translated body", "#1", null, true, null)).Expect<WikiTranslation>();
				await Assert.That((await wiki.GetTranslationsAsync(package.Page.Id)).Single().Locale).IsEqualTo("fr");
				break;
			case "source-title":
				await Assert.That((await wiki.SetTitleAsync(package.Page.Id, "SECRET_NEW_SOURCE_TITLE")).Expect<WikiPage>().Title).IsEqualTo("SECRET_NEW_SOURCE_TITLE");
				break;
			case "source-body":
				await Assert.That((await wiki.UpdateAsync(package.Page.Id, "Changed source body", "#1")).Expect<WikiPage>().RevisionNumber).IsEqualTo(package.Page.RevisionNumber + 1);
				break;
			case "source-publication":
				await Assert.That((await wiki.SetMetadataAsync(package.Page.Id, package.Page.Categories, false)).Expect<WikiPage>().Published).IsFalse();
				break;
			case "default-locale":
				var options = (await Factory.Services.GetRequiredService<IConfigOptionWriter>().SetAsync(nameof(WikiOptions.DefaultLocale), "fr")).Expect<SharpMUSHOptions>();
				await Assert.That(options.Wiki.DefaultLocale).IsEqualTo("fr");
				await Assert.That(Factory.Services.GetRequiredService<IWikiLocalizationService>().DefaultLocale).IsEqualTo("fr");
				break;
			case "source-replace":
				(await wiki.DeleteAsync(package.Page.Id, "#1")).Expect<None>();
				var page = (await wiki.CreateAsync("SECRET_REPLACEMENT_TITLE", "Replacement source body", "#1", sourceLocale: "en", slug: package.Page.Slug)).Expect<WikiPage>();
				await Assert.That(page.Id == package.Page.Id).IsFalse();
				break;
			case "ownership-loss":
				{
					var registry = (IPackageRegistryService)LandingFreshNative.Database(Factory);
					var installed = (await registry.GetInstalledPackageAsync(package.Manifest.Name)).Expect<InstalledPackageRecord>();
					await Assert.That(installed.WikiLandingPage?.PageId).IsEqualTo(package.Page.Id);
					var owner = (await registry.GetPackageWikiPageAsync(package.Manifest.Name, package.Page.Namespace, package.Page.Slug)).Expect<PackageWikiPageRecord>();
					await Assert.That(owner.PageId).IsEqualTo(package.Page.Id);
					await registry.RemovePackageWikiPageAsync(package.Manifest.Name, package.Page.Namespace, package.Page.Slug);
					await Assert.That(await registry.GetPackageWikiPageAsync(package.Manifest.Name, package.Page.Namespace, package.Page.Slug) is NotFound).IsTrue();
					var unchanged = (await registry.GetInstalledPackageAsync(package.Manifest.Name)).Expect<InstalledPackageRecord>();
					await Assert.That(JsonSerializer.Serialize(unchanged)).IsEqualTo(JsonSerializer.Serialize(installed));
					break;
				}
			case "binding-clear":
				{
					var registry = (IPackageRegistryService)LandingFreshNative.Database(Factory);
					var installed = (await registry.GetInstalledPackageAsync(package.Manifest.Name)).Expect<InstalledPackageRecord>();
					await Assert.That(installed.WikiLandingPage?.PageId).IsEqualTo(package.Page.Id);
					var owner = (await registry.GetPackageWikiPageAsync(package.Manifest.Name, package.Page.Namespace, package.Page.Slug)).Expect<PackageWikiPageRecord>();
					await Assert.That(owner.PageId).IsEqualTo(package.Page.Id);
					var cleared = installed with { WikiLandingPage = null };
					await registry.UpsertInstalledPackageAsync(cleared);
					var current = (await registry.GetInstalledPackageAsync(package.Manifest.Name)).Expect<InstalledPackageRecord>();
					await Assert.That(current.WikiLandingPage).IsNull();
					await Assert.That(JsonSerializer.Serialize(current)).IsEqualTo(JsonSerializer.Serialize(cleared));
					var unchangedOwner = (await registry.GetPackageWikiPageAsync(package.Manifest.Name, package.Page.Namespace, package.Page.Slug)).Expect<PackageWikiPageRecord>();
					await Assert.That(JsonSerializer.Serialize(unchangedOwner)).IsEqualTo(JsonSerializer.Serialize(owner));
					break;
				}
			case "character-unlink":
				{
					var accounts = Factory.Services.GetRequiredService<IAccountService>();
					(await accounts.UnlinkCharacterAsync(reader.AccountId, reader.Character.Object.DBRef)).Expect<Success>();
					await Assert.That((await accounts.GetCharactersAsync(reader.AccountId))
						.Any(character => character.Object.DBRef == reader.Character.Object.DBRef)).IsFalse();
					var account = await accounts.GetByIdAsync(reader.AccountId) ?? throw new InvalidOperationException("Native reader account disappeared.");
					await Assert.That(account.IsActive).IsTrue();
					var character = (await LandingFreshNative.Database(Factory).GetObjectNodeAsync(reader.Character.Object.DBRef))
						.Expect<SharpMUSH.Library.Models.SharpPlayer>();
					await Assert.That(character.Object.DBRef).IsEqualTo(reader.Character.Object.DBRef);
					await Assert.That((await LandingFreshNative.HeldAsync(Factory, reader)).Contains(PortalPermission.WikiRead)).IsFalse();
					break;
				}
			case "account-disable":
				{
					var accounts = Factory.Services.GetRequiredService<IAccountService>();
					(await accounts.DisableAccountAsync(reader.AccountId)).Expect<Success>();
					var account = await accounts.GetByIdAsync(reader.AccountId) ?? throw new InvalidOperationException("Native reader account disappeared.");
					await Assert.That(account.IsActive).IsFalse();
					await Assert.That((await accounts.GetCharactersAsync(reader.AccountId))
						.Any(character => character.Object.DBRef == reader.Character.Object.DBRef)).IsTrue();
					await Assert.That((await LandingFreshNative.HeldAsync(Factory, reader)).Contains(PortalPermission.WikiRead)).IsFalse();
					break;
				}
			default: throw new ArgumentOutOfRangeException(nameof(change));
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task PrimedDelegatedMemoCannotPreserveDraftTranslationAfterOnlyDraftGrantIsRevoked(bool article)
	{
		var reader = await LandingFreshNative.ReaderAsync(Factory, drafts: true);
		LandingNativePackage? package = null;
		LandingLocalizationHold? hold = null;
		Task<HttpResponseMessage>? request = null;
		try
		{
			package = await LandingFreshNative.InstallAsync(Factory);
			(await LandingFreshNative.Wiki(Factory).UpsertTranslationAsync(package.Page.Id, "fr", SelectedTitle, SelectedBody, "#1", null, false, null)).Expect<WikiTranslation>();
			var token = await LandingFreshNative.OAuthAsync(Factory, reader);
			hold = Factory.Services.GetRequiredService<LandingHoldRegistry>().Arm(package.Page.Id, delegated: true);
			using var client = LandingFreshNative.Client(Factory, token);
			request = client.GetAsync(Path(package, article, "fr"));
			var selected = await CapturedAsync(hold, request);
			await Assert.That(selected.App!.Character).IsEqualTo(reader.Character.Object.DBRef);
			await Assert.That(selected.Page.Title).IsEqualTo(SelectedTitle);
			await Assert.That(selected.Page.Published).IsFalse();
			await Assert.That(selected.IncludeDrafts).IsTrue();
			await Assert.That(selected.PrimedMemo!.Contains(PortalPermission.WikiDrafts)).IsTrue();
			await LandingFreshNative.Roles(Factory).SetAccountOverrideAsync(reader.AccountId, PortalPermission.WikiDrafts, PermissionState.Deny);
			var current = await LandingFreshNative.HeldAsync(Factory, reader);
			await Assert.That(current.Contains(PortalPermission.WikiRead)).IsTrue();
			await Assert.That(current.Contains(PortalPermission.WikiDrafts)).IsFalse();
			var source = (await LandingFreshNative.Wiki(Factory).GetByIdAsync(package.Page.Id)).Expect<WikiPage>();
			await Assert.That(source.Published).IsTrue();
			await Assert.That(await Factory.Services.GetRequiredService<IWikiAccessService>().CanSeeAsync(
				WikiReader.From(current, reader.Character.Object.DBRef.ToString()), source)).IsTrue();
			hold.Release.TrySetResult();
			using var response = await request;
			await Assert.That(hold.MemoAfterMutation!.Contains(PortalPermission.WikiDrafts)).IsTrue();
			await Assert.That(ReferenceEquals(hold.MemoAfterMutation, selected.PrimedMemo)).IsTrue();
			await Assert.That(ReferenceEquals(selected.Context, hold.ReturnedContext)).IsTrue();
			await AssertWithheldAsync(response, package, article);
			await Assert.That(hold.Calls).IsEqualTo(1);
		}
		finally
		{
			hold?.Release.TrySetResult();
			try { if (request is not null) (await request).Dispose(); }
			finally
			{
				if (package is not null) Factory.Services.GetRequiredService<LandingHoldRegistry>().Remove(package.Page.Id);
				await LandingFreshNative.CleanupAsync(Factory, package, reader);
			}
		}
	}
}
