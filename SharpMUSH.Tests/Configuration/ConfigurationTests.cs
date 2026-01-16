using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Configuration;

public class ConfigurationTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }
	private IOptionsWrapper<SharpMUSHOptions> Configuration => Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();

	[Test]
	public async Task ParseConfigurationFile()
	{
		var configFile = Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");
		var options = ReadPennMushConfig.Create(configFile);

		await Assert.That(options.Chat.ChatTokenAlias).IsEqualTo('+');
		await Assert.That(options.Net.MudName).IsEqualTo("PennMUSH Emulation by SharpMUSH");
	}

	[Test]
	public async Task CanUseOptionsFromServer()
	{
		await Assert.That(Configuration.CurrentValue.Chat.ChatTokenAlias).IsEqualTo('+');
		await Assert.That(Configuration.CurrentValue.Net.MudName).IsEqualTo("PennMUSH Emulation by SharpMUSH");
	} 
}