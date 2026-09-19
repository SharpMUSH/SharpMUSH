using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@attribute/enum</c> as PennMUSH enforces it: <c>check_attr_value</c> (<c>src/atr_tab.c:504-600</c>)
/// runs inside <c>do_set_atr</c> (<c>src/attrib.c:2363</c>), so every player-facing set is refused unless
/// the value names one of the choices. The match is case-insensitive, an exact choice wins over a
/// prefix, a prefix picks the first choice it starts, and what is stored is the choice as the enum
/// spells it. <c>valid(attrvalue, …)</c> asks the same question (<c>src/funmisc.c:103</c>).
/// </summary>
[NotInParallel]
public class AttributeValueRestrictionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;

	private async Task<string> EnumAttributeAsync(string choices)
	{
		var name = TestIsolationHelpers.GenerateUniqueName("ENUM").ToUpperInvariant();
		await CommandAsync($"@attribute/enum {name}={choices}");
		return name;
	}

	[Test]
	[Arguments("green", "Green")]
	[Arguments("GR", "Green")]
	[Arguments("b", "Blue")]
	[Arguments("Blue", "Blue")]
	public async Task Set_StoresTheChoiceAsTheEnumSpellsIt(string typed, string stored)
	{
		var attr = await EnumAttributeAsync("Red Green Blue Blueberry");
		var thing = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "EnumSet");

		await CommandAsync($"&{attr} {thing}={typed}");

		await Assert.That(await FunctionAsync($"get({thing}/{attr})")).IsEqualTo(stored);
	}

	[Test]
	public async Task Set_AnExactChoiceWinsOverAnEarlierChoiceItPrefixes()
	{
		var attr = await EnumAttributeAsync("Blueberry Blue");
		var thing = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "EnumExact");

		await CommandAsync($"&{attr} {thing}=blue");

		await Assert.That(await FunctionAsync($"get({thing}/{attr})")).IsEqualTo("Blue");
	}

	[Test]
	[Arguments("purple")]
	[Arguments("red green")]
	public async Task Set_RefusesAValueThatNamesNoChoice_AndKeepsTheOldOne(string typed)
	{
		var attr = await EnumAttributeAsync("Red Green Blue");
		var thing = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "EnumRefuse");
		await CommandAsync($"&{attr} {thing}=red");

		await CommandAsync($"&{attr} {thing}={typed}");

		await Assert.That(await FunctionAsync($"get({thing}/{attr})")).IsEqualTo("Red");
		await NotifyService.Received().Notify(
			Arg.Any<AnySharpObject>(),
			Arg.Is<SharpMessage>(m =>
				TestHelpers.MessagePlainTextEquals(m, $"Value for {attr} needs to be one of: Red Green Blue")),
			Arg.Any<AnySharpObject?>(),
			Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Arguments("gr", "1")]
	[Arguments("GREEN", "1")]
	[Arguments("purple", "0")]
	[Arguments("red green", "0")]
	public async Task Valid_AsksTheSameQuestion(string typed, string expected)
	{
		var attr = await EnumAttributeAsync("Red Green Blue");

		await Assert.That(await FunctionAsync($"valid(attrvalue,{typed},{attr})")).IsEqualTo(expected);
	}

	private ValueTask<CallState> CommandAsync(string command) =>
		CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private async Task<string> FunctionAsync(string expression) =>
		(await FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();
}
