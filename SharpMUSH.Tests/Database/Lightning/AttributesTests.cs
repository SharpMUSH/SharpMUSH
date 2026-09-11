using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

public class AttributesTests
{
	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private LightningDatabase _db = null!;
	private string _path = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = Create(_path);
		await _db.Migrate();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		if (Directory.Exists(_path))
		{
			try
			{
				Directory.Delete(_path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort, same as MigrationTests: a lingering mdb.lck can outlive the writer join.
			}
		}
	}

	private async Task<SharpPlayer> God() => (await _db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();

	[Test]
	public async Task SetCreatesAncestorsAndMarksBranch()
	{
		var god = await God();
		await _db.SetAttributeAsync(new DBRef(1), ["FOO", "BAR", "BAZ"], MarkupText.Plain("deep"), god);

		var path = await _db.GetAttributeAsync(new DBRef(1), ["FOO", "BAR", "BAZ"]).ToListAsync();
		await Assert.That(path.Count).IsEqualTo(3);
		await Assert.That(path.Select(a => a.LongName)).IsEquivalentTo(new[] { "FOO", "FOO`BAR", "FOO`BAR`BAZ" });
		await Assert.That(path[^1].Name).IsEqualTo("BAZ");
		await Assert.That(path[^1].Value.ToPlainText()).IsEqualTo("deep");

		var foo = await _db.GetAttributeAsync(new DBRef(1), ["FOO"]).FirstAsync();
		await Assert.That(foo.Flags.Select(f => f.Name)).Contains("branch");

		var partial = await _db.GetAttributeAsync(new DBRef(1), ["FOO", "NOPE"]).ToListAsync();
		await Assert.That(partial).IsEmpty();
	}

	[Test]
	public async Task WildcardListingStaysWithinOneLevelUnlessDoubleStar()
	{
		var god = await God();
		await _db.SetAttributeAsync(new DBRef(1), ["FOO", "A"], MarkupText.Plain("1"), god);
		await _db.SetAttributeAsync(new DBRef(1), ["FOO", "A", "B"], MarkupText.Plain("2"), god);
		await _db.SetAttributeAsync(new DBRef(1), ["FOX"], MarkupText.Plain("3"), god);

		var one = await _db.GetAttributesAsync(new DBRef(1), "FO*").Select(a => a.LongName).ToListAsync();
		await Assert.That(one).IsEquivalentTo(new[] { "FOO", "FOX" });

		var all = await _db.GetAttributesAsync(new DBRef(1), "FOO`**").Select(a => a.LongName).ToListAsync();
		await Assert.That(all).IsEquivalentTo(new[] { "FOO`A", "FOO`A`B" });

		var direct = await _db.GetAttributesAsync(new DBRef(1), "FOO`").Select(a => a.LongName).ToListAsync();
		await Assert.That(direct).IsEquivalentTo(new[] { "FOO`A" });
	}

	[Test]
	public async Task RegexResultsArriveParentBeforeChild()
	{
		var god = await God();
		await _db.SetAttributeAsync(new DBRef(1), ["FOO", "A", "B"], MarkupText.Plain("2"), god);

		var regex = await _db.GetAttributesByRegexAsync(new DBRef(1), "^FOO.*").Select(a => a.LongName).ToListAsync();
		await Assert.That(regex).IsEquivalentTo(new[] { "FOO", "FOO`A", "FOO`A`B" });
	}

	[Test]
	public async Task LazyAttributesDeferTheValueRead()
	{
		var god = await God();
		await _db.SetAttributeAsync(new DBRef(1), ["LAZY"], MarkupText.Plain("later"), god);

		var lazy = await _db.GetLazyAttributeAsync(new DBRef(1), ["LAZY"]).FirstAsync();
		await Assert.That(lazy.LongName).IsEqualTo("LAZY");
		await Assert.That((await lazy.Value.WithCancellation(CancellationToken.None)).ToPlainText()).IsEqualTo("later");
	}

	[Test]
	public async Task ClearWithChildrenKeepsTheNodeAndEmptiesItsValue()
	{
		var god = await God();
		await _db.SetAttributeAsync(new DBRef(1), ["KEEP"], MarkupText.Plain("parent"), god);
		await _db.SetAttributeAsync(new DBRef(1), ["KEEP", "CHILD"], MarkupText.Plain("child"), god);

		await Assert.That(await _db.ClearAttributeAsync(new DBRef(1), ["KEEP"])).IsTrue();

		var kept = await _db.GetAttributeAsync(new DBRef(1), ["KEEP"]).ToListAsync();
		await Assert.That(kept.Count).IsEqualTo(1);
		await Assert.That(kept[0].Value.ToPlainText()).IsEqualTo("");
		await Assert.That(await _db.GetAttributeAsync(new DBRef(1), ["KEEP", "CHILD"]).CountAsync()).IsEqualTo(2);
	}

	[Test]
	public async Task ClearWithoutChildrenDeletesTheNodeAndDropsTheParentBranchFlag()
	{
		var god = await God();
		await _db.SetAttributeAsync(new DBRef(1), ["ROOT", "ONLY"], MarkupText.Plain("leaf"), god);

		await Assert.That(await _db.ClearAttributeAsync(new DBRef(1), ["ROOT", "ONLY"])).IsTrue();

		await Assert.That(await _db.GetAttributeAsync(new DBRef(1), ["ROOT", "ONLY"]).ToListAsync()).IsEmpty();
		var root = await _db.GetAttributeAsync(new DBRef(1), ["ROOT"]).FirstAsync();
		await Assert.That(root.Flags.Select(f => f.Name)).DoesNotContain("branch");
	}

	[Test]
	public async Task WipeRemovesTheWholeSubtreeButNotItsSiblings()
	{
		var god = await God();
		await _db.SetAttributeAsync(new DBRef(1), ["TREE", "A", "B"], MarkupText.Plain("1"), god);
		await _db.SetAttributeAsync(new DBRef(1), ["TREE", "C"], MarkupText.Plain("2"), god);
		await _db.SetAttributeAsync(new DBRef(1), ["TREEZ"], MarkupText.Plain("sibling"), god);

		await Assert.That(await _db.WipeAttributeAsync(new DBRef(1), ["TREE", "A"])).IsTrue();

		var left = await _db.GetAttributesByRegexAsync(new DBRef(1), "^TREE").Select(a => a.LongName).ToListAsync();
		await Assert.That(left).IsEquivalentTo(new[] { "TREE", "TREE`C", "TREEZ" });
	}

	[Test]
	public async Task ReassignAttributeOwnerMovesOwnership()
	{
		var god = await God();
		var newOwnerRef = await _db.CreatePlayerAsync("Heir", "pw", new DBRef(2), new DBRef(2), 0);
		var heir = (await _db.GetObjectNodeAsync(newOwnerRef)).Expect<SharpPlayer>();

		await _db.SetAttributeAsync(new DBRef(1), ["OWNED"], MarkupText.Plain("v"), god);
		var before = await _db.GetAttributeAsync(new DBRef(1), ["OWNED"]).FirstAsync();
		await Assert.That((await before.Owner.WithCancellation(CancellationToken.None))!.Object.DBRef.Number).IsEqualTo(1);

		await _db.ReassignAttributeOwnerAsync(god, heir);

		var after = await _db.GetAttributeAsync(new DBRef(1), ["OWNED"]).FirstAsync();
		await Assert.That((await after.Owner.WithCancellation(CancellationToken.None))!.Object.DBRef.Number)
			.IsEqualTo(newOwnerRef.Number);
	}

	[Test]
	public async Task AttributeFlagsAndEntriesRoundTrip()
	{
		var god = await God();
		await _db.SetAttributeAsync(new DBRef(1), ["FLAGGED"], MarkupText.Plain("v"), god);

		var wizard = await _db.GetAttributeFlagAsync("wizard");
		await Assert.That(wizard).IsNotNull();

		var target = (await _db.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>().Object();
		await Assert.That(await _db.SetAttributeFlagAsync(target, ["FLAGGED"], wizard!)).IsTrue();
		var flagged = await _db.GetAttributeAsync(new DBRef(1), ["FLAGGED"]).FirstAsync();
		await Assert.That(flagged.Flags.Select(f => f.Name)).Contains("wizard");

		await Assert.That(await _db.UnsetAttributeFlagAsync(target, ["FLAGGED"], wizard!)).IsTrue();
		var unflagged = await _db.GetAttributeAsync(new DBRef(1), ["FLAGGED"]).FirstAsync();
		await Assert.That(unflagged.Flags.Select(f => f.Name)).DoesNotContain("wizard");

		var entry = await _db.CreateOrUpdateAttributeEntryAsync("MYENTRY", ["visual"]);
		await Assert.That(entry!.DefaultFlags).IsEquivalentTo(new[] { "visual" });
		await Assert.That((await _db.GetSharpAttributeEntry("MYENTRY"))!.Name).IsEqualTo("MYENTRY");
		await Assert.That(await _db.DeleteAttributeEntryAsync("MYENTRY")).IsTrue();
		await Assert.That(await _db.GetSharpAttributeEntry("MYENTRY")).IsNull();
	}

	[Test]
	public async Task SetAppliesTheAttributeEntryDefaultFlags()
	{
		var god = await God();
		await _db.SetAttributeAsync(new DBRef(1), ["ADESTROY"], MarkupText.Plain("v"), god);

		var attr = await _db.GetAttributeAsync(new DBRef(1), ["ADESTROY"]).FirstAsync();
		await Assert.That(attr.Flags.Select(f => f.Name)).Contains("wizard");
	}

	[Test]
	public async Task MigrationSeedsTheAncestorFormats()
	{
		var format = await _db.GetAttributeAsync(new DBRef(4), ["FORMAT", "EMIT"]).ToListAsync();
		await Assert.That(format.Count).IsEqualTo(2);
		await Assert.That(format[^1].Value.ToPlainText()).IsEqualTo("%0");
	}

	[Test]
	public async Task AttributeRowsAreStoredUnderTheDbrefPrefix()
	{
		var god = await God();
		await _db.SetAttributeAsync(new DBRef(1), ["STORED", "HERE"], MarkupText.Plain("v"), god);

		var keys = _db.Store.Read(tx => tx.Range(Tables.AttrMeta, Keys.Attr(1, "STORED"))
			.Select(e => Keys.ParseAttr(e.Key).LongName).ToList());
		await Assert.That(keys).IsEquivalentTo(new[] { "STORED", "STORED`HERE" });
	}
}
