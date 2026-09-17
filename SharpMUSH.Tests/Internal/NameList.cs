using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Internal;

public class NameList
{
	[Test]
	[Arguments("God", "God")]
	public async Task SingleString(string str, string expected)
	{
		var result = ArgHelpers.NameList(str);

		await Assert
			.That(result.Single().Expect<string>())
			.IsEqualTo(expected);
	}

	[Test]
	[Arguments("#1", 1)]
	public async Task SingleDBRef(string str, int expected)
	{
		var result = ArgHelpers.NameList(str);

		await Assert
			.That(result.Single().Expect<DBRef>())
			.IsEquatableOrEqualTo(new DBRef(expected));
	}

	[Test]
	[Arguments("#1:999", 1, 999)]
	public async Task SingleDBRefWithTimestamp(string str, int expectedDbRef, int expectedTimestamp)
	{
		var result = ArgHelpers.NameList(str);

		await Assert
			.That(result.Single().Expect<DBRef>())
			.IsEquatableOrEqualTo(new DBRef(expectedDbRef, expectedTimestamp));
	}

	// parse_dbref (parse.c:128-133) accepts only a whole `#nnn` token, so a dbref-shaped prefix does not
	// split a word: `#12Lamp` is one name, not `#12` followed by `Lamp`.
	[Test]
	[Arguments("#12Lamp")]
	[Arguments("#1:999x")]
	public async Task DbRefPrefixedWordIsOneName(string str)
	{
		await Assert.That(ArgHelpers.NameList(str).Single().Expect<string>()).IsEqualTo(str);
		await Assert.That(ArgHelpers.NameListString(str).Single()).IsEqualTo(str);
	}

	[Test]
	public async Task DbRefFollowedByANameIsTwoEntries()
	{
		var result = ArgHelpers.NameList("#1 God").ToList();

		await Assert.That(result[0].Expect<DBRef>()).IsEquatableOrEqualTo(new DBRef(1));
		await Assert.That(result[1].Expect<string>()).IsEqualTo("God");
	}

	// An objid is a whole token too, wherever it sits in the list.
	[Test]
	public async Task ObjIdAmongNamesIsOneEntry()
	{
		var result = ArgHelpers.NameList("God #1:999 \"Brass Lamp\" #2").ToList();

		await Assert.That(result.Count).IsEqualTo(4);
		await Assert.That(result[0].Expect<string>()).IsEqualTo("God");
		await Assert.That(result[1].Expect<DBRef>()).IsEquatableOrEqualTo(new DBRef(1, 999));
		await Assert.That(result[2].Expect<string>()).IsEqualTo("Brass Lamp");
		await Assert.That(result[3].Expect<DBRef>()).IsEquatableOrEqualTo(new DBRef(2));
		await Assert.That(ArgHelpers.NameListString("#1:999 God").ToList()).IsEquivalentTo(["#1:999", "God"]);
	}
}
