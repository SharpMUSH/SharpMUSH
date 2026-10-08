using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.API;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// wiki-reader's <c>+wiki</c> read through the command parser, as a player sees it: an article ends with
/// its categories, named in the reader's language, and <c>+wiki/category</c> lists a category's pages.
/// Not in parallel with <see cref="PlusHelpIntegrationTests"/>, which installs and removes the same package.
/// </summary>
[NotInParallel]
public class WikiReaderIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IWikiService Wiki => WebAppFactoryArg.Services.GetRequiredService<IWikiService>();

	private static readonly string Tag = Guid.NewGuid().ToString("N")[..8];
	private readonly ConcurrentDictionary<long, DBRef> _actors = new();

	private async Task<CallState> God1(string command) =>
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private async Task<string> RunAs(long handle, string command)
	{
		var actor = _actors[handle];
		var before = Notifications.CountFor(actor);
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));
		return string.Join("\n", Notifications.For(actor).Skip(before));
	}

	/// <summary>Every raw notification <paramref name="command"/> sent the reader, as one string of markup.</summary>
	private async Task<MString> ShownAsync(long handle, string command)
	{
		var actor = _actors[handle];
		var before = Notifications.RawCountFor(actor);
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));
		return MarkupText.Join(MarkupText.Plain("\n"), Notifications.RawFor(actor).Skip(before)
			.Select(m => m switch
			{
				MString markup => markup,
				string text => MarkupText.Plain(text),
			}));
	}

	private async Task<long> CreatePlayerAsync(string name)
	{
		await God1($"@pcreate {name}=pw-{Tag}-1");
		var dbref = (await God1($"think [pmatch({name})]")).Message?.ToPlainText()?.Trim() ?? string.Empty;
		if (!DBRef.TryParse(dbref, out var parsed) || parsed is null)
		{
			throw new InvalidOperationException($"Failed to create player {name}; pmatch returned '{dbref}'.");
		}

		var handle = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, parsed.Value);
		_actors[handle] = parsed.Value;
		return handle;
	}

	[Test]
	public async Task AnArticleListsItsCategoriesUnderItsBodyInTheReadersLanguage()
	{
		var installer = WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
		var reader = await CreatePlayerAsync($"WikiR{Tag}");
		var categoryTitle = $"Harbour Places {Tag}";
		var categoryKey = WikiHelpers.CategoryKey(categoryTitle);

		var category = (await Wiki.CreateAsync(categoryTitle, "Places by the water.", "#1", WikiNamespace.Category, "en"))
			.Expect<WikiPage>();
		await Wiki.UpsertTranslationAsync(
			category.Id, "fr", $"Lieux du port {Tag}", "Lieux.", "#1", null, published: true, expectedRevisionNumber: null);
		var page = (await Wiki.CreateAsync($"Tidewater Quay {Tag}", "The quay at low tide.", "#1", WikiNamespace.Main, "en",
			[categoryTitle, $"Uncharted {Tag}"])).Expect<WikiPage>();

		try
		{
			var controller = new PackagesController(
				WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>(),
				WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
				WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
				installer,
				WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
				WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>());
			var applied = await controller.Apply(
				new ApplyRequest(BundledPackages.RemoteName, "wiki-reader", null, null, null), CancellationToken.None);
			await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because("wiki-reader must install for +wiki to answer");

			var article = await RunAs(reader, $"+wiki main:{page.Slug}");
			var body = article.IndexOf("The quay at low tide.", StringComparison.Ordinal);
			var bar = article.IndexOf("Categories:", StringComparison.Ordinal);
			await Assert.That(body).IsGreaterThanOrEqualTo(0);
			await Assert.That(bar).IsGreaterThan(body).Because("the categories are listed under the article's body");
			await Assert.That(article).Contains(categoryTitle).Because("a category's name is its category page's title");
			await Assert.That(article).Contains($"Uncharted {Tag.ToLowerInvariant()}")
				.Because("a category with no page shows its key as a title");
			await Assert.That(article).DoesNotContain($"{WikiHelpers.NamespaceName(WikiNamespace.Main)}:")
				.Because("a page's header names its namespace alone, never a category");

			var listing = await RunAs(reader, $"+wiki/category {categoryTitle}");
			await Assert.That(listing).Contains(page.Slug).Because("+wiki/category lists the pages in a category");
			await Assert.That(listing).Contains($"Category: {categoryTitle}");

			// A listing's table takes the screen's width rather than its content's, and no screen has
			// sides: a line copied from a terminal carries no border.
			foreach (var command in new[] { "+wiki", "+wiki/list main", $"+wiki/category {categoryTitle}" })
			{
				var shown = await ShownAsync(reader, command);
				await Assert.That(shown.Render(MarkupFormat.Html)).Contains("ms-fill")
					.Because($"{command} spans the full width");
				await Assert.That(shown.ToPlainText().Split('\n').Any(line => line.TrimStart().StartsWith('|') || line.TrimStart().StartsWith('│')))
					.IsFalse().Because($"{command} draws no sides");
			}

			await RunAs(reader, "@locale fr");
			var french = await RunAs(reader, $"+wiki main:{page.Slug}");
			await Assert.That(french).Contains($"Lieux du port {Tag}")
				.Because("translating the category page translates the category's name; the page's categories stay its own");
		}
		finally
		{
			await installer.UninstallAsync("wiki-reader", force: true, CancellationToken.None);
		}
	}
}
