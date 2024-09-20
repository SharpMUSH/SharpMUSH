namespace SharpMUSH.Tests.Internal;

public class NameList
{
	[Test]
	[Arguments("God", "God")]
	public async Task SingleString(string str, string expected)
	{
		var result = Implementation.Functions.Functions.NameList(str);

		await Assert
			.That(result.Single().AsT1)
			.IsEqualTo(expected);
	}

	[Test]
	[Arguments("#1", 1)]
	public async Task SingleDBRef(string str, int expected)
	{
		var result = Implementation.Functions.Functions.NameList(str);

		await Assert
			.That(result.Single().AsT0)
			.IsEqualTo(new Library.Models.DBRef(expected));
	}

	[Test]
	[Arguments("#1:999", 1, 999)]
	public async Task SingleDBRefWithTimestamp(string str, int expectedDbRef, int expectedTimestamp)
	{
		var result = Implementation.Functions.Functions.NameList(str);

		await Assert
			.That(result.Single().AsT0)
			.IsEqualTo(new Library.Models.DBRef(expectedDbRef, expectedTimestamp));
	}
}