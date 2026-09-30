using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Portal;

/// <summary>
/// <c>api/admin/profile-handler</c>, which the portal's Profile Handler page
/// (<c>/admin/profiles</c>) reads and resets. The page shipped while no controller served either
/// route, so it only ever showed its load error.
///
/// The handler is the shared <c>http_handler</c> every HTTP test uses, so the reset cases damage
/// only <c>FN`FIELD</c> — a helper the shipped profile schema does not call — and run one at a time.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class ProfileHandlerAdminApiTests(ServerWebAppFactory factory)
{
	private record AttributeStatus(string Attribute, bool Present);

	private record HandlerStatus(
		bool Configured, int? HandlerDbref, string? HandlerName, bool HandlerExists, List<AttributeStatus> Attributes);

	private record ResetResult(int Written, List<string> Failed);

	private const string Damaged = "FN`FIELD";

	private IMediator Mediator => factory.Services.GetRequiredService<IMediator>();

	private SharpMUSHOptions Options =>
		factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;

	private DBRef Handler => new((int)Options.Database.HttpHandler!.Value);

	/// <summary>Pinned to https: following the http→https redirect drops the request body and headers.</summary>
	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	private static IReadOnlyCollection<string> ProfileHandlerAttributes()
	{
		var manifest = new PackageManifestService().ParseManifest(BundledPackages.ManifestYaml("profile-handler")) switch
		{
			ParsedPackageManifest parsed => parsed.Manifest,
			PackageManifestFailure failure => throw new InvalidOperationException(
				string.Join("; ", failure.Issues.Select(i => i.ToString())))
		};
		return manifest.Objects.Single().Attributes.Keys.ToList();
	}

	private async Task<string?> ReadAsync(string attribute) =>
		(await Mediator.CreateStream(new GetAttributeQuery(Handler, attribute.Split('`'))).LastOrDefaultAsync())
		?.Value.ToPlainText();

	private async Task<HandlerStatus> StatusAsync(HttpClient http)
	{
		using var response = await http.GetAsync("api/admin/profile-handler");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (await response.Content.ReadFromJsonAsync<HandlerStatus>())!;
	}

	private async Task<ResetResult> ResetAsync(HttpClient http)
	{
		using var response = await http.PostAsync("api/admin/profile-handler/reset", null);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (await response.Content.ReadFromJsonAsync<ResetResult>())!;
	}

	[Test]
	public async Task Status_ReportsTheConfiguredHandlerAndEveryProfileHandlerAttribute()
	{
		using var http = CreateClient();
		var status = await StatusAsync(http);

		await Assert.That(status.Configured).IsTrue();
		await Assert.That(status.HandlerExists).IsTrue();
		await Assert.That(status.HandlerDbref).IsEqualTo(Handler.Number);
		await Assert.That(status.HandlerName).IsNotNull().And.IsNotEmpty();
		await Assert.That(status.Attributes.Select(a => a.Attribute))
			.IsEquivalentTo(ProfileHandlerAttributes());
		await Assert.That(status.Attributes.Single(a => a.Attribute == "GET`PROFILE`SCHEMA").Present).IsTrue();
	}

	/// <summary>
	/// A second reset finds nothing to write. Were the planned value (placeholders resolved) ever to
	/// differ from what the first reset stored, every reset would rewrite those attributes and report
	/// them as repaired.
	/// </summary>
	[Test, NotInParallel(nameof(ProfileHandlerAdminApiTests))]
	public async Task Reset_OfAnAlreadyResetHandler_WritesNothing()
	{
		using var http = CreateClient();
		await ResetAsync(http);

		var again = await ResetAsync(http);

		await Assert.That(again.Failed).IsEmpty();
		await Assert.That(again.Written).IsEqualTo(0);
	}

	[Test, NotInParallel(nameof(ProfileHandlerAdminApiTests))]
	public async Task Reset_RestoresADeletedAttribute()
	{
		using var http = CreateClient();
		await ResetAsync(http); // from a clean handler, so the count below is this test's damage alone
		await Mediator.Send(new ClearAttributeCommand(Handler, Damaged.Split('`')));

		var before = await StatusAsync(http);
		await Assert.That(before.Attributes.Single(a => a.Attribute == Damaged).Present).IsFalse();

		var result = await ResetAsync(http);

		await Assert.That(result.Failed).IsEmpty();
		await Assert.That(result.Written).IsEqualTo(1);
		await Assert.That(await ReadAsync(Damaged)).IsEqualTo(BundledHttpHooks.Attribute(Damaged));
		var after = await StatusAsync(http);
		await Assert.That(after.Attributes.Single(a => a.Attribute == Damaged).Present).IsTrue();
	}

	[Test, NotInParallel(nameof(ProfileHandlerAdminApiTests))]
	public async Task Reset_OverwritesALocallyEditedAttribute()
	{
		using var http = CreateClient();
		await ResetAsync(http); // from a clean handler, so the count below is this test's damage alone
		var marker = TestIsolationHelpers.GenerateUniqueName("PHEdit");
		var owner = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		await Mediator.Send(new SetAttributeCommand(Handler, Damaged.Split('`'), MarkupText.Plain(marker), owner));
		await Assert.That(await ReadAsync(Damaged)).IsEqualTo(marker);

		var result = await ResetAsync(http);

		await Assert.That(result.Failed).IsEmpty();
		await Assert.That(result.Written).IsEqualTo(1);
		await Assert.That(await ReadAsync(Damaged)).IsEqualTo(BundledHttpHooks.Attribute(Damaged));
	}

	/// <summary>
	/// A game that installed a newer profile-handler than this build ships keeps it: bootstrap leaves a
	/// same-or-newer install alone, so writing this build's values would downgrade the softcode while the
	/// registry still records the newer version and its baselines.
	/// </summary>
	[Test, NotInParallel(nameof(ProfileHandlerAdminApiTests))]
	public async Task Reset_WhenANewerVersionIsInstalled_RefusesAndWritesNothing()
	{
		using var http = CreateClient();
		await ResetAsync(http); // from a clean handler
		var registry = factory.Services.GetRequiredService<IPackageRegistryService>();
		var installed = (await registry.GetInstalledPackageAsync("profile-handler")).Expect<InstalledPackageRecord>();
		var marker = TestIsolationHelpers.GenerateUniqueName("PHNewer");
		var owner = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		await Mediator.Send(new SetAttributeCommand(Handler, Damaged.Split('`'), MarkupText.Plain(marker), owner));
		await registry.UpsertInstalledPackageAsync(installed with { Version = "999.0.0" });
		try
		{
			using var response = await http.PostAsync("api/admin/profile-handler/reset", null);

			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
			await Assert.That(await ReadAsync(Damaged)).IsEqualTo(marker);
		}
		finally
		{
			await registry.UpsertInstalledPackageAsync(installed);
			await ResetAsync(http);
		}
	}
}
