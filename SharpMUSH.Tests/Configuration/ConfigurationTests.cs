using SharpMUSH.Configuration;

namespace SharpMUSH.Tests.Configuration;

public class ConfigurationTests
{
	[Test]
	public async Task ParseConfigurationFile()
	{
		var configReader = new ReadPennMUSHConfig();
		var currentDirectory = Directory.GetCurrentDirectory();
		var options = configReader.Create(Path.Combine(currentDirectory, "Configuration", "Testfile", "mushcnf.dst"));

		await Assert.That(options.Chat.ChatTokenAlias).IsEqualTo('+');
		await Assert.That(options.Net.MudName).IsEqualTo("PennMUSH Emulation by SharpMUSH");
	}
}