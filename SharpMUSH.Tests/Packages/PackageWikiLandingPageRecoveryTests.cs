using System.Text.Json;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>Real installed-row publication is compensated before an isolated native world is reopened.</summary>
public class PackageWikiLandingPageRecoveryTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private sealed class PublicationFailure : Exception { }
	private sealed record BindingValue(string? Namespace, string? Slug, string? PageId);

	private static BindingValue? Binding<T>(T value)
	{
		var json = JsonSerializer.SerializeToElement(value);
		if (!json.TryGetProperty("WikiLandingPage", out var binding) || binding.ValueKind == JsonValueKind.Null) return null;
		static string? Text(JsonElement element, string name)
			=> element.TryGetProperty(name, out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
		return new(Text(binding, "Namespace"), Text(binding, "Slug"), Text(binding, "PageId"));
	}

	private static PackageManifest Manifest(string id, bool upgrade = false) =>
		new PackageManifestService().ParseManifest($$"""
			format: 1.14
			package: {{id}}
			version: {{(upgrade ? "1.1.0" : "1.0.0")}}
			wiki_landing_page: main:{{id}}{{(upgrade ? "_second" : "_entrance")}}
			wiki:
			  - page: main:{{id}}_entrance
			    title: Original Entrance
			    markdown: {{JsonSerializer.Serialize(upgrade ? "Changed during rejected upgrade.\n" : "Original package content.\n")}}
			{{(upgrade ? "  - page: main:" + id + "_second\n    title: Second Entrance\n    markdown: New entrance.\n" : string.Empty)}}
			""").Expect<ParsedPackageManifest>().Manifest;

	private static PackageApplyRequest Request(PackageManifest manifest) => new(
		new PackageApplySource("https://example.invalid/owned-landing-recovery", manifest.Name + "/", "commit-" + manifest.Version, "main"),
		new Dictionary<string, string>(), []);

	private ServerTestWebApplicationBuilderFactory<SharpMUSH.Server.Program> OpenHost(string path) => new(
		WebAppFactoryArg.MySqlTestServer.Instance.GetConnectionString(),
		Path.Join(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"),
		notifier: null, worldPath: path);

	private static LightningDatabase OpenNative(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path }, Substitute.For<IPasswordService>(), relations: null);

	private static string ScratchPath() => Path.Join(Path.GetTempPath(), "landing-recovery-" + Guid.NewGuid().ToString("N"), "world");

	private static void DeleteWorld(string path)
	{
		var parent = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("The owned world has no scratch parent.");
		if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
	}

	private static string RegistryBytes(LightningDatabase database) => database.Store.Read(tx => JsonSerializer.Serialize(new
	{
		Installed = tx.Range(Tables.Pkg, []).Select(entry => Convert.ToHexString(entry.Key) + ":" + Convert.ToHexString(entry.Value)).ToArray(),
		Owned = tx.Range(Tables.PkgWiki, []).Select(entry => Convert.ToHexString(entry.Key) + ":" + Convert.ToHexString(entry.Value)).ToArray(),
		Revisions = tx.Range(Tables.PkgRev, []).Select(entry => Convert.ToHexString(entry.Key) + ":" + Convert.ToHexString(entry.Value)).ToArray(),
		Dependencies = tx.Range(Tables.PkgDep, []).Select(entry => Convert.ToHexString(entry.Key) + ":" + Convert.ToHexString(entry.Value)).ToArray()
	}));

	private static string PageContent(WikiPage page) => JsonSerializer.Serialize(new
	{
		page.Id,
		page.Namespace,
		page.Slug,
		page.Title,
		page.MarkdownSource,
		page.RenderedHtml,
		page.PlainText,
		page.Categories,
		page.Published,
		page.SourceLocale
	});

	private static PackageInstallService Installer(IServiceProvider services, IPackageRegistryService registry)
	{
		var database = services.GetRequiredService<ISharpDatabase>();
		return new PackageInstallService(
			database, database, database, database, registry, (IApplicationRegistryService)database,
			services.GetRequiredService<IPackagePlanService>(),
			services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(),
			services.GetRequiredService<IPackageLifecycleRunner>(),
			services.GetRequiredService<IPluginPackageInstaller>(),
			services.GetRequiredService<IMediator>(),
			services.GetRequiredService<IPackageOperationGate>(),
			services.GetRequiredService<ILockService>(),
			services.GetRequiredService<IPackageDeclarationService>(),
			services.GetRequiredService<IPackageSettingService>(),
			services.GetRequiredService<IPackageWikiService>(),
			services.GetRequiredService<Lazy<IRuntimeRegistrationService>>(),
			services.GetRequiredService<EngineVersion>(),
			services.GetRequiredService<Lazy<IPluginManager>>(), services.GetRequiredService<Lazy<IPluginAdministration>>());
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public Task LateFailureOrCancellationRestoresExactRegistryAfterPublicationAndNativeReopen(bool upgrade, bool cancel)
		=> CheckRecoveryAsync(upgrade, cancel, null);

	[Test]
	[Arguments(PackageConflictResolution.KeepMine, false)]
	[Arguments(PackageConflictResolution.KeepMine, true)]
	[Arguments(PackageConflictResolution.TakeTheirs, false)]
	[Arguments(PackageConflictResolution.TakeTheirs, true)]
	public Task LateContentConflictFailureOrCancellationRestoresStaffContentAfterNativeReopen(PackageConflictResolution resolution, bool cancel)
		=> CheckRecoveryAsync(true, cancel, resolution);

	private async Task CheckRecoveryAsync(bool upgrade, bool cancel, PackageConflictResolution? conflictResolution)
	{
		var path = ScratchPath();
		var id = "landing-recovery-" + Guid.NewGuid().ToString("N")[..12];
		var originalManifest = Manifest(id);
		var desired = Manifest(id, upgrade);
		var targetSlug = desired.WikiLandingPage?.Page ?? throw new InvalidOperationException("The desired fixture has no landing target.");
		var target = desired.WikiPages.Single(page => page.Page == targetSlug);
		string before;
		string? originalPage = null;
		string? originalBody = null;
		string? originalId = null;
		PublicationRegistry boundary;
		ServerTestWebApplicationBuilderFactory<SharpMUSH.Server.Program>? host = null;
		LightningDatabase? reopened = null;
		try
		{
			// This factory owns only this world's host, and does not delete its explicit worldPath on disposal.
			host = OpenHost(path);
			{
				var services = host.Services;
				var database = (LightningDatabase)services.GetRequiredService<ISharpDatabase>();
				var wiki = services.GetRequiredService<IWikiService>();
				if (upgrade)
				{
					(await services.GetRequiredService<IPackageInstallService>().ApplyAsync(originalManifest, Request(originalManifest)))
						.Expect<PackageApplyResult>();
					var page = (await wiki.GetBySlugAsync(id + "_entrance")).Expect<WikiPage>();
					if (conflictResolution is not null)
					{
						// Staff and the package replace the same baseline line differently, forcing a real ModifyModify conflict.
						page = (await wiki.UpdateAsync(page.Id, "Staff content kept after rejected upgrade.\n", "#1", "Staff conflict fixture")).Expect<WikiPage>();
					}
					originalId = page.Id;
					originalPage = PageContent(page);
					originalBody = page.MarkdownSource;
				}

				before = RegistryBytes(database);
				var request = Request(desired);
				if (conflictResolution is { } resolution)
				{
					var plan = await services.GetRequiredService<IPackageInstallService>().PlanAsync(desired);
					var conflict = (plan.Wiki ?? []).Single(change => change.Page == id + "_entrance");
					await Assert.That(conflict.Action).IsEqualTo(PackageWikiAction.Conflict);
					await Assert.That(conflict.Conflict).IsEqualTo(PackageConflictKind.ModifyModify);
					await Assert.That((plan.Wiki ?? []).Count(change => change.Action == PackageWikiAction.Conflict)).IsEqualTo(1);
					request = request with
					{
						ConflictDecisions = [new PackageConflictDecision(conflict.TargetRef, conflict.Page, resolution)]
					};
				}

				using var requestCancellation = new CancellationTokenSource();
				using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
				boundary = new PublicationRegistry(database, wiki, id, target.Page, cancel, requestCancellation.Token,
					conflictResolution is null ? null : originalId);
				var apply = Installer(services, boundary).ApplyAsync(desired, request, requestCancellation.Token);
				try
				{
					await boundary.Published.Task.WaitAsync(deadline.Token);
					if (cancel) requestCancellation.Cancel();
					Exception? caught = null;
					try { await apply.WaitAsync(deadline.Token); }
					catch (Exception error) when (apply.IsCompleted) { caught = error; }
					await Assert.That(cancel ? caught is OperationCanceledException : caught is PublicationFailure).IsTrue();
					await Assert.That(boundary.PublicationCount).IsEqualTo(1);
					await Assert.That(boundary.RevisionCommitCalls).IsEqualTo(0);
					await Assert.That(boundary.RevisionWasAbsent).IsTrue();
					await Assert.That(RegistryBytes(database)).IsEqualTo(before);
					if (upgrade)
					{
						var pageId = originalId ?? throw new InvalidOperationException("The upgrade fixture has no original page ID.");
						await Assert.That(PageContent((await wiki.GetByIdAsync(pageId)).Expect<WikiPage>())).IsEqualTo(originalPage);
					}
					await Assert.That(await wiki.GetBySlugAsync(target.Page) is NotFound).IsTrue();
				}
				finally
				{
					requestCancellation.Cancel();
					// Join the actual apply/recovery before allowing its host/environment to close, including assertion failure.
					try { await apply; }
					catch (Exception) when (apply.IsCompleted) { }
				}
			}
			await host.DisposeAsync();
			host = null;

			// The host has closed its native environment. Only now may another environment open the same path.
			reopened = OpenNative(path);
			{
				await reopened.Migrate();
				await Assert.That(RegistryBytes(reopened)).IsEqualTo(before);
				await Assert.That(await reopened.GetPageBySlugAsync("main", target.Page) is NotFound).IsTrue();
				if (upgrade)
				{
					var pageId = originalId ?? throw new InvalidOperationException("The upgrade fixture has no original page ID.");
					await Assert.That(PageContent((await reopened.GetPageByIdAsync(pageId)).Expect<WikiPage>())).IsEqualTo(originalPage);
				}
			}
			await reopened.DisposeAsync();
			reopened = null;

			// Assert missing future behavior after real fault, compensation and reopen have all been exercised.
			var publication = boundary.Installed ?? throw new InvalidOperationException("No installed row was observed at the native publication boundary.");
			var owner = boundary.Owner ?? throw new InvalidOperationException("No ownership was observed at the native publication boundary.");
			var live = boundary.Page ?? throw new InvalidOperationException("No live target was observed at the native publication boundary.");
			await Assert.That(publication.CurrentRevision).IsEqualTo(upgrade ? 2 : 1);
			await Assert.That(publication.Version).IsEqualTo(desired.Version.ToString());
			await Assert.That(owner.PageId).IsEqualTo(live.Id);
			await Assert.That(owner.Namespace).IsEqualTo("main");
			await Assert.That(owner.Slug).IsEqualTo(target.Page);
			await Assert.That(live.Namespace).IsEqualTo("main");
			await Assert.That(live.Slug).IsEqualTo(target.Page);
			await Assert.That(Binding(publication)).IsEqualTo(new BindingValue("main", target.Page, live.Id));
			if (conflictResolution is { } acceptedResolution)
			{
				var conflicted = boundary.ConflictPage ?? throw new InvalidOperationException("No actual conflicted page was observed at publication.");
				var expectedBody = acceptedResolution == PackageConflictResolution.KeepMine
					? originalBody
					: desired.WikiPages.Single(page => page.Page == id + "_entrance").Markdown;
				await Assert.That(conflicted.Id).IsEqualTo(originalId);
				await Assert.That(conflicted.MarkdownSource).IsEqualTo(expectedBody);
			}
		}
		finally
		{
			// A close failure stops cleanup before directory deletion; no still-open environment is removed.
			if (host is not null) await host.DisposeAsync();
			if (reopened is not null) await reopened.DisposeAsync();
			DeleteWorld(path);
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ActualInstallerBindingOrOmissionSurvivesNativeCloseAndReopen(bool omit)
	{
		var path = ScratchPath();
		var id = "landing-reopen-" + Guid.NewGuid().ToString("N")[..12];
		var manifest = Manifest(id);
		var slug = id + "_entrance";
		string pageId;
		ServerTestWebApplicationBuilderFactory<SharpMUSH.Server.Program>? host = null;
		LightningDatabase? reopened = null;
		try
		{
			host = OpenHost(path);
			{
				var installer = host.Services.GetRequiredService<IPackageInstallService>();
				(await installer.ApplyAsync(manifest, Request(manifest))).Expect<PackageApplyResult>();
				pageId = (await host.Services.GetRequiredService<IWikiService>().GetBySlugAsync(slug)).Expect<WikiPage>().Id;
				if (omit)
				{
					var without = manifest with { Version = new PackageVersion(1, 1, 0), Format = new PackageFormatVersion(1, 4), WikiLandingPage = null };
					(await installer.ApplyAsync(without, Request(without))).Expect<PackageApplyResult>();
				}
			}
			await host.DisposeAsync();
			host = null;

			reopened = OpenNative(path);
			{
				await reopened.Migrate();
				var installed = (await reopened.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
				var owner = (await reopened.GetPackageWikiPageAsync(id, "main", slug)).Expect<PackageWikiPageRecord>();
				var live = (await reopened.GetPageByIdAsync(owner.PageId)).Expect<WikiPage>();
				await Assert.That(owner.PageId).IsEqualTo(pageId);
				await Assert.That(live.Id).IsEqualTo(pageId);
				await Assert.That(live.Namespace).IsEqualTo("main");
				await Assert.That(live.Slug).IsEqualTo(slug);
				var expected = omit ? null : new BindingValue("main", slug, pageId);
				await Assert.That(Binding(installed)).IsEqualTo(expected);
				await Assert.That(Binding((await reopened.GetInstalledPackagesAsync()).Single(package => package.Id == id))).IsEqualTo(expected);
				var revision = (await reopened.GetPackageRevisionAsync(id, installed.CurrentRevision)).Expect<PackageRevisionRecord>();
				var snapshot = JsonSerializer.Deserialize<PackageRevisionSnapshot>(revision.ManifestSnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
					?? throw new InvalidOperationException("The reopened revision has no snapshot.");
				await Assert.That(Binding(snapshot)).IsEqualTo(expected);
			}
		}
		finally
		{
			if (host is not null) await host.DisposeAsync();
			if (reopened is not null) await reopened.DisposeAsync();
			DeleteWorld(path);
		}
	}

	/// <summary>Only the next dependency write is interrupted; every actual write and inverse delegates unchanged.</summary>
	private sealed class PublicationRegistry(
		IPackageRegistryService inner, IWikiService wiki, string packageId, string targetSlug, bool cancel, CancellationToken cancellationToken,
		string? conflictedPageId = null)
		: IPackageRegistryService
	{
		public TaskCompletionSource Published { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public InstalledPackageRecord? Installed { get; private set; }
		public PackageWikiPageRecord? Owner { get; private set; }
		public WikiPage? Page { get; private set; }
		public WikiPage? ConflictPage { get; private set; }
		public bool RevisionWasAbsent { get; private set; }
		public int PublicationCount { get; private set; }
		public int RevisionCommitCalls { get; private set; }
		private bool _interrupted;

		public async Task SetPackageDependenciesAsync(string id, IReadOnlyList<PackageDependencyRecord> dependencies)
		{
			if (!_interrupted && id == packageId)
			{
				_interrupted = true;
				// Installed Upsert has returned to WriteRowAsync, which has now registered its real inverse.
				Installed = (await inner.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
				Owner = (await inner.GetPackageWikiPageAsync(id, "main", targetSlug)).Expect<PackageWikiPageRecord>();
				Page = (await wiki.GetByIdAsync(Owner.PageId)).Expect<WikiPage>();
				if (conflictedPageId is { } pageId)
				{
					ConflictPage = (await wiki.GetByIdAsync(pageId)).Expect<WikiPage>();
				}
				RevisionWasAbsent = await inner.GetPackageRevisionAsync(id, Installed.CurrentRevision) is NotFound;
				PublicationCount++;
				Published.TrySetResult();
				if (cancel)
				{
					var neverReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
					await neverReleased.Task.WaitAsync(cancellationToken);
				}
				throw new PublicationFailure();
			}
			await inner.SetPackageDependenciesAsync(id, dependencies);
		}

		public Task UpsertInstalledPackageAsync(InstalledPackageRecord package) => inner.UpsertInstalledPackageAsync(package);
		public Task<Found<InstalledPackageRecord>> GetInstalledPackageAsync(string id) => inner.GetInstalledPackageAsync(id);
		public Task<IReadOnlyList<InstalledPackageRecord>> GetInstalledPackagesAsync() => inner.GetInstalledPackagesAsync();
		public Task RemoveInstalledPackageAsync(string id) => inner.RemoveInstalledPackageAsync(id);
		public Task UpsertPackageObjectAsync(PackageObjectRecord record) => inner.UpsertPackageObjectAsync(record);
		public Task<IReadOnlyList<PackageObjectRecord>> GetPackageObjectsAsync(string id) => inner.GetPackageObjectsAsync(id);
		public Task<int> CountPackageObjectsAsync(string id) => inner.CountPackageObjectsAsync(id);
		public Task RemovePackageObjectAsync(string id, string reference) => inner.RemovePackageObjectAsync(id, reference);
		public Task UpsertManagedAttributeAsync(ManagedAttributeRecord record) => inner.UpsertManagedAttributeAsync(record);
		public Task<IReadOnlyList<ManagedAttributeRecord>> GetManagedAttributesAsync(string id) => inner.GetManagedAttributesAsync(id);
		public Task<int> CountManagedAttributesAsync(string id) => inner.CountManagedAttributesAsync(id);
		public Task<IReadOnlyList<ManagedAttributeRecord>> GetManagedAttributesForObjectAsync(string id) => inner.GetManagedAttributesForObjectAsync(id);
		public Task RemoveManagedAttributeAsync(string id, string objid, string attribute) => inner.RemoveManagedAttributeAsync(id, objid, attribute);
		public Task UpsertManagedStructureAsync(ManagedStructureRecord record) => inner.UpsertManagedStructureAsync(record);
		public Task<IReadOnlyList<ManagedStructureRecord>> GetManagedStructuresAsync(string id) => inner.GetManagedStructuresAsync(id);
		public Task RemoveManagedStructureAsync(string id, string objid) => inner.RemoveManagedStructureAsync(id, objid);
		public Task UpsertPackageWikiPageAsync(PackageWikiPageRecord record) => inner.UpsertPackageWikiPageAsync(record);
		public Task<Found<PackageWikiPageRecord>> GetPackageWikiPageAsync(string id, string ns, string slug) => inner.GetPackageWikiPageAsync(id, ns, slug);
		public Task<IReadOnlyList<PackageWikiPageRecord>> GetPackageWikiPagesAsync(string id) => inner.GetPackageWikiPagesAsync(id);
		public Task<IReadOnlyList<PackageWikiPageRecord>> GetAllPackageWikiPagesAsync() => inner.GetAllPackageWikiPagesAsync();
		public Task RemovePackageWikiPageAsync(string id, string ns, string slug) => inner.RemovePackageWikiPageAsync(id, ns, slug);
		public Task<IReadOnlyList<PackageDependencyRecord>> GetPackageDependenciesAsync(string id) => inner.GetPackageDependenciesAsync(id);
		public Task<IReadOnlyList<PackageDependencyRecord>> GetPackageDependentsAsync(string id) => inner.GetPackageDependentsAsync(id);
		public Task<IReadOnlyList<PackageDependencyRecord>> GetAllPackageDependenciesAsync() => inner.GetAllPackageDependenciesAsync();
		public Task UpsertPackageRemoteAsync(PackageRemoteRecord remote) => inner.UpsertPackageRemoteAsync(remote);
		public Task<IReadOnlyList<PackageRemoteRecord>> GetPackageRemotesAsync() => inner.GetPackageRemotesAsync();
		public Task<Found<PackageRemoteRecord>> GetPackageRemoteAsync(string name) => inner.GetPackageRemoteAsync(name);
		public Task RemovePackageRemoteAsync(string name) => inner.RemovePackageRemoteAsync(name);
		public Task AddPackageRevisionAsync(PackageRevisionRecord revision)
		{
			RevisionCommitCalls++;
			return inner.AddPackageRevisionAsync(revision);
		}
		public Task<IReadOnlyList<PackageRevisionRecord>> GetPackageRevisionsAsync(string id) => inner.GetPackageRevisionsAsync(id);
		public Task<Found<PackageRevisionRecord>> GetPackageRevisionAsync(string id, int revision) => inner.GetPackageRevisionAsync(id, revision);
		public Task PrunePackageRevisionsAsync(string id, int keep) => inner.PrunePackageRevisionsAsync(id, keep);
	}
}
