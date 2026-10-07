using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// A list-valued config option prints its words, not the array's type name. The default-flag
/// options are PennMUSH's <c>cf_flag</c> (<c>pennmush/src/conf.c:709</c>), which appends every
/// configured word after a space, so a non-empty list keeps that leading space when printed.
///
/// Expected values observed on PennMUSH 1.8.8 (<c>80a1d5b9</c>) with the shipped <c>mushcnf.dst</c>,
/// as God over telnet:
/// <code>
/// think [config(room_flags)]            => '[ no_command]'
/// @config room_flags                    => ' room_flags                                no_command'
/// </code>
/// </summary>
public class ConfigListOptionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("player_flags", " enter_ok ansi no_command")]
	[Arguments("room_flags", " no_command")]
	[Arguments("thing_flags", " no_command")]
	[Arguments("channel_flags", " player")]
	public async Task FlagOptionPrintsItsFlags(string option, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain($"[config({option})]")))?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>Every PennMUSH <c>cf_flag</c> option (<c>pennmush/src/conf.c:257-266</c>) is declared as one.</summary>
	[Test]
	[Arguments("player_flags")]
	[Arguments("room_flags")]
	[Arguments("exit_flags")]
	[Arguments("thing_flags")]
	[Arguments("channel_flags")]
	public async Task FlagOptionIsDeclaredAsOne(string option)
		=> await Assert.That(ConfigMetadata.PropertyMetadata[ConfigMetadata.AttributeToPropertyName[option]].Flag).IsTrue();

	/// <summary>
	/// An empty list prints nothing, and a list that is not a flag option has no leading space.
	/// </summary>
	[Test]
	public async Task FormatterWritesLists()
	{
		await Assert.That(ConfigValueDisplay.Format(Array.Empty<string>(), Metadata(flag: true))).IsEqualTo(string.Empty);
		await Assert.That(ConfigValueDisplay.Format(new[] { "wizard", "dark" }, Metadata(flag: true))).IsEqualTo(" wizard dark");
		await Assert.That(ConfigValueDisplay.Format(new[] { "guest", "admin" }, Metadata(flag: false))).IsEqualTo("guest admin");
	}

	/// <summary>A mapping option prints <c>key=values</c> in key order, separated by <c>|</c>.</summary>
	[Test]
	public async Task FormatterWritesMappings()
	{
		var map = new Dictionary<string, string[]> { { "page", ["p"] }, { "@edit", ["@gedit", "@ged"] } };

		await Assert.That(ConfigValueDisplay.Format(map, Metadata(flag: false))).IsEqualTo("@edit=@gedit @ged|page=p");
		await Assert.That(ConfigValueDisplay.Format(new Dictionary<string, string[]>(), Metadata(flag: false))).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task ConfigFunctionPrintsAMapping()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("[config(command_aliases)]")))?.Message!;

		await Assert.That(result.ToPlainText()).Contains("@ATRLOCK=@attrlock|");
	}

	private static SharpConfigAttribute Metadata(bool flag) => new()
	{
		Name = "test_option",
		Description = "",
		Category = "Flag",
		Flag = flag
	};
}
