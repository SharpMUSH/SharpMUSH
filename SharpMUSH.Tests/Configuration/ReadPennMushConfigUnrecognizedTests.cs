using SharpMUSH.Configuration;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// The directives a configuration names that SharpMUSH has no option for are listed, once each, so an import can say
/// what it left out. Every directive the reader handles some other way is not.
/// </summary>
public class ReadPennMushConfigUnrecognizedTests
{
	[Test]
	public async Task DirectivesWithNoOptionAreListedOnceInOrder()
	{
		var path = Path.Join(Path.GetTempPath(), $"sharpmush-cnf-{Guid.NewGuid():N}.cnf");
		await File.WriteAllLinesAsync(path,
		[
			"# a comment",
			"mud_name Unrecognized",
			"chunk_swap_file data/chunkswap",
			"restrict_command @dig builder",
			"restrict_attrib FOO wizard",
			"mssp genre/Fantasy",
			"sql_password hunter2",
			"include alias.cnf",
			"forking_dump yes",
			"chunk_swap_file data/other"
		]);
		try
		{
			var import = ReadPennMushConfig.Import(path, followIncludes: false);
			await Assert.That(import.Unrecognized).IsEquivalentTo(new[] { "chunk_swap_file", "forking_dump" },
				TUnit.Assertions.Enums.CollectionOrdering.Matching);
		}
		finally
		{
			File.Delete(path);
		}
	}
}
