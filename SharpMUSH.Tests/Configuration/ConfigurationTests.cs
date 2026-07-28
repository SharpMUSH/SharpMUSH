using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Configuration;

public class ConfigurationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }
	private IOptionsWrapper<SharpMUSHOptions> Configuration => WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();

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

	[Test]
	public async Task WikiDefaultLocale_DefaultsToEnglish()
	{
		var configFile = Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");
		var options = ReadPennMushConfig.Create(configFile);

		await Assert.That(options.Wiki.DefaultLocale).IsEqualTo("en");
	}

	[Test]
	public async Task WikiDefaultLocale_IsExposedThroughTheSchemaAccessor()
	{
		var configFile = Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");
		var options = ReadPennMushConfig.Create(configFile);

		var value = ConfigAccessor.GetValue(options, nameof(WikiOptions.DefaultLocale));

		await Assert.That(value).IsEqualTo("en");
		await Assert.That(ConfigAccessor.GetCategoryForProperty(nameof(WikiOptions.DefaultLocale))).IsEqualTo("Wiki");
	}

	[Test]
	public async Task WikiDefaultLocale_IsARealParameterDefaultNotARequiredMember()
	{
		// Constructing WikiOptions with no argument must compile and must yield the documented default.
		// If someone later makes DefaultLocale `required`, this line stops compiling — which is the point.
		//
		// The const's literal value is pinned by WikiDefaultLocale_DefaultsToEnglish instead of here:
		// ReadPennMushConfig uses DefaultLocaleFallback as its fallback, so that test fails if the const
		// stops being "en". Asserting the const against "en" directly trips TUnitAssertions0005.
		await Assert.That(new WikiOptions().DefaultLocale).IsEqualTo(WikiOptions.DefaultLocaleFallback);
	}

	[Test]
	public async Task ValidateSharpOptions_RejectsAnUnparseableWikiDefaultLocale()
	{
		var options = TestSharpMushOptions.Create(wikiDefaultLocale: "not a locale");

		var result = new ValidateSharpOptions().Validate(null, options);

		await Assert.That(result.Failed).IsTrue();
		await Assert.That(result.FailureMessage)
			.Contains("not a locale")
			.Because("a startup failure that does not name the offending value is a scavenger hunt");
	}

	[Test]
	public async Task ValidateSharpOptions_AcceptsARegionalWikiDefaultLocale()
	{
		var result = new ValidateSharpOptions().Validate(null, TestSharpMushOptions.Create(wikiDefaultLocale: "pt-BR"));

		await Assert.That(result.Failed).IsFalse();
	}
}