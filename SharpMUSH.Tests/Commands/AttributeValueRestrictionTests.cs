using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Definitions;
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

	/// <summary>A fresh attribute-table entry. Penn's /limit and /enum refuse an attribute that is not in the table.</summary>
	private async Task<string> TableAttributeAsync(string prefix)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix).ToUpperInvariant();
		await CommandAsync($"@attribute/access {name}=");
		return name;
	}

	private async Task<string> EnumAttributeAsync(string choices, string delimiter = "")
	{
		var name = await TableAttributeAsync("ENUM");
		await CommandAsync($"@attribute/enum {delimiter}{(delimiter.Length > 0 ? " " : "")}{name}={choices}");
		return name;
	}

	private bool Told(string key, string text) =>
		TestHelpers.ReceivedNotifyLocalizedRendering(NotifyService, key, text);

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

	// ---- @attribute/limit and /enum as commands (src/atr_tab.c do_attribute_limit) ----------------

	[Test]
	public async Task Limit_IsSetAndEnforcedCaselessly()
	{
		var attr = await TableAttributeAsync("LIMIT");
		var thing = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "LimitSet");

		await CommandAsync($"@attribute/limit {attr}=^%[a-z%]+$");
		await CommandAsync($"&{attr} {thing}=ABC");
		await CommandAsync($"&{attr} {thing}=abc1");

		await Assert.That(Told(nameof(ErrorMessages.Notifications.AttributeCommandRestrictionSetFormat),
			$"{attr} -- Attribute limit set to: ^[a-z]+$")).IsTrue();
		await Assert.That(await FunctionAsync($"get({thing}/{attr})")).IsEqualTo("ABC");
	}

	[Test]
	public async Task Limit_ThatDoesNotCompile_IsRefused()
	{
		var attr = await TableAttributeAsync("LIMITBAD");

		await CommandAsync($"@attribute/limit {attr}=(unclosed");

		await Assert.That(Told(nameof(ErrorMessages.Notifications.AttributeCommandInvalidRegexp), "Invalid Regular Expression."))
			.IsTrue();
		await Assert.That(await FunctionAsync($"valid(attrvalue,anything,{attr})")).IsEqualTo("1");
	}

	[Test]
	public async Task Enum_TakesADelimiter_AndItsChoicesMayHoldSpaces()
	{
		var attr = await EnumAttributeAsync("light blue|dark red", delimiter: "|");
		var thing = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "EnumDelim");

		await CommandAsync($"&{attr} {thing}=dark");
		await Assert.That(await FunctionAsync($"get({thing}/{attr})")).IsEqualTo("dark red");

		await CommandAsync($"&{attr} {thing}=LIGHT BLUE");
		await Assert.That(await FunctionAsync($"get({thing}/{attr})")).IsEqualTo("light blue");

		await Assert.That(await FunctionAsync($"valid(attrvalue,light|dark,{attr})")).IsEqualTo("0");
	}

	[Test]
	public async Task Enum_DelimiterMustBeOneCharacter()
	{
		var attr = await TableAttributeAsync("ENUMDELIM");

		await CommandAsync($"@attribute/enum || {attr}=a||b");

		await Assert.That(Told(nameof(ErrorMessages.Notifications.AttributeCommandDelimiterOneCharacter),
			"Delimiter must be one character.")).IsTrue();
		await Assert.That(await FunctionAsync($"valid(attrvalue,zzz,{attr})")).IsEqualTo("1");
	}

	[Test]
	public async Task EmptyRestriction_Unsets()
	{
		var attr = await EnumAttributeAsync("Red Green");

		await CommandAsync($"@attribute/enum {attr}=");

		await Assert.That(Told(nameof(ErrorMessages.Notifications.AttributeCommandRestrictionUnsetFormat),
			$"{attr} -- Attribute limit or enum unset.")).IsTrue();
		await Assert.That(await FunctionAsync($"valid(attrvalue,purple,{attr})")).IsEqualTo("1");
	}

	[Test]
	public async Task Restriction_OnAnAttributeNotInTheTable_IsRefused()
	{
		var attr = TestIsolationHelpers.GenerateUniqueName("NOTABLE").ToUpperInvariant();

		await CommandAsync($"@attribute/enum {attr}=a b");

		await Assert.That(Told(nameof(ErrorMessages.Notifications.AttributeCommandNotInTableUseAccess),
			"I don't know that attribute. Please use @attribute/access to create it, first.")).IsTrue();
		await Assert.That(await FunctionAsync($"valid(attrvalue,zzz,{attr})")).IsEqualTo("1");
	}

	[Test]
	public async Task LimitAndEnum_ReplaceEachOther()
	{
		var attr = await EnumAttributeAsync("Red Green");

		await CommandAsync($"@attribute/limit {attr}=^%[0-9%]+$");

		await Assert.That(await FunctionAsync($"valid(attrvalue,red,{attr})")).IsEqualTo("0");
		await Assert.That(await FunctionAsync($"valid(attrvalue,42,{attr})")).IsEqualTo("1");
	}

	[Test]
	public async Task Access_KeepsTheEnum()
	{
		var attr = await EnumAttributeAsync("Red Green");

		await CommandAsync($"@attribute/access {attr}=no_clone");

		await Assert.That(await FunctionAsync($"valid(attrvalue,purple,{attr})")).IsEqualTo("0");
	}

	// ---- @attribute/decompile <pattern> is quick_wild (src/atr_tab.c:1017) -------------------------

	[Test]
	public async Task Decompile_MatchesTheWholeNameAsAWildcard()
	{
		var stem = TestIsolationHelpers.GenerateUniqueName("DECO").ToUpperInvariant();
		await CommandAsync($"@attribute/access {stem}=");
		await CommandAsync($"@attribute/access {stem}X=");
		await CommandAsync($"@attribute/enum | {stem}X=a b|c");

		await CommandAsync($"@attribute/decompile {stem}");
		await CommandAsync($"@attribute/decompile {stem}?");

		await Assert.That(Told(nameof(ErrorMessages.Notifications.AttributeCommandDecompileHeaderFormat),
			$"@attribute/decompile: 1 attributes match pattern '{stem}'")).IsTrue();
		await Assert.That(Told(nameof(ErrorMessages.Notifications.AttributeCommandDecompileHeaderFormat),
			$"@attribute/decompile: 1 attributes match pattern '{stem}?'")).IsTrue();
		await Assert.That(Told(nameof(ErrorMessages.Notifications.AttributeCommandDecompileEnumFormat),
			$"@attribute/enum | {stem}X=a b|c")).IsTrue();
	}

	// ---- As a mortal: God passes every permission check vacuously ----------------------------------

	[Test]
	public async Task Mortal_CannotSetARestriction()
	{
		var attr = await TableAttributeAsync("MORTLIM");
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>(), ConnectionService, "LimitMortal");

		await CommandParser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@attribute/limit {attr}=^x$"));
		await CommandParser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@attribute/enum {attr}=x"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.PermissionDenied), mortal.DbRef)).IsTrue();
		await Assert.That(await FunctionAsync($"valid(attrvalue,anything,{attr})")).IsEqualTo("1");
	}

	[Test]
	public async Task Mortal_WritesAreHeldToTheLimit()
	{
		var attr = await TableAttributeAsync("MORTWRITE");
		await CommandAsync($"@attribute/limit {attr}=^%[0-9%]+$");
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>(), ConnectionService, "LimitWriter");
		var thing = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "LimitMortalThing");
		await CommandAsync($"@chown {thing}={mortal.DbRef}");

		await CommandParser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"&{attr} {thing}=42"));
		await CommandParser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"&{attr} {thing}=forty-two"));

		await Assert.That(await FunctionAsync($"get({thing}/{attr})")).IsEqualTo("42");
	}

	private ValueTask<CallState> CommandAsync(string command) =>
		CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private async Task<string> FunctionAsync(string expression) =>
		(await FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();
}
