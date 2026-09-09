using SharpMUSH.Configuration;

namespace SharpMUSH.Tests.Configuration;

public class ReadPennMushConfigAliasTests
{
	[Test]
	public async Task DefaultAliasesDoNotShadowTheStandaloneInsertFunction()
	{
		var path = Path.GetTempFileName();
		try
		{
			var options = ReadPennMushConfig.Create(path);
			await Assert.That(options.Alias.FunctionAliases.Values.SelectMany(aliases => aliases)
				.Contains("insert", StringComparer.OrdinalIgnoreCase)).IsFalse();
		}
		finally { File.Delete(path); }
	}
}
