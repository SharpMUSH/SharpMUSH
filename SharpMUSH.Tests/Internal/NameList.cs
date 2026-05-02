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
			.That(result.Single().AsName)
			.IsEqualTo(expected);
	}

	[Test]
	[Arguments("#1", 1)]
	public async Task SingleDBRef(string str, int expected)
	{
		var result = ArgHelpers.NameList(str);

		await Assert
			.That(result.Single().AsDBRef)
			.IsEquatableOrEqualTo(new DBRef(expected));
	}

	[Test]
	[Arguments("#1:999", 1, 999)]
	public async Task SingleDBRefWithTimestamp(string str, int expectedDbRef, int expectedTimestamp)
	{
		var result = ArgHelpers.NameList(str);

		await Assert
			.That(result.Single().AsDBRef)
			.IsEquatableOrEqualTo(new DBRef(expectedDbRef, expectedTimestamp));
	}
}