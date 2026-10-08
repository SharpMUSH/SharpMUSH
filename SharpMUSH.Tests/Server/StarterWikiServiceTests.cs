using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Resources;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// <see cref="StarterWikiService"/>: the setup wizard writes the starter pages a game lacks, filed in their
/// categories, leaves the ones it has, and replaces Home only while it is still the page seeded at boot.
/// </summary>
public class StarterWikiServiceTests
{
	/// <summary>Expanded server data in memory, by type name, as the store keys it.</summary>
	private sealed class InMemoryServerData : IExpandedObjectDataService
	{
		private readonly Dictionary<string, object> _data = new(StringComparer.Ordinal);

		public ValueTask<T?> GetExpandedDataAsync<T>(SharpObject obj) where T : class => throw new NotSupportedException();

		public ValueTask SetExpandedDataAsync<T>(T data, SharpObject obj, bool ignoreNull = false) where T : class
			=> throw new NotSupportedException();

		public ValueTask<T?> GetExpandedServerDataAsync<T>() where T : class
			=> ValueTask.FromResult(_data.TryGetValue(typeof(T).Name, out var value) ? (T?)value : null);

		public ValueTask SetExpandedServerDataAsync<T>(T data, bool ignoreNull = false) where T : class
		{
			_data[typeof(T).Name] = data;
			return ValueTask.CompletedTask;
		}
	}

