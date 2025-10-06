using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Configuration;

public class ConfigurationTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }
	private IOptionsMonitor<SharpMUSHOptions> Configuration => WebAppFactoryArg.Services.GetRequiredService<IOptionsMonitor<SharpMUSHOptions>>();

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