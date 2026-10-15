using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Attribute indexes in the provider (<c>LightningDatabase.AttributeIndex.cs</c>): every place a value is written,
/// cleared, wiped, destroyed or imported keeps the index, the orders the options give, the cut and its recheck, and a
/// build that stops and resumes.
/// </summary>
public class AttributeIndexTests : LightningDatabaseFixture
{
	private async Task<SharpPlayer> God() => (await Db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();

	private async Task<AnySharpContainer> Room()
		=> (await Db.GetObjectNodeAsync(new DBRef(2))).Expect<AnySharpObject>().AsOptionalContainer.Expect<AnySharpContainer>();

	private async Task<int> ThingAsync(string name)
	{
		var room = await Room();
		return (await Db.CreateThingAsync(name, room, await God(), room)).Number;
	}

	private async Task SetAsync(int dbref, string name, string value)
		=> await Db.SetAttributeAsync(new DBRef(dbref), name.Split('`'), MarkupText.Plain(value), await God());

	/// <summary>Declares <paramref name="name"/>, indexes it and builds the index to the end.</summary>
	private async Task IndexAsync(string name, AttributeIndexOptions? options = null)
	{
		await Db.CreateOrUpdateAttributeEntryAsync(name, []);
		(await Db.SetAttributeIndexAsync(name, options ?? AttributeIndexOptions.Default)).Expect<SharpAttributeEntry>();
		await BuildAsync(name);
	}

	private async Task BuildAsync(string name, int batch = 5_000)
	{
		while (!(await Db.BuildAttributeIndexBatchAsync(name, batch)).Expect<AttributeIndexBuildStep>().Ready)
		{
		}
	}

	private async Task<List<int>> QueryAsync(string name, IndexedAttributeOperation op, string value = "", string? upper = null,
		int limit = 1_000)
	{
		var found = new List<int>();
		IndexedAttributeCursor? after = null;
		while (true)
		{
			var page = (await Db.QueryAttributeIndexAsync(new IndexedAttributeQuerySpec(name, op, value, upper, after, limit, false)))
				.Expect<IndexedAttributePage>();
			found.AddRange(page.Hits.Select(hit => hit.Number));
			if (page.Next is null) return found;
			after = page.Next;
		}
	}

	private Task<List<int>> EqualAsync(string name, string value) => QueryAsync(name, IndexedAttributeOperation.Equal, value);

	[Test]
	public async Task SetClearAndRewriteKeepTheIndex()
	{
		await IndexAsync("IDXSTATE");
		var a = await ThingAsync("IdxA");
		var b = await ThingAsync("IdxB");
		await SetAsync(a, "IDXSTATE", "Approved");
		await SetAsync(b, "IDXSTATE", "approved");

		await Assert.That(await EqualAsync("IDXSTATE", "APPROVED")).IsEquivalentTo([a, b]);

		await SetAsync(b, "IDXSTATE", "pending");
		await Assert.That(await EqualAsync("IDXSTATE", "approved")).IsEquivalentTo([a]);
		await Assert.That(await EqualAsync("IDXSTATE", "pending")).IsEquivalentTo([b]);

		await Db.ClearAttributeAsync(new DBRef(a), ["IDXSTATE"]);
		await Assert.That(await EqualAsync("IDXSTATE", "approved")).IsEmpty();

		// An empty value has no entry.
		await SetAsync(b, "IDXSTATE", "");
		await Assert.That(await QueryAsync("IDXSTATE", IndexedAttributeOperation.First)).IsEmpty();
		await Assert.That(Dump(Tables.AttrIdxOf)).IsEmpty();
	}

	[Test]
	public async Task WipeAndDestroyTakeCopiesOut()
	{
		await IndexAsync("IDXTREE`LEAF");
		await IndexAsync("IDXTOP");
		var a = await ThingAsync("WipeA");
		var b = await ThingAsync("WipeB");
		await SetAsync(a, "IDXTREE`LEAF", "x");
		await SetAsync(b, "IDXTREE`LEAF", "x");
		await SetAsync(b, "IDXTOP", "y");

		await Db.WipeAttributeAsync(new DBRef(a), ["IDXTREE"]);
		await Assert.That(await EqualAsync("IDXTREE`LEAF", "x")).IsEquivalentTo([b]);

		await Db.DeleteObjectAsync(new DBRef(b));
		await Assert.That(await EqualAsync("IDXTREE`LEAF", "x")).IsEmpty();
		await Assert.That(await EqualAsync("IDXTOP", "y")).IsEmpty();
		await Assert.That(Dump(Tables.AttrIdx)).IsEmpty();
		await Assert.That(Dump(Tables.AttrIdxOf)).IsEmpty();
	}

	[Test]
	public async Task AnImportIsIndexedAsItLands()
	{
		await IndexAsync("IDXIMPORT");
		var a = await ThingAsync("ImportA");
		var god = await God();
		await Db.SetAttributesAsync(new DBRef(a), [new AttributeWrite(["IDXIMPORT"], MarkupText.Plain("carried"), god, [])]);

		await Assert.That(await EqualAsync("IDXIMPORT", "carried")).IsEquivalentTo([a]);
	}

	[Test]
	public async Task NumbersComeFirstInNumericOrderThenText()
	{
		await IndexAsync("IDXLEVEL", new AttributeIndexOptions(Numeric: true));
		var values = new[] { "10", "9", "05", "5", "-1.5", "abc", "Zed", "-0", "0" };
		var things = new List<int>();
		foreach (var value in values)
		{
			var thing = await ThingAsync("Level" + value);
			await SetAsync(thing, "IDXLEVEL", value);
			things.Add(thing);
		}

		int Of(string value) => things[Array.IndexOf(values, value)];

		await Assert.That(await QueryAsync("IDXLEVEL", IndexedAttributeOperation.First)).IsEquivalentTo(
			[Of("-1.5"), Of("-0"), Of("0"), Of("05"), Of("5"), Of("9"), Of("10"), Of("abc"), Of("Zed")],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(await EqualAsync("IDXLEVEL", "5.0")).IsEquivalentTo([Of("05"), Of("5")]);
		await Assert.That(await EqualAsync("IDXLEVEL", "0")).IsEquivalentTo([Of("-0"), Of("0")]);
		await Assert.That(await QueryAsync("IDXLEVEL", IndexedAttributeOperation.Between, "5", "10"))
			.IsEquivalentTo([Of("05"), Of("5"), Of("9"), Of("10")], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(await QueryAsync("IDXLEVEL", IndexedAttributeOperation.Greater, "9"))
			.IsEquivalentTo([Of("10"), Of("abc"), Of("Zed")], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(await QueryAsync("IDXLEVEL", IndexedAttributeOperation.Less, "0"))
			.IsEquivalentTo([Of("-1.5")]);
		// A prefix in a numeric index matches text only.
		await Assert.That(await QueryAsync("IDXLEVEL", IndexedAttributeOperation.Prefix, "z")).IsEquivalentTo([Of("Zed")]);
		await Assert.That(await QueryAsync("IDXLEVEL", IndexedAttributeOperation.Last, limit: 2)).IsEquivalentTo(
			[Of("Zed"), Of("abc"), Of("10"), Of("9"), Of("5"), Of("05"), Of("0"), Of("-0"), Of("-1.5")],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task ExactKeepsCaseAndTheDefaultFolds()
	{
		await IndexAsync("IDXEXACT", new AttributeIndexOptions(Exact: true));
		await IndexAsync("IDXFOLD");
		var a = await ThingAsync("CaseA");
		var b = await ThingAsync("CaseB");
		foreach (var name in new[] { "IDXEXACT", "IDXFOLD" })
		{
			await SetAsync(a, name, "Abc");
			await SetAsync(b, name, "abc");
		}

		await Assert.That(await EqualAsync("IDXEXACT", "Abc")).IsEquivalentTo([a]);
		await Assert.That(await EqualAsync("IDXFOLD", "ABC")).IsEquivalentTo([a, b]);
		await Assert.That(await QueryAsync("IDXEXACT", IndexedAttributeOperation.First))
			.IsEquivalentTo([a, b], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task ASplitIndexListsEachElement()
	{
		await IndexAsync("IDXTAGS", new AttributeIndexOptions(Split: " "));
		await IndexAsync("IDXPIPES", new AttributeIndexOptions(Split: "|"));
		var a = await ThingAsync("TagsA");
		await SetAsync(a, "IDXTAGS", "red  blue red");
		await SetAsync(a, "IDXPIPES", "one two||three");

		await Assert.That(await EqualAsync("IDXTAGS", "blue")).IsEquivalentTo([a]);
		await Assert.That(await EqualAsync("IDXTAGS", "red")).IsEquivalentTo([a]);
		await Assert.That(await EqualAsync("IDXPIPES", "one two")).IsEquivalentTo([a]);
		await Assert.That(await EqualAsync("IDXPIPES", "")).IsEmpty();
		// A repeated element is one entry.
		await Assert.That(await QueryAsync("IDXTAGS", IndexedAttributeOperation.First)).IsEquivalentTo([a, a]);

		await SetAsync(a, "IDXTAGS", "green");
		await Assert.That(await EqualAsync("IDXTAGS", "red")).IsEmpty();
		await Assert.That(await EqualAsync("IDXTAGS", "green")).IsEquivalentTo([a]);
	}

	[Test]
	public async Task ALongValueIsCutAndAnEqualityProbeChecksTheStoredValue()
	{
		await IndexAsync("IDXLONG");
		var common = new string('a', 398) + "é";
		var a = await ThingAsync("LongA");
		var b = await ThingAsync("LongB");
		await SetAsync(a, "IDXLONG", common + "first");
		await SetAsync(b, "IDXLONG", common + "second");

		await Assert.That(await EqualAsync("IDXLONG", common + "first")).IsEquivalentTo([a]);
		await Assert.That(await EqualAsync("IDXLONG", common + "SECOND")).IsEquivalentTo([b]);
		await Assert.That(await EqualAsync("IDXLONG", common)).IsEmpty();
		await Assert.That((await Db.CountAttributeIndexAsync("IDXLONG", common + "first")).Expect<long>()).IsEqualTo(1);
		await Assert.That(await QueryAsync("IDXLONG", IndexedAttributeOperation.Prefix, common + "sec")).IsEquivalentTo([b]);
	}

	/// <summary>Values that share the cut share a key, so a range bound on that key compares the stored values in full.</summary>
	[Test]
	public async Task ARangeProbeAtTheCutComparesTheStoredValue()
	{
		await IndexAsync("IDXLONGRANGE");
		var common = new string('b', 398) + "é";
		var a = await ThingAsync("RangeA");
		var b = await ThingAsync("RangeB");
		var c = await ThingAsync("RangeC");
		await SetAsync(a, "IDXLONGRANGE", common + "apple");
		await SetAsync(b, "IDXLONGRANGE", common + "melon");
		await SetAsync(c, "IDXLONGRANGE", common + "zebra");

		await Assert.That(await QueryAsync("IDXLONGRANGE", IndexedAttributeOperation.Less, common + "melon")).IsEquivalentTo([a]);
		await Assert.That(await QueryAsync("IDXLONGRANGE", IndexedAttributeOperation.LessOrEqual, common + "MELON")).IsEquivalentTo([a, b]);
		await Assert.That(await QueryAsync("IDXLONGRANGE", IndexedAttributeOperation.Greater, common + "melon")).IsEquivalentTo([c]);
		await Assert.That(await QueryAsync("IDXLONGRANGE", IndexedAttributeOperation.GreaterOrEqual, common + "melon")).IsEquivalentTo([b, c]);
		await Assert.That(await QueryAsync("IDXLONGRANGE", IndexedAttributeOperation.Between, common + "b", common + "n")).IsEquivalentTo([b]);
		await Assert.That(await QueryAsync("IDXLONGRANGE", IndexedAttributeOperation.Less, common + "m", limit: 1)).IsEquivalentTo([a]);
	}

	/// <summary>An index key holds the name, a separator and up to 400 bytes of value, inside LMDB's 511.</summary>
	[Test]
	public async Task ANameTooLongForAnIndexKeyIsRefused()
	{
		var tooLong = "IDX" + new string('N', AttributeIndexOptions.MaxNameBytes - 2);
		await Db.CreateOrUpdateAttributeEntryAsync(tooLong, []);
		await Assert.That(await Db.SetAttributeIndexAsync(tooLong, AttributeIndexOptions.Default) is Error<string>).IsTrue();

		var longest = "IDX" + new string('M', AttributeIndexOptions.MaxNameBytes - 3);
		await IndexAsync(longest);
		var a = await ThingAsync("LongestName");
		var value = new string('v', 600);
		await SetAsync(a, longest, value);
		await Assert.That(await EqualAsync(longest, value)).IsEquivalentTo([a]);
	}

	[Test]
	public async Task AnInternalCopyIsNeverListed()
	{
		await IndexAsync("IDXHIDDEN");
		var a = await ThingAsync("InternalA");
		await SetAsync(a, "IDXHIDDEN", "v");
		var thing = (await Db.GetObjectNodeAsync(new DBRef(a))).Expect<AnySharpObject>().Object();
		var internalFlag = (await Db.GetAttributeFlagAsync("internal"))!;

		await Db.SetAttributeFlagAsync(thing, ["IDXHIDDEN"], internalFlag);
		await Assert.That(await EqualAsync("IDXHIDDEN", "v")).IsEmpty();

		await Db.UnsetAttributeFlagAsync(thing, ["IDXHIDDEN"], internalFlag);
		await Assert.That(await EqualAsync("IDXHIDDEN", "v")).IsEquivalentTo([a]);

		await Db.CreateOrUpdateAttributeEntryAsync("IDXINTERNAL", ["internal"]);
		await Assert.That(await Db.SetAttributeIndexAsync("IDXINTERNAL", AttributeIndexOptions.Default) is Error<string>).IsTrue();
	}

	[Test]
	public async Task ATypeScopeListsOnlyThatType()
	{
		await IndexAsync("IDXSCOPED", new AttributeIndexOptions(Type: "THING"));
		var a = await ThingAsync("ScopedA");
		await SetAsync(a, "IDXSCOPED", "v");
		await SetAsync(1, "IDXSCOPED", "v");

		await Assert.That(await EqualAsync("IDXSCOPED", "v")).IsEquivalentTo([a]);
	}

	[Test]
	public async Task ABuildResumesAfterARestartAndKeepsWritesBetweenBatches()
	{
		await Db.CreateOrUpdateAttributeEntryAsync("IDXLATE", []);
		var things = new List<int>();
		for (var i = 0; i < 12; i++)
		{
			var thing = await ThingAsync("Late" + i);
			await SetAsync(thing, "IDXLATE", i % 2 == 0 ? "even" : "odd");
			things.Add(thing);
		}

		(await Db.SetAttributeIndexAsync("IDXLATE", AttributeIndexOptions.Default)).Expect<SharpAttributeEntry>();
		var first = (await Db.BuildAttributeIndexBatchAsync("IDXLATE", 5)).Expect<AttributeIndexBuildStep>();
		await Assert.That(first.Ready).IsFalse();
		await Assert.That(await Db.QueryAttributeIndexAsync(new IndexedAttributeQuerySpec("IDXLATE",
			IndexedAttributeOperation.Equal, "even", null, null, 10, false)) is IndexBuilding).IsTrue();
		await Assert.That(await Db.CountAttributeIndexAsync("IDXLATE", "even") is IndexBuilding).IsTrue();

		// A write while building, on an object the build has not reached, lands in the index; the build re-reads it.
		await SetAsync(things[^1], "IDXLATE", "even");

		await ReopenAsync();
		var index = (await Db.GetSharpAttributeEntry("IDXLATE"))?.Index;
		await Assert.That(index is { Ready: false }).IsTrue();
		await Assert.That(index?.ResumeAt).IsEqualTo(first.ResumeAt);

		await BuildAsync("IDXLATE", 5);
		await Assert.That(await EqualAsync("IDXLATE", "even")).IsEquivalentTo([.. things.Where((_, i) => i % 2 == 0), things[^1]]);
		await Assert.That(await EqualAsync("IDXLATE", "odd")).IsEquivalentTo([.. things.Where((_, i) => i % 2 == 1 && i != 11)]);
	}

	[Test]
	public async Task OffDropsEveryRowAndTheEntryKeepsItsIndexThroughAccess()
	{
		await IndexAsync("IDXDROP");
		var a = await ThingAsync("DropA");
		await SetAsync(a, "IDXDROP", "v");

		var updated = await Db.CreateOrUpdateAttributeEntryAsync("IDXDROP", ["no_command"]);
		await Assert.That(updated?.Index).IsNotNull();
		await Assert.That(await EqualAsync("IDXDROP", "v")).IsEquivalentTo([a]);

		await Assert.That(await Db.DropAttributeIndexAsync("IDXDROP")).IsTrue();
		await Assert.That(Dump(Tables.AttrIdx)).IsEmpty();
		await Assert.That(Dump(Tables.AttrIdxOf)).IsEmpty();
		await Assert.That(await Db.QueryAttributeIndexAsync(new IndexedAttributeQuerySpec("IDXDROP",
			IndexedAttributeOperation.First, "", null, null, 10, false)) is NotIndexed).IsTrue();

		// Writes to an attribute with no index leave no rows.
		await SetAsync(a, "IDXDROP", "w");
		await Assert.That(Dump(Tables.AttrIdx)).IsEmpty();
	}

	[Test]
	public async Task AFilteredSearchSeedsFromTheIndex()
	{
		await IndexAsync("IDXSEARCH");
		var a = await ThingAsync("SeedA");
		var b = await ThingAsync("SeedB");
		await SetAsync(a, "IDXSEARCH", "Gold");
		await SetAsync(b, "IDXSEARCH", "silver");

		var found = await Db.GetFilteredObjectsAsync(new ObjectSearchFilter { AttributeValue = new AttributeIndexProbe("IDXSEARCH", "gold") })
			.Select(o => o.Key).ToListAsync();
		await Assert.That(found).IsEquivalentTo([a]);

		var owned = await Db.GetFilteredObjectsAsync(new ObjectSearchFilter
		{
			Owner = new DBRef(1), Types = ["THING"], AttributeValue = new AttributeIndexProbe("IDXSEARCH", "silver")
		}).Select(o => o.Key).ToListAsync();
		await Assert.That(owned).IsEquivalentTo([b]);

		var unindexed = await Db.GetFilteredObjectsAsync(new ObjectSearchFilter { AttributeValue = new AttributeIndexProbe("NOSUCHIDX", "gold") })
			.ToListAsync();
		await Assert.That(unindexed).IsEmpty();
	}

	[Test]
	public async Task FactsCarryOwnerAndAttributeFlags()
	{
		await IndexAsync("IDXFACTS");
		var a = await ThingAsync("FactsA");
		await SetAsync(a, "IDXFACTS", "v");
		var thing = (await Db.GetObjectNodeAsync(new DBRef(a))).Expect<AnySharpObject>().Object();
		await Db.SetAttributeFlagAsync(thing, ["IDXFACTS"], (await Db.GetAttributeFlagAsync("visual"))!);

		var hit = (await Db.QueryAttributeIndexAsync(new IndexedAttributeQuerySpec("IDXFACTS", IndexedAttributeOperation.Equal, "v",
			null, null, 10, true))).Expect<IndexedAttributePage>().Hits.Single();
		await Assert.That(hit.Owner).IsEqualTo(1);
		await Assert.That(hit.CreationTime).IsGreaterThan(0);
		await Assert.That(hit.Facts.HasFlag(IndexedAttributeFacts.AllVisual)).IsTrue();
		await Assert.That(hit.Facts.HasFlag(IndexedAttributeFacts.MortalDark)).IsFalse();
	}

	/// <summary>
	/// Every start re-seeds the built-in attribute entries; an index staff put on one of them is not the seed's and
	/// stays, with its rows still kept by later writes.
	/// </summary>
	[Test]
	public async Task AnIndexOnASeededEntrySurvivesARestart()
	{
		await IndexAsync("SEX", new AttributeIndexOptions(Type: "PLAYER"));
		await SetAsync(1, "SEX", "Plural");

		await ReopenAsync();
		await Assert.That((await Db.GetSharpAttributeEntry("SEX"))?.Index?.Options).IsEqualTo(new AttributeIndexOptions(Type: "PLAYER"));
		await Assert.That(await EqualAsync("SEX", "plural")).IsEquivalentTo([1]);

		await SetAsync(1, "SEX", "Neuter");
		await Assert.That(await EqualAsync("SEX", "plural")).IsEmpty();
		await Assert.That(await EqualAsync("SEX", "neuter")).IsEquivalentTo([1]);
	}

	[Test]
	public async Task UsageReportsEachIndex()
	{
		await IndexAsync("IDXUSAGE");
		for (var i = 0; i < 3; i++)
		{
			await SetAsync(await ThingAsync("Usage" + i), "IDXUSAGE", "value" + i);
		}

		var usage = Db.GetAttributeIndexUsage().Single(u => u.Name == "IDXUSAGE");
		await Assert.That(usage.Rows).IsEqualTo(3);
		await Assert.That(usage.Ready).IsTrue();
		await Assert.That(usage.Bytes).IsGreaterThan(0);
		await Assert.That(await Db.CountAttributeCopiesAsync("IDXUSAGE", "THING")).IsEqualTo(3);
	}
}