	private static WikiPage Page(string title, WikiNamespace ns, string markdown)
		=> new($"{ns}:{title}", title.ToLowerInvariant().Replace(' ', '_'), title, ns.ToString().ToLowerInvariant(),
			markdown, string.Empty, string.Empty, "#1", "#1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1);

	/// <summary>A wiki with the boot-seeded Home and, optionally, pages the game already has.</summary>
	private static IWikiService Wiki(string homeMarkdown, params (string Title, WikiNamespace Ns)[] existing)
	{
		var wiki = Substitute.For<IWikiService>();
		var home = Page("Home", WikiNamespace.Main, homeMarkdown);
		wiki.GetBySlugAsync("home", WikiNamespace.Main).Returns(Task.FromResult<Found<WikiPage>>(home));
		wiki.UpdateAsync(home.Id!, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
			.Returns(Task.FromResult<Found<WikiPage>>(home));

		wiki.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WikiNamespace>(),
				Arg.Any<string?>(), Arg.Any<IEnumerable<string>?>())
			.Returns(call =>
			{
				var (title, ns) = (call.ArgAt<string>(0), call.ArgAt<WikiNamespace>(3));
				return existing.Contains((title, ns))
					? Task.FromResult<Result<WikiPage>>(new Error<string>("A page with that slug already exists."))
					: Task.FromResult<Result<WikiPage>>(Page(title, ns, call.ArgAt<string>(1)));
			});
		foreach (var (title, ns) in existing)
		{
			wiki.GetBySlugAsync(title.ToLowerInvariant().Replace(' ', '_'), ns)
				.Returns(Task.FromResult<Found<WikiPage>>(Page(title, ns, "the game's own")));
		}

		wiki.SetCategoryPinnedAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(Task.FromResult<Result<bool>>(true));
		wiki.SetRequirementsAsync(Arg.Any<WikiRuleTarget>(), Arg.Any<IReadOnlyDictionary<WikiAction, IReadOnlyList<string>>>(), Arg.Any<string>()).Returns(Task.FromResult<Found<None>>(new None()));
		return wiki;
	}

	[Test]
	public async Task Apply_WritesEveryPage_InItsCategories_AndProtectsTheHubs()
	{
		var wiki = Wiki(SeededWikiPages.Home);
		var data = new InMemoryServerData();
		var service = new StarterWikiService(wiki, data, NullLogger<StarterWikiService>.Instance);

		await Assert.That(await service.AppliedAsync()).IsFalse();
		await Assert.That(await service.ApplyAsync() is Success).IsTrue();

		foreach (var page in StarterWikiPages.All)
		{
			await wiki.Received(1).CreateAsync(page.Title, page.Markdown, "#1", page.Namespace, "en",
				Arg.Is<IEnumerable<string>?>(c => c != null && c.SequenceEqual(page.Categories)));
		}

		await wiki.Received(1).SetRequirementsAsync(WikiRuleTarget.ForPage("Main:Theme"), WikiRequirementSet.Protection, "#1");
		await wiki.Received(1).SetRequirementsAsync(WikiRuleTarget.ForPage("Main:Setting"), WikiRequirementSet.Protection, "#1");
		await wiki.Received(1).SetRequirementsAsync(WikiRuleTarget.ForPage("Main:Policies"), WikiRequirementSet.Protection, "#1");
		await wiki.Received(3).SetRequirementsAsync(Arg.Any<WikiRuleTarget>(), WikiRequirementSet.Protection, "#1");
		await wiki.Received(1).UpdateAsync("Main:Home", StarterWikiPages.Home, "#1", Arg.Any<string?>());
		await Assert.That(await service.AppliedAsync()).IsTrue();
	}

	/// <summary>Every category the starter set creates is pinned to the wiki home, the game's own included.</summary>
	[Test]
	public async Task Apply_PinsEveryStarterCategory()
	{
		var wiki = Wiki(SeededWikiPages.Home, ("Places", WikiNamespace.Category));
		var service = new StarterWikiService(wiki, new InMemoryServerData(), NullLogger<StarterWikiService>.Instance);

		await Assert.That(await service.ApplyAsync() is Success).IsTrue();

		var categories = StarterWikiPages.All.Where(p => p.Namespace == WikiNamespace.Category).Select(p => p.Title).ToList();
		await Assert.That(categories).Contains("Places");
		foreach (var category in categories)
		{
			await wiki.Received(1).SetCategoryPinnedAsync(category, true);
		}

		await wiki.Received(categories.Count).SetCategoryPinnedAsync(Arg.Any<string>(), Arg.Any<bool>());
	}

	/// <summary>A page the game already has stays as it is, and is not a failure.</summary>
	[Test]
	public async Task Apply_LeavesPagesTheGameHas()
	{
		var wiki = Wiki(SeededWikiPages.Home, ("Theme", WikiNamespace.Main));
		var service = new StarterWikiService(wiki, new InMemoryServerData(), NullLogger<StarterWikiService>.Instance);

		await Assert.That(await service.ApplyAsync() is Success).IsTrue();
		await wiki.DidNotReceive().SetRequirementsAsync(WikiRuleTarget.ForPage("Main:Theme"), Arg.Any<IReadOnlyDictionary<WikiAction, IReadOnlyList<string>>>(), Arg.Any<string>());
		await wiki.DidNotReceive().UpdateAsync("Main:Theme", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
	}

	/// <summary>An administrator's own Home is not replaced.</summary>
	[Test]
	public async Task Apply_KeepsAnEditedHome()
	{
		var wiki = Wiki("Welcome to our game.");
		var service = new StarterWikiService(wiki, new InMemoryServerData(), NullLogger<StarterWikiService>.Instance);

		await Assert.That(await service.ApplyAsync() is Success).IsTrue();
		await wiki.DidNotReceive().UpdateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
	}

	/// <summary>Once applied, a repeated request writes nothing, so a deleted starter page stays deleted.</summary>
	[Test]
	public async Task Apply_Twice_WritesOnce()
	{
		var wiki = Wiki(SeededWikiPages.Home);
		var service = new StarterWikiService(wiki, new InMemoryServerData(), NullLogger<StarterWikiService>.Instance);

		await Task.WhenAll(service.ApplyAsync(), service.ApplyAsync());
		await Assert.That(await service.ApplyAsync() is Success).IsTrue();

		await wiki.Received(StarterWikiPages.All.Count).CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<WikiNamespace>(), Arg.Any<string?>(), Arg.Any<IEnumerable<string>?>());
	}
}
