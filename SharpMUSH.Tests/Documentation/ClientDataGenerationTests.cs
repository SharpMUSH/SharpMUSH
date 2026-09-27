namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// The checked-in editor data files are exactly what <see cref="ClientDataGenerator"/> builds from the
/// registries and the helpfiles. A failure here means an attribute or a helpfile changed without the
/// data following it: regenerate with <c>SHARPMUSH_REGENERATE_CLIENT_DATA=1</c> and commit the result.
/// </summary>
public class ClientDataGenerationTests
{
	[Test]
	[Arguments("mush-defs.json")]
	[Arguments("mush-functions.json")]
	[Arguments("mush-commands.json")]
	public async Task CheckedInFileMatchesTheGeneratedOne(string file)
	{
		var expected = ClientDataGenerator.Files()[file];
		if (Environment.GetEnvironmentVariable("SHARPMUSH_REGENERATE_CLIENT_DATA") == "1")
			await File.WriteAllTextAsync(Path.Join(ClientDataGenerator.DataDirectory, file), expected);

		var actual = File.ReadAllText(Path.Join(ClientDataGenerator.DataDirectory, file)).Replace("\r\n", "\n");

		await Assert.That(actual == expected).IsTrue()
			.Because($"{file} is generated; run with SHARPMUSH_REGENERATE_CLIENT_DATA=1 and commit the result");
	}
}
