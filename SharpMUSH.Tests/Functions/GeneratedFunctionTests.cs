using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Tests to verify that version() and config() functions work correctly
/// with code-generated accessors instead of reflection.
/// </summary>
public class GeneratedFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	#region version() Function Tests

	[Test]
	public async Task Version_ReturnsConsistentValue()
	{
		var result1 = await Parser.EvaluateAsync(MarkupText.Plain("version()"));
		var result2 = await Parser.EvaluateAsync(MarkupText.Plain("version()"));

		await Assert.That(result1.ToPlainText()).IsEqualTo(result2.ToPlainText());
	}

	[Test]
	public async Task Version_UsesGeneratedCode()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("version()"));
		var versionText = result.ToPlainText();

		var generatedVersion = SharpMUSH.Implementation.Generated.VersionInfo.Version;
		await Assert.That(versionText).IsEqualTo(generatedVersion);
	}

	#endregion

	#region config() Function Tests

	[Test]
	public async Task Config_NoArgs_ReturnsListOfOptions()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("config()"));
		var resultText = result.ToPlainText();

		await Assert.That(resultText).IsNotEmpty();

		await Assert.That(resultText).Contains("mud_name");
		await Assert.That(resultText).Contains("player_start");
	}

	[Test]
	public async Task Config_ValidOption_ReturnsValue()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("config(mud_name)"));
		var resultText = result.ToPlainText();

		await Assert.That(resultText).IsNotEmpty();
		await Assert.That(resultText).IsEqualTo("PennMUSH Emulation by SharpMUSH");
	}

	[Test]
	public async Task Config_InvalidOption_ReturnsError()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("config(invalid_option_xyz_123)"));
		var resultText = result.ToPlainText();

		await Assert.That(resultText).Contains("#-1 NO SUCH OPTION");
	}

	[Test]
	public async Task Config_CaseInsensitive_ReturnsValue()
	{
		var result1 = await Parser.EvaluateAsync(MarkupText.Plain("config(mud_name)"));
		var result2 = await Parser.EvaluateAsync(MarkupText.Plain("config(MUD_NAME)"));
		var result3 = await Parser.EvaluateAsync(MarkupText.Plain("config(Mud_Name)"));

		await Assert.That(result1.ToPlainText()).IsEqualTo(result2.ToPlainText());
		await Assert.That(result1.ToPlainText()).IsEqualTo(result3.ToPlainText());
	}

	/// <summary>
	/// PennMUSH prints a boolean option as Yes or No, never the language's own spelling of the bit
	/// (<c>help config()</c>; <c>cf_bool</c> in <c>display_config_value</c>, <c>src/conf.c:1705</c>).
	/// Observed on 1.8.8 (<c>80a1d5b9</c>): <c>think config(exits_connect_rooms)</c> → <c>No</c>.
	/// </summary>
	[Test]
	public async Task Config_BooleanOption_ReadsYesOrNo()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("config(noisy_whisper)"));
		var resultText = result.ToPlainText();

		await Assert.That(resultText).IsIn("Yes", "No");
	}

	/// <summary>
	/// A plain numeric option carries no <c>#</c> — only a dbref-typed one does
	/// (<see cref="ConfigDbrefOptionTests"/>). Observed on 1.8.8 (<c>80a1d5b9</c>):
	/// <c>think config(max_aliases)</c> → <c>3</c>.
	/// </summary>
	[Test]
	public async Task Config_NumericOption_ReturnsBareNumber()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("config(max_aliases)"));
		var resultText = result.ToPlainText();

		await Assert.That(uint.TryParse(resultText, out _)).IsTrue();
	}

	[Test]
	public async Task Config_AllOptionsListed_CanBeQueried()
	{
		var listResult = await Parser.EvaluateAsync(MarkupText.Plain("config()"));
		var optionsList = listResult.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries);

		var optionsToTest = optionsList.Take(5);

		foreach (var option in optionsToTest)
		{
			var result = await Parser.EvaluateAsync(MarkupText.Plain($"config({option})"));
			var resultText = result.ToPlainText();

			await Assert.That(resultText).DoesNotContain("#-1 NO SUCH OPTION");
		}
	}

	[Test]
	public async Task Config_UsesGeneratedAccessor()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("config(mud_name)"));
		var resultText = result.ToPlainText();

		var options = WebAppFactoryArg.Services.GetRequiredService<
			SharpMUSH.Library.Services.Interfaces.IOptionsWrapper<
				SharpMUSH.Configuration.Options.SharpMUSHOptions>>().CurrentValue;

		var propertyName = SharpMUSH.Configuration.Generated.ConfigMetadata.AttributeToPropertyName["mud_name"];
		var expectedValue = SharpMUSH.Configuration.Generated.ConfigAccessor.GetValue(options, propertyName);
		var metadata = SharpMUSH.Configuration.Generated.ConfigMetadata.PropertyMetadata[propertyName];

		await Assert.That(resultText).IsEqualTo(SharpMUSH.Configuration.ConfigValueDisplay.Format(expectedValue, metadata));
	}

	#endregion
}
