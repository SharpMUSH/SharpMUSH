using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>A package write that unpins a category, undone, leaves the category where staff placed it on the wiki home.</summary>
public class PackageWikiPinRevertTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IServiceProvider Services => WebAppFactoryArg.Services;
	private IWikiService Wiki => Services.GetRequiredService<IWikiService>();

	private static PackageManifest Manifest(string id, string version, bool pin)
		=> new PackageManifestService().ParseManifest($$"""
			format: 1.4
			package: {{id}}
			version: "{{version}}"
			wiki:
			  - page: category:{{id}}
			    title: Travel {{id}}
			    pin: {{(pin ? "true" : "false")}}
			    markdown: Roads and ferries.
			""").Expect<ParsedPackageManifest>().Manifest;

	private async Task<string[]> OrderOfAsync(params string[] keys)
		=> [.. (await Wiki.GetPinnedCategoriesAsync()).Where(keys.Contains)];

	[Test]
	public async Task UndoingAnUnpinPutsTheCategoryBackInItsPlace()
	{
		var id = "wkpin" + Guid.NewGuid().ToString("N")[..10];
		var before = id + "_before";
		var after = id + "_after";
		(await Wiki.SetCategoryPinnedAsync(before, true)).Expect<bool>();
		(await Services.GetRequiredService<IPackageInstallService>().ApplyAsync(Manifest(id, "1.0.0", pin: true),
			new PackageApplyRequest(new PackageApplySource("https://example.invalid/pin-revert", id + "/", "commit-1", "main"),
				new Dictionary<string, string>(), []))).Expect<PackageApplyResult>();
		(await Wiki.SetCategoryPinnedAsync(after, true)).Expect<bool>();
		await Assert.That(await OrderOfAsync(before, id, after)).IsEquivalentTo(new[] { before, id, after }, TUnit.Assertions.Enums.CollectionOrdering.Matching);

		var manager = (await Services.GetRequiredService<ISharpDatabase>().GetObjectNodeAsync(new DBRef(7))).Expect<SharpPlayer>();
		await using (var writes = TestPackageWrites.Issue(Services, manager))
		{
			var v2 = Manifest(id, "1.1.0", pin: false);
			(await Services.GetRequiredService<IPackageWikiService>().ApplyAsync(writes, id, "1.1.0", v2.WikiPages, [], null,
				_ => null, "#7", []))
				.Expect<IReadOnlyList<PackageWikiDeletion>>();
			await Assert.That(await OrderOfAsync(before, id, after)).IsEquivalentTo(new[] { before, after }, TUnit.Assertions.Enums.CollectionOrdering.Matching);

			await writes.RevertAsync(new Error<string>("A later step failed."));
		}

		await Assert.That(await OrderOfAsync(before, id, after)).IsEquivalentTo(new[] { before, id, after }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}
}
