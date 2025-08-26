using SharpMUSH.Configuration;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Configuration;

public class ConfigurationTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => (IMUSHCodeParser)WebAppFactoryArg.Services.GetService(typeof(IMUSHCodeParser))!;

	[Test]
	public async Task ParseConfigurationFile()
	{
		var configFile = Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");
		var configReader = new ReadPennMushConfig(configFile);
		var options = configReader.Create(string.Empty);

		await Assert.That(options.Chat.ChatTokenAlias).IsEqualTo('+');
		await Assert.That(options.Net.MudName).IsEqualTo("PennMUSH Emulation by SharpMUSH");
	}

	[Test]
	public async Task CanUseOptionsFromServer()
	{
		var parser = Parser;

		await Assert.That(parser.Configuration.CurrentValue.Chat.ChatTokenAlias).IsEqualTo('+');
		await Assert.That(parser.Configuration.CurrentValue.Net.MudName).IsEqualTo("PennMUSH Emulation by SharpMUSH");
	} 
}