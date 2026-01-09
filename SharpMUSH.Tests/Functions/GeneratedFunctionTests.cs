using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Tests to verify that version() and config() functions work correctly
/// with code-generated accessors instead of reflection.
/// </summary>
public class GeneratedFunctionTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	#region version() Function Tests

	[Test]
	public async Task Version_ReturnsNonEmptyString()
	{
		// Verify that version() returns a version string using generated VersionInfo
		var result = (await Parser.FunctionParse(MModule.single("version()")))?.Message!;
		var versionText = result.ToPlainText();
		
		await Assert.That(versionText).IsNotEmpty();
		await Assert.That(versionText).IsNotNull();
	}

	[Test]
	public async Task Version_ReturnsConsistentValue()
	{
		// Verify that version() returns the same value on multiple calls
		var result1 = (await Parser.FunctionParse(MModule.single("version()")))?.Message!;
		var result2 = (await Parser.FunctionParse(MModule.single("version()")))?.Message!;
		
		await Assert.That(result1.ToPlainText()).IsEqualTo(result2.ToPlainText());
	}

	[Test]
	public async Task Version_UsesGeneratedCode()
	{
		// Verify that the version matches what's in the generated VersionInfo class
		var result = (await Parser.FunctionParse(MModule.single("version()")))?.Message!;
		var versionText = result.ToPlainText();
		
		// The version should match the generated VersionInfo.Version constant
		var generatedVersion = SharpMUSH.Implementation.Generated.VersionInfo.Version;
		await Assert.That(versionText).IsEqualTo(generatedVersion);
	}

	#endregion

	#region config() Function Tests

	[Test]
	public async Task Config_NoArgs_ReturnsListOfOptions()
	{
		// Verify that config() without arguments returns a list of all config options
		var result = (await Parser.FunctionParse(MModule.single("config()")))?.Message!;
		var resultText = result.ToPlainText();
		
		await Assert.That(resultText).IsNotEmpty();
		
		// Should contain some known config option names (in lowercase)
		await Assert.That(resultText).Contains("mud_name");
		await Assert.That(resultText).Contains("player_start");
	}

	[Test]
	public async Task Config_ValidOption_ReturnsValue()
	{
		// Verify that config() with a valid option name returns the option value
		var result = (await Parser.FunctionParse(MModule.single("config(mud_name)")))?.Message!;
		var resultText = result.ToPlainText();
		
		await Assert.That(resultText).IsNotEmpty();
		await Assert.That(resultText).IsEqualTo("PennMUSH Emulation by SharpMUSH");
	}

	[Test]
	public async Task Config_InvalidOption_ReturnsError()
	{
		// Verify that config() with an invalid option name returns an error
		var result = (await Parser.FunctionParse(MModule.single("config(invalid_option_xyz_123)")))?.Message!;
		var resultText = result.ToPlainText();
		
		await Assert.That(resultText).Contains("#-1 NO SUCH OPTION");
	}

	[Test]
	public async Task Config_CaseInsensitive_ReturnsValue()
	{
		// Verify that config() is case-insensitive for option names
		var result1 = (await Parser.FunctionParse(MModule.single("config(mud_name)")))?.Message!;
		var result2 = (await Parser.FunctionParse(MModule.single("config(MUD_NAME)")))?.Message!;
		var result3 = (await Parser.FunctionParse(MModule.single("config(Mud_Name)")))?.Message!;
		
		await Assert.That(result1.ToPlainText()).IsEqualTo(result2.ToPlainText());
		await Assert.That(result1.ToPlainText()).IsEqualTo(result3.ToPlainText());
	}

	[Test]
	public async Task Config_BooleanOption_ReturnsCorrectValue()
	{
		// Verify that config() returns correct values for boolean options
		var result = (await Parser.FunctionParse(MModule.single("config(noisy_whisper)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return either "True" or "False"
		await Assert.That(resultText).IsIn("True", "False");
	}

	[Test]
	public async Task Config_NumericOption_ReturnsCorrectValue()
	{
		// Verify that config() returns correct values for numeric options
		var result = (await Parser.FunctionParse(MModule.single("config(player_start)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should be a number
		await Assert.That(uint.TryParse(resultText, out _)).IsTrue();
	}

	[Test]
	public async Task Config_AllOptionsListed_CanBeQueried()
	{
		// Verify that all options listed by config() can be queried individually
		var listResult = (await Parser.FunctionParse(MModule.single("config()")))?.Message!;
		var optionsList = listResult.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries);
		
		// Test a few random options from the list
		var optionsToTest = optionsList.Take(5);
		
		foreach (var option in optionsToTest)
		{
			var result = (await Parser.FunctionParse(MModule.single($"config({option})")))?.Message!;
			var resultText = result.ToPlainText();
			
			// Should not return an error
			await Assert.That(resultText).DoesNotContain("#-1 NO SUCH OPTION");
		}
	}

	[Test]
	public async Task Config_UsesGeneratedAccessor()
	{
		// Verify that config() uses ConfigAccessor to get values
		var result = (await Parser.FunctionParse(MModule.single("config(mud_name)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// The result should match what we get from ConfigAccessor directly
		var options = WebAppFactoryArg.Services.GetRequiredService<
			SharpMUSH.Library.Services.Interfaces.IOptionsWrapper<
				SharpMUSH.Configuration.Options.SharpMUSHOptions>>().CurrentValue;
		
		// Get the property name for "mud_name" attribute
		var propertyName = SharpMUSH.Configuration.Generated.ConfigMetadata.AttributeToPropertyName["mud_name"];
		var expectedValue = SharpMUSH.Configuration.Generated.ConfigAccessor.GetValue(options, propertyName);
		
		await Assert.That(resultText).IsEqualTo(expectedValue?.ToString() ?? "");
	}

	#endregion
}
