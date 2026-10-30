using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Navigation;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>The portal navigation layout: one document, saved over the version it was read at, kept across a reopen.</summary>
public class NavLayoutStoreTests
{
	private string _path = null!;
	private LightningDatabase _db = null!;

	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private static NavLayout Layout(string label = "Community") => new(
		NavLayout.CurrentFormat,
		0,
		[
			new NavLayoutGroup("community", label, NavGroupModes.Flyout, "forum",
				[new("app:boards"), new("link:discord", "Our Discord", "https://discord.gg/example", "chat")]),
		],
		["core:help"],
		["core:play", "core:wiki"]);

	private async Task ReopenAsync()
	{
		await _db.DisposeAsync();
		_db = Create(_path);
		await _db.Migrate();
	}

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-navlayout-" + Guid.NewGuid().ToString("N"));
		_db = Create(_path);
		await _db.Migrate();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		Directory.Delete(_path, recursive: true);
	}

	[Test]
	public async Task ANewGame_HasNoLayout()
	{
		await Assert.That(await _db.GetNavLayoutAsync() is NotFound).IsTrue();
	}

	[Test]
	public async Task ASave_IsTheNextVersion_AndSurvivesAReopen()
	{
		var saved = (await _db.SaveNavLayoutAsync(Layout(), 0)).Expect<NavLayout>();

		await ReopenAsync();
		var loaded = (await _db.GetNavLayoutAsync()).Expect<NavLayout>();

		await Assert.That(saved.Version).IsEqualTo(1);
		await Assert.That(loaded.Version).IsEqualTo(1);
		await Assert.That(loaded.Groups.Single().Mode).IsEqualTo(NavGroupModes.Flyout);
		await Assert.That(loaded.Groups.Single().Items[1].Href).IsEqualTo("https://discord.gg/example");
		await Assert.That(loaded.Hidden).IsEquivalentTo(new[] { "core:help" });
		await Assert.That(loaded.MobileTabs).IsEquivalentTo(new[] { "core:play", "core:wiki" });
	}

	/// <summary>Two staff read version 1; the second to save is told where the layout is now and changes nothing.</summary>
	[Test]
	public async Task ASaveReadAtAnOlderVersion_IsAConflict()
	{
		await _db.SaveNavLayoutAsync(Layout(), 0);
		await _db.SaveNavLayoutAsync(Layout("First"), 1);

		var late = await _db.SaveNavLayoutAsync(Layout("Second"), 1);

		await Assert.That(late.Expect<NavLayoutConflict>().CurrentVersion).IsEqualTo(2);
		await Assert.That((await _db.GetNavLayoutAsync()).Expect<NavLayout>().Groups.Single().Label).IsEqualTo("First");
	}

	[Test]
	public async Task ALayoutBreakingTheRules_IsRefused()
	{
		var twice = Layout() with
		{
			Groups = [new NavLayoutGroup("a", "A", NavGroupModes.Inline, null, [new("core:wiki")]), new NavLayoutGroup("b", "B", NavGroupModes.Inline, null, [new("core:wiki")])],
		};

		var result = await _db.SaveNavLayoutAsync(twice, 0);

		await Assert.That(result.Expect<Error<string>>().Value).Contains("two places");
		await Assert.That(await _db.GetNavLayoutAsync() is NotFound).IsTrue();
	}

	[Test]
	public async Task AReset_LeavesNoLayout_AndTheNextSaveStartsOver()
	{
		await _db.SaveNavLayoutAsync(Layout(), 0);

		await _db.ResetNavLayoutAsync();

		await Assert.That(await _db.GetNavLayoutAsync() is NotFound).IsTrue();
		await Assert.That((await _db.SaveNavLayoutAsync(Layout(), 0)).Expect<NavLayout>().Version).IsEqualTo(1);
	}
}
