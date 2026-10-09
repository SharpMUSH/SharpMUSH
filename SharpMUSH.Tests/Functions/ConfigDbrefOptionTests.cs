using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// A dbref-typed config option prints with its <c>#</c>. PennMUSH decides that by handler: the
/// twelve options registered with <c>cf_dbref</c> go through the <c>#%d</c> branch of
/// <c>display_config_value</c> (<c>pennmush/src/conf.c:1707</c>), which is what lets softcode write
/// <c>name(config(master_room))</c> or <c>isdbref(config(...))</c> and have it mean anything.
///
/// Expected values observed on PennMUSH 1.8.8 (<c>80a1d5b9</c>) with the shipped <c>mushcnf.dst</c>,
/// as God over telnet:
/// <code>
/// config(player_start)   => '#0'    config(ancestor_room)   => '#-1'
/// config(master_room)    => '#2'    config(ancestor_exit)   => '#-1'
/// config(base_room)      => '#0'    config(event_handler)   => '#-1'
/// config(default_home)   => '#0'    config(http_handler)    => '#0'
/// config(probate_judge)  => '#1'    config(max_dbref)       => '#0'
/// isdbref(config(master_room)) => '1'   name(config(master_room)) => 'Master Room'
/// </code>
/// The dbrefs themselves are SharpMUSH's own (it seeds more objects than PennMUSH does, and
/// <c>help pennmush compatibility</c> records that); only the <c>#&lt;n&gt;</c> shape is the parity
/// claim here.
/// </summary>
public class ConfigDbrefOptionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	/// <summary>
	/// The options PennMUSH registers with <c>cf_dbref</c> (<c>pennmush/src/conf.c:122-133, 202, 281</c>).
	/// </summary>
	private static readonly string[] PennMUSHDbrefOptions =
	[
		"player_start", "master_room", "base_room", "default_home",
		"ancestor_room", "ancestor_exit", "ancestor_thing", "ancestor_player",
		"event_handler", "http_handler", "max_dbref", "probate_judge"
	];

	/// <summary>
	/// Each dbref option's value as <c>config()</c> prints it. The numbers are SharpMUSH's seeded
	/// objects, from the shipped <c>mushcnf.dst</c>; the <c>#</c> in front of each is PennMUSH's.
	/// </summary>
	[Test]
	[Arguments("player_start", "#0")]
	[Arguments("master_room", "#2")]
	[Arguments("base_room", "#0")]
	[Arguments("default_home", "#0")]
	[Arguments("ancestor_room", "#3")]
	[Arguments("ancestor_player", "#4")]
	[Arguments("ancestor_exit", "#5")]
	[Arguments("ancestor_thing", "#6")]
	[Arguments("package_manager", "#7")]
	[Arguments("http_handler", "#8")]
	[Arguments("event_handler", "#9")]
	[Arguments("probate_judge", "#1")]
	[Arguments("max_dbref", "#0")]
	public async Task DbrefOptionCarriesItsHash(string option, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain($"config({option})")))!.Message;

		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>The point of the <c>#</c>: a dbref option's value is a dbref to every other function.</summary>
	[Test]
	public async Task DbrefOptionFeedsDbrefFunctions()
	{
		var isDbref = (await Parser.FunctionParse(MarkupText.Plain("isdbref(config(master_room))")))!.Message;
		var name = (await Parser.FunctionParse(MarkupText.Plain("name(config(master_room))")))!.Message;

		await Assert.That(isDbref.ToPlainText()).IsEqualTo("1");
		await Assert.That(name.ToPlainText()).IsNotEmpty();
	}

	/// <summary>
	/// Every option PennMUSH types as a dbref is declared as one here, so adding a core-room or
	/// handler option cannot quietly go back to printing a bare number.
	/// </summary>
	[Test]
	public async Task EveryPennMUSHDbrefOptionIsDeclaredAsOne()
	{
		var undeclared = PennMUSHDbrefOptions
			.Where(option => !ConfigMetadata.PropertyMetadata[ConfigMetadata.AttributeToPropertyName[option]].Dbref)
			.ToArray();

		await Assert.That(undeclared).IsEmpty();
	}

	/// <summary>
	/// An unset dbref option holds PennMUSH's <c>NOTHING</c> and prints <c>#-1</c> — the same text
	/// <c>@config/set &lt;option&gt;=-1</c> stores as null, so the two round-trip. Every dbref option in
	/// the shipped <c>mushcnf.dst</c> names an object, so this case only exists at the formatter.
	/// </summary>
	[Test]
	[Arguments(null, "#-1")]
	[Arguments(0u, "#0")]
	[Arguments(9u, "#9")]
	public async Task FormatterWritesDbrefValues(object? value, string expected)
		=> await Assert.That(ConfigValueDisplay.Format(value, DbrefMetadata(dbref: true))).IsEqualTo(expected);

	/// <summary>
	/// A boolean reads Yes or No (<c>help config()</c>; <c>src/conf.c:1705</c>), and nothing else is
	/// reshaped — a plain number stays bare, so only the declared dbref options gain a <c>#</c>.
	/// </summary>
	[Test]
	[Arguments(true, "Yes")]
	[Arguments(false, "No")]
	[Arguments(3u, "3")]
	[Arguments("Penny", "Penny")]
	[Arguments(null, "")]
	public async Task FormatterWritesNonDbrefValues(object? value, string expected)
		=> await Assert.That(ConfigValueDisplay.Format(value, DbrefMetadata(dbref: false))).IsEqualTo(expected);

	/// <summary>An option declaration that differs from its neighbours only in naming an object.</summary>
	private static SharpConfigAttribute DbrefMetadata(bool dbref) => new()
	{
		Name = "test_option",
		Description = "",
		Category = "Database",
		Dbref = dbref
	};
}
