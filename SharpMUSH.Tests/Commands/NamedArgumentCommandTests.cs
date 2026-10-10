using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>Named arguments from <c>@trigger/args</c>, <c>@include/args</c>, <c>@function/args</c> and <c>stepargs()</c>.</summary>
public class NamedArgumentCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;

	private async Task Run(string command) => await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private async Task<string> Eval(string expression)
		=> (await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

	private Task<DBRef> NewThing(string prefix) => TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, prefix);

	[Test]
	public async Task TriggerArgsPassesNamesAndNoPositions()
	{
		var thing = await NewThing("TrigArgs");
		await Run($"&RUN {thing}=&OUT {thing}=%<who>/%<count>/[%0]");
		await Run($"@trigger/inline/args {thing}/RUN=who,Bob,count,3");
		await Assert.That(await Eval($"get({thing}/OUT)")).IsEqualTo("Bob/3/");
	}

	[Test]
	public async Task TriggerArgsRefusesAnUnpairedName()
	{
		var thing = await NewThing("TrigOdd");
		await Run($"&RUN {thing}=&OUT {thing}=ran");
		await Run($"@trigger/inline/args {thing}/RUN=who");
		await Assert.That(await Eval($"get({thing}/OUT)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task IncludeArgsPassesNames()
	{
		var thing = await NewThing("InclArgs");
		await Run($"&STEP {thing}=&OUT {thing}=%<who>");
		await Run($"@include/args {thing}/STEP=who,Bob");
		await Assert.That(await Eval($"get({thing}/OUT)")).IsEqualTo("Bob");
	}

	[Test]
	public async Task FunctionArgsNamesAGlobalFunctionsArguments()
	{
		var thing = await NewThing("FnArgs");
		var name = $"nf{Guid.NewGuid():N}"[..20];
		await Run($"&FN {thing}=%<who>:%<count>:%0");
		await Run($"@function {name}={thing},FN");
		try
		{
			await Assert.That(await Eval($"{name}(Bob,3)")).IsEqualTo("::Bob");
			await Run($"@function/args {name}=who count");
			await Assert.That(await Eval($"{name}(Bob,3)")).IsEqualTo("Bob:3:Bob");
			await Run($"@function/args {name}=");
			await Assert.That(await Eval($"{name}(Bob,3)")).IsEqualTo("::Bob");
		}
		finally
		{
			await Run($"@function/delete {name}");
		}
	}

	[Test]
	public async Task FunctionArgsLastNameTakesTheRestNumbered()
	{
		var thing = await NewThing("FnRest");
		var name = $"nr{Guid.NewGuid():N}"[..20];
		await Run($"&FN {thing}=%<who>:%<items1>:%<items2>:%<itemscount>:[%<items3>]");
		await Run($"@function {name}={thing},FN");
		try
		{
			await Run($"@function/args {name}=who items...");
			await Assert.That(await Eval($"{name}(Bob,a b,c)")).IsEqualTo("Bob:a b:c:2:");
			await Assert.That(await Eval($"{name}(Bob)")).IsEqualTo("Bob:::0:");
		}
		finally
		{
			await Run($"@function/delete {name}");
		}
	}

	[Test]
	public async Task FunctionArgsTrailingNamesDealTheRestOutInTurn()
	{
		var thing = await NewThing("FnGroups");
		var name = $"ng{Guid.NewGuid():N}"[..20];
		await Run($"&FN {thing}=%<obj>:[iter(lnum(1,%<keycount>),%<key##>=%<value##>+%<arg##>,%b,|)]:%<keycount>%<valuecount>%<argcount>");
		await Run($"@function {name}={thing},FN");
		try
		{
			await Run($"@function/args {name}=obj key... value... arg...");
			await Assert.That(await Eval($"{name}(me,k1,v1,a1,k2,v2,a2)")).IsEqualTo("me:k1=v1+a1|k2=v2+a2:222");
			await Assert.That(await Eval($"{name}(me,k1,v1,a1,k2)")).IsEqualTo("me:k1=v1+a1|k2=+:211");
		}
		finally
		{
			await Run($"@function/delete {name}");
		}
	}

	[Test]
	public async Task FunctionArgsBareRestTakesCallerNamedPairs()
	{
		var thing = await NewThing("FnPairs");
		var name = $"np{Guid.NewGuid():N}"[..20];
		await Run($"&FN {thing}=%<obj>:%<who>:%<count>");
		await Run($"@function {name}={thing},FN");
		try
		{
			await Run($"@function/args {name}=obj ...");
			await Assert.That(await Eval($"{name}(me,who,Bob,count,3)")).IsEqualTo("me:Bob:3");
			await Assert.That(await Eval($"{name}(me,who)")).IsEqualTo(ErrorMessages.Returns.NamedArgumentsComeInPairs);
			await Assert.That(await Eval($"{name}(me,obj,x)")).IsEqualTo(ErrorMessages.Returns.BadArgumentName);
			await Assert.That(await Eval($"{name}(me,0,x)")).IsEqualTo(ErrorMessages.Returns.BadArgumentName);
		}
		finally
		{
			await Run($"@function/delete {name}");
		}
	}

	[Test]
	[Arguments("items... who")]
	[Arguments("...")]
	[Arguments("items items...")]
	[Arguments("key... ...")]
	[Arguments("... ...")]
	public async Task FunctionArgsRefusesAMisplacedOrRepeatedRest(string names)
	{
		var thing = await NewThing("FnRestBad");
		var name = $"nb{Guid.NewGuid():N}"[..20];
		await Run($"&FN {thing}=[%<items1>]x");
		await Run($"@function {name}={thing},FN");
		try
		{
			await Run($"@function/args {name}={names}");
			await Assert.That(await Eval($"{name}(a,b)")).IsEqualTo("x");
		}
		finally
		{
			await Run($"@function/delete {name}");
		}
	}

	[Test]
	public async Task StepArgsPassesEachRunUnderTheNames()
	{
		var thing = await NewThing("StepArgs");
		await Run($"&ROW {thing}=%<name>=%<count>");
		await Assert.That(await Eval($"stepargs({thing}/ROW,Bob 3 Alice 1 Carol,name count,,|)"))
			.IsEqualTo("Bob=3|Alice=1|Carol=");
	}

	[Test]
	[Arguments("")]
	[Arguments("name NAME")]
	public async Task StepArgsRefusesEmptyOrRepeatedNames(string names)
	{
		var thing = await NewThing("StepBad");
		await Run($"&ROW {thing}=x");
		await Assert.That(await Eval($"stepargs({thing}/ROW,a b,{names})")).IsEqualTo(ErrorMessages.Returns.BadArgumentName);
	}

	[Test]
	public async Task StepStillPassesPositions()
	{
		var thing = await NewThing("StepPos");
		await Run($"&ROW {thing}=%0-%1");
		await Assert.That(await Eval($"step({thing}/ROW,a b c,2,,|)")).IsEqualTo("a-b|c-");
	}

	/// <summary>The hook argument registers are no longer q-registers that every command leaves behind.</summary>
	[Test]
	public async Task CommandsLeaveNoArgumentQRegisters()
	{
		var thing = await NewThing("NoLsReg");
		await Run($"&A {thing}=1");
		await CommandParser.CommandListParse(MarkupText.Plain($"@atrchown {thing}/A=#1;&B {thing}=x%q<ls>%q<rs>%q<args>"));
		await Assert.That(await Eval($"get({thing}/B)")).IsEqualTo("x");
	}
}

/// <summary>A hook reads its command's arguments as named arguments; hooks are global, so not in parallel.</summary>
[NotInParallel]
public class HookNamedArgumentTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IHookService HookService => WebAppFactoryArg.Services.GetRequiredService<IHookService>();

	private async Task Run(string command) => await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	[Test]
	public async Task BeforeHookReadsTheCommandsArguments()
	{
		var holder = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "HookArgs");
		var target = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "HookTgt");
		await Run($"&SEEN {holder}=");
		await Run($"&BEFORE {holder}=attrib_set({holder}/SEEN,%<ls>|%<rs>|[r(equals,args)]|%<args>)");
		await Run($"@hook/before @ATRCHOWN={holder},BEFORE");
		try
		{
			await Run($"&A {target}=1");
			await Run($"@atrchown {target}/A=#1");
			var seen = await FunctionParser.EvaluateAsync(MarkupText.Plain($"get({holder}/SEEN)"));
			await Assert.That(seen.ToPlainText()).IsEqualTo($"{target}/A|#1|=|{target}/A=#1");
		}
		finally
		{
			await HookService.ClearHookAsync("@ATRCHOWN", "BEFORE");
		}
	}

	[Test]
	public async Task CommandArgsNamesTheArgumentsHooksRead()
	{
		var holder = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "HookNamed");
		var target = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "HookNTgt");
		await Run($"&SEEN {holder}=");
		await Run($"&BEFORE {holder}=attrib_set({holder}/SEEN,%<attribute>|%<owner>|%<lsa2>)");
		await Run("@command/args @ATRCHOWN=attribute owner");
		await Run($"@hook/before @ATRCHOWN={holder},BEFORE");
		try
		{
			await Run($"&A {target}=1");
			await Run($"@atrchown {target}/A=#1");
			var seen = await FunctionParser.EvaluateAsync(MarkupText.Plain($"get({holder}/SEEN)"));
			await Assert.That(seen.ToPlainText()).IsEqualTo($"{target}/A|#1|#1");
		}
		finally
		{
			await HookService.ClearHookAsync("@ATRCHOWN", "BEFORE");
			await Run("@command/args @ATRCHOWN=");
		}
	}

	[Test]
	public async Task CommandArgsLastNameTakesTheRestNumbered()
	{
		var holder = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "HookRest");
		var target = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "HookRTgt");
		await Run($"&SEEN {holder}=");
		await Run($"&BEFORE {holder}=attrib_set({holder}/SEEN,%<parts1>|%<parts2>|%<partscount>)");
		await Run("@command/args @ATRCHOWN=parts...");
		await Run($"@hook/before @ATRCHOWN={holder},BEFORE");
		try
		{
			await Run($"&A {target}=1");
			await Run($"@atrchown {target}/A=#1");
			var seen = await FunctionParser.EvaluateAsync(MarkupText.Plain($"get({holder}/SEEN)"));
			await Assert.That(seen.ToPlainText()).IsEqualTo($"{target}/A|#1|2");
		}
		finally
		{
			await HookService.ClearHookAsync("@ATRCHOWN", "BEFORE");
			await Run("@command/args @ATRCHOWN=");
		}
	}

	[Test]
	[Arguments("one ONE")]
	[Arguments("one 2")]
	public async Task CommandArgsRefusesRepeatedOrNumericNames(string names)
	{
		await Run($"@command/args @ATRCHOWN={names}");
		var attribute = CommandParser.CommandLibrary["@ATRCHOWN"].LibraryInformation.Attribute;
		await Assert.That(attribute.ArgumentNames.Length).IsEqualTo(0);
	}
}
