using SharpMUSH.Configuration;

namespace SharpMUSH.Tests.Configuration;

public class ConfigurationTests : BaseUnitTest
{
	[Test]
	public async Task ParseConfigurationFile()
	{
		var configFile = Path.Combine(Directory.GetCurrentDirectory(), "Configuration", "Testfile", "mushcnf.dst");
		var configReader = new ReadPennMushConfig(configFile);
		var options = configReader.Create(string.Empty);

		await Assert.That(options.Chat.ChatTokenAlias).IsEqualTo('+');
		await Assert.That(options.Net.MudName).IsEqualTo("PennMUSH Emulation by SharpMUSH");
	}

	[Test]
	public async Task CanUseOptionsFromServer()
	{
		var parser = await TestParser();

		await Assert.That(parser.Configuration.CurrentValue.Chat.ChatTokenAlias).IsEqualTo('+');
		await Assert.That(parser.Configuration.CurrentValue.Net.MudName).IsEqualTo("PennMUSH Emulation by SharpMUSH");
	} 
}