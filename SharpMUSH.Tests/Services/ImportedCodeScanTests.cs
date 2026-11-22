using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Parsing;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Softcode;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services.Imports;

namespace SharpMUSH.Tests.Services;

public class ImportedCodeScanTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.Services.GetRequiredService<IMUSHCodeParser>();
	private IConnectionService Connections => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();

	private SoftcodeSyntaxAnalyzer Analyzer => new(Parser.FunctionLibrary,
		WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>());

	[Test]
	[Arguments("add(1,mul(2,3))", "add mul")]
	[Arguments("He smile(s) at [add(1,2)].", "add")]
	[Arguments("x [first(rest(a b c))] after(2)", "first rest")]
	[Arguments("$go *:@pemit %#=[ucstr(%0)]", "ucstr")]
	[Arguments("think strtrunc(x,3)", "")]
	[Arguments("No calls here.", "")]
	[Arguments("[lit(missing())] [lit([other()])]", "lit lit")]
	public async Task CallsAreTheBracketedOnesAndALeadingOne(string text, string expected)
		=> await Assert.That(string.Join(' ', Analyzer.CalledFunctions(MarkupText.Plain(text)))).IsEqualTo(expected);

	[Test]
	public async Task ReportsEachMissingFunctionOnceWithWhereItIsCalled()
	{
		var id = TestIsolationHelpers.GenerateUniqueName("s")[^5..].Replace("_", "");
		var thing = $"ScanThing{id}";
		foreach (var command in new[]
		{
			$"@create {thing}",
			$"&FN {thing}=zzlead{id}(1,[zzinner{id}()])",
			$"&CMD {thing}=$scan{id} *:@pemit %#=[zzlead{id}(%0)]",
			$"&DESC {thing}=It smile{id}(s).",
			$"&STARTUP {thing}=@function zzmine{id}=me,FN",
			$"&USE {thing}=[zzmine{id}()] [add(1,2)]"
		})
			await Parser.CommandParse(1, Connections, MarkupText.Plain(command));
		await Scheduler.SettleForTestsAsync();

		var result = new ConversionResult();
		await ImportedCodeScan.ReportAsync(WebAppFactoryArg.Services.GetRequiredService<IMediator>(), Parser.FunctionLibrary, Parser.CommandLibrary,
			Analyzer, null, '+', result, CancellationToken.None);
		var text = string.Join(" | ", result.Warnings.Select(w => w.Text));

		var lead = result.Warnings.Single(w => w.Text.Contains($"zzlead{id}()"));
		await Assert.That(lead.Kind).IsEqualTo(ImportWarningKind.Softcode);
		await Assert.That(lead.Text).Contains("is called 2 times").And.Contains("/FN").And.Contains("/CMD");
		await Assert.That(text).Contains($"zzinner{id}() is called once");
		await Assert.That(text).DoesNotContain($"smile{id}").Because("a name in prose is not a call");
		await Assert.That(text).DoesNotContain($"zzmine{id}").Because("an @function in the world registers it");
		await Assert.That(text).DoesNotContain(": add()").Because("add() is a built-in function");
	}

	[Test]
	public async Task ReportsCommandsSwitchesArgumentCountsAndAttributesNothingAnswers()
	{
		var id = TestIsolationHelpers.GenerateUniqueName("c")[^5..].Replace("_", "");
		var thing = $"ScanCmds{id}";
		// Each is written many times over, so that it is among the most used the report names one by one in a world
		// other tests share.
		var body = Repeat($"@zzcmd{id} me;@pemit/zzsw{id} %#=hi;think [ljust(a,1,2,3,4,5,6,7)];"
			+ $"@switch 1=1,{{zzinner{id} x}},@dolist a=@pemit/zzsw2{id} %#=b");
		foreach (var command in new[]
		{
			$"@create {thing}",
			$"&CMD {thing}=$scan{id}:{body}",
			$"&MINE {thing}=$+mine{id} *:think ok",
			$"&AUSE {thing}=+mine{id} hello;@tr Me/LATER{id};@pemit/silent %#=fine;]@zzbracket{id} x;@force me=zzforced{id}",
			$"&LATER{id} {thing}={Repeat($"zzlater{id} hi")}",
			$"&READ {thing}=$read{id}:think {Repeat($"[u(me/ZZMISSING{id})] [u(me/HELD{id})] [u(SETBYCODE{id})] [u(me/LATER{id})] [get(me/DATA{id})] [iter(a,u(me/IT_##))]")}",
			$"&HELD{id} {thing}=1",
			$"&SETIT {thing}=$setit{id}:&SETBYCODE{id} me=1"
		})
			await Parser.CommandParse(1, Connections, MarkupText.Plain(command));
		await Scheduler.SettleForTestsAsync();

		var result = new ConversionResult();
		await ImportedCodeScan.ReportAsync(WebAppFactoryArg.Services.GetRequiredService<IMediator>(), Parser.FunctionLibrary,
			Parser.CommandLibrary, Analyzer, null, '+', result, CancellationToken.None);
		var text = string.Join(" | ", result.Warnings.Select(w => w.Text));

		await Assert.That(text).Contains($"the command @zzcmd{id} is run {Times} times");
		await Assert.That(text).Contains($"the command zzinner{id} is run {Times} times").Because("an @switch action runs as a command");
		await Assert.That(text).Contains($"the command zzlater{id} is run {Times} times").Because("an attribute @trigger names runs as commands");
		await Assert.That(text).DoesNotContain($"+mine{id}").Because("a $-command in the world answers it");
		await Assert.That(text).Contains($"@pemit/zzsw{id} is run {Times} times, and @pemit has no /zzsw{id} switch");
		await Assert.That(text).Contains($"@pemit/zzsw2{id}").Because("an @dolist action runs as a command");
		await Assert.That(text).DoesNotContain("@pemit/silent").Because("@pemit has /silent");
		await Assert.That(text).Contains($"@zzbracket{id}").And.DoesNotContain($"]@zzbracket{id}")
			.Because("] runs the command after it unevaluated");
		await Assert.That(text).DoesNotContain($"zzforced{id}").Because("the forced object runs it, with its own commands in reach");
		await Assert.That(text).Contains("ljust() is called with 8 arguments").And.Contains("it takes 2 to 4 arguments");
		await Assert.That(text).Contains($"the attribute ZZMISSING{id.ToUpperInvariant()} is called {Times} times");
		await Assert.That(text).DoesNotContain($"HELD{id.ToUpperInvariant()} ").Because("an object has it");
		await Assert.That(text).DoesNotContain($"SETBYCODE{id.ToUpperInvariant()}").Because("code sets it");
		await Assert.That(text).DoesNotContain($"LATER{id.ToUpperInvariant()} is called").Because("an object has it");
		await Assert.That(text).DoesNotContain($"DATA{id.ToUpperInvariant()}").Because("get() reads data, which may not be set yet");
		await Assert.That(text).DoesNotContain("IT_##").Because("## makes the name a computed one");
	}

	[Test]
	public async Task NamesTheHelpTopicForWhatSharpMUSHLeavesOut()
	{
		var thing = $"ScanLeftOut{TestIsolationHelpers.GenerateUniqueName("l")[^5..].Replace("_", "")}";
		foreach (var command in new[]
		{
			$"@create {thing}",
			$"&STARTUP {thing}={Repeat("@cron me=x;think [execscript(x)]")}",
			$"@set {thing}/STARTUP=rhost"
		})
			await Parser.CommandParse(1, Connections, MarkupText.Plain(command));
		await Scheduler.SettleForTestsAsync();

		var result = new ConversionResult();
		await ImportedCodeScan.ReportAsync(WebAppFactoryArg.Services.GetRequiredService<IMediator>(), Parser.FunctionLibrary,
			Parser.CommandLibrary, Analyzer, null, '+', result, CancellationToken.None);

		var execscript = result.Warnings.Single(w => w.Text.Contains("execscript()"));
		await Assert.That(execscript.Text).Contains($"see 'help {ImportedCodeScan.LeftOutTopic}'");
		var cron = result.Warnings.Single(w => w.Text.Contains("the command @cron "));
		await Assert.That(cron.Text).DoesNotContain("on purpose").Because("@cron is TinyMUX's, and this code is RhostMUSH's");
	}

	private const int Times = 60;

	private static string Repeat(string commands) => string.Join(';', Enumerable.Repeat(commands, Times));

	[Test]
	[Arguments("@pemit %#=a;{@emit b;think c};@dolist x=y", "@pemit %#=a|{@emit b;think c}|@dolist x=y")]
	[Arguments("  look ;; say hi", "look |say hi")]
	public async Task CommandsAreTheListsTopLevelOnes(string text, string expected)
		=> await Assert.That(string.Join('|', Analyzer.Commands(MarkupText.Plain(text)))).IsEqualTo(expected);

	/// <summary>
	/// Each name SharpMUSH leaves out is one it does not have, under its own name or the dialect's, and the help
	/// topic the report points to lists it. A name brought in later has to leave the list and the topic.
	/// </summary>
	[Test]
	public async Task WhatIsLeftOutIsMissingAndListedInHelp()
	{
		var help = await File.ReadAllTextAsync(Path.Combine(TestPaths.Helpfiles.FullName, "compatibility-left-out.md"));
		var trie = CommandTrie.For(Parser.CommandLibrary);
		foreach (var (dialect, names) in new[]
		{
			(SoftcodeDialects.Rhost, ImportLeftOut.RhostFunctions), (SoftcodeDialects.Mux, ImportLeftOut.MuxFunctions)
		})
			foreach (var name in names)
			{
				await Assert.That(Parser.FunctionLibrary.ContainsKey(name) || dialect.FunctionFor(name) is not null).IsFalse()
					.Because($"{name}() is a function here now; take it out of {nameof(ImportLeftOut)} and the topic");
				await Assert.That(help).Contains($"`{name}()`").Because($"the topic lists {name}()");
			}

		foreach (var (dialect, names) in new[]
		{
			(SoftcodeDialects.Rhost, ImportLeftOut.RhostCommands), (SoftcodeDialects.Mux, ImportLeftOut.MuxCommands)
		})
			foreach (var name in names)
			{
				await Assert.That(trie.FindExact(name) is not null || dialect.CommandFor(name) is not null).IsFalse()
					.Because($"{name} is a command here now; take it out of {nameof(ImportLeftOut)} and the topic");
				await Assert.That(help).Contains($"`{name}`").Because($"the topic lists {name}");
			}

		foreach (var (dialect, entries) in new[]
		{
			(SoftcodeDialects.Rhost, ImportLeftOut.RhostSwitches), (SoftcodeDialects.Mux, ImportLeftOut.MuxSwitches)
		})
			foreach (var entry in entries)
			{
				var (command, typed) = (entry[..entry.IndexOf('/')], entry[(entry.IndexOf('/') + 1)..]);
				var definition = trie.FindExact(command)?.Attribute;
				await Assert.That(definition).IsNotNull().Because($"{command} is SharpMUSH's name for the command");
				if (definition is null) continue;
				var switches = definition.Switches ?? [];
				await Assert.That(switches.Contains(typed, StringComparer.OrdinalIgnoreCase)
						|| dialect.SwitchMeaning(definition.Name, switches, typed, command) is not null).IsFalse()
					.Because($"{entry} works here now; take it out of {nameof(ImportLeftOut)} and the topic");
				await Assert.That(help).Contains($"`{entry}`").Because($"the topic lists {entry}");
			}
	}

	[Test]
	public async Task WarnsOfCodeThatDoesNotParseObjidsBuiltFromSecondsAndBellDelimiters()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var god = (await services.GetRequiredService<IObjectStore>().GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var room = await mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName("ScanParse"), god));
		foreach (var (name, value) in new[]
		{
			// What & refuses to store, as an import writes it.
			("BROKEN", "[first(a{b]c})]"),
			("SECONDS", "[num(%0)]:[csecs(%0)]"),
			("LITERAL", "#3:[csecs(#3)]"),
			("MILLISECONDS", "[num(%0)]:[csecs(%0,ms)]"),
			("BELLS", "[words(%0,beep())]"),
			("NESTED", "[words(strcat(%0,extract(%1,1,2)),beep())]"),
			("MARKED", "[words(%0,%*<|>)]"),
			("RINGS", "[pemit(%#,beep())] [extract(%0,1,1,beep())] Created:[csecs(%0)]")
		})
			await mediator.Send(new SetAttributeCommand(room, [name], MarkupText.Plain(value), god));

		var result = new ConversionResult();
		await ImportedCodeScan.ReportAsync(mediator, Parser.FunctionLibrary, Parser.CommandLibrary, Analyzer, null, '+', result, CancellationToken.None);

		// The world is shared, so other tests' code that does not parse may fill the places the warning names.
		await Assert.That(Analyzer.ValidateAndGetErrors(MarkupText.Plain("[first(a{b]c})]"), ParseType.Function)).IsNotEmpty();
		await Assert.That(result.Warnings.Select(w => w.Text)).Contains(text => text.Contains("do not parse"));
		var seconds = result.Warnings.Single(w => w.Text.Contains("build an objid as"));
		await Assert.That(seconds.Text).Contains($"#{room.Number}/SECONDS").And.Contains($"#{room.Number}/LITERAL")
			.And.DoesNotContain($"#{room.Number}/MILLISECONDS");
		var bells = result.Warnings.Single(w => w.Text.Contains("give beep() as the delimiter"));
		await Assert.That(bells.Text).Contains($"#{room.Number}/BELLS").And.Contains($"#{room.Number}/NESTED").And.DoesNotContain($"#{room.Number}/MARKED")
			.And.DoesNotContain($"#{room.Number}/RINGS");
		await Assert.That(seconds.Text).DoesNotContain($"#{room.Number}/RINGS");
	}

	/// <summary>
	/// Only code is read for calls: a help attribute nothing runs is left alone, and so is SQL,
	/// whether in sql()'s query or in an attribute holding a statement. An @dolist over a written list registers its
	/// words as @functions.
	/// </summary>
	[Test]
	public async Task ReadsCallsOnlyFromCodeThatRuns()
	{
		var id = TestIsolationHelpers.GenerateUniqueName("n")[^5..].Replace("_", "");
		var thing = $"ScanNoise{id}";
		foreach (var command in new[]
		{
			$"@create {thing}",
			$"&HELPTEXT{id} {thing}=zzhelp{id}(topic) explains it.",
			$"&RAN{id} {thing}={Repeat($"[zzran{id}(1)]")}",
			$"&QUERY{id} {thing}=SELECT zzcount{id}(*) FROM t",
			$"&USE {thing}=$use{id}:think [map(me/RAN{id},a b)] [sql(SELECT zzsum{id}(x) FROM t)] [sql(u(me/QUERY{id}))] [zzone{id}()][zztwo{id}()]",
			$"&STARTUP {thing}=@dolist zzone{id} zztwo{id}={{@function ##=me/RAN{id}}}",
			$"&HOOKED{id} {thing}={Repeat($"[zzhook{id}(1)]")}",
			$"&EDEF{id} {thing}={Repeat($"[zzedef{id}(1)]")}",
			$"&PROSE{id} {thing}=Update {Repeat($"[zzprose{id}(1)]")}",
			$"&SQLCODE{id} {thing}=SELECT {Repeat($"[zzsqlcode{id}(1)]")} FROM t",
			$"&ACT {thing}=$act{id}:@hook/before zzcmd{id}=me,HOOKED{id};think [edefault(me/EDEF{id},x)] [u(me/PROSE{id})] [u(me/SQLCODE{id})] {Repeat($"[sql(SELECT 1 FROM t WHERE id=[zzinquery{id}(1)])]")}"
		})
			await Parser.CommandParse(1, Connections, MarkupText.Plain(command));
		await Scheduler.SettleForTestsAsync();

		var result = new ConversionResult();
		await ImportedCodeScan.ReportAsync(WebAppFactoryArg.Services.GetRequiredService<IMediator>(), Parser.FunctionLibrary,
			Parser.CommandLibrary, Analyzer, null, '+', result, CancellationToken.None);
		var text = string.Join(" | ", result.Warnings.Select(w => w.Text));

		await Assert.That(text).Contains($"zzran{id}() is called {Times} times").Because("map() runs RAN");
		await Assert.That(text).DoesNotContain($"zzhelp{id}").Because("nothing runs the help text");
		await Assert.That(text).DoesNotContain($"zzsum{id}").Because("sql()'s query is the database's");
		await Assert.That(text).DoesNotContain($"zzcount{id}").Because("an attribute holding a statement is the database's");
		await Assert.That(text).Contains($"zzhook{id}() is called {Times} times").Because("@hook runs HOOKED");
		await Assert.That(text).Contains($"zzedef{id}() is called {Times} times").Because("edefault() runs EDEF");
		await Assert.That(text).Contains($"zzprose{id}() is called {Times} times").Because("u() runs PROSE, which only starts like SQL");
		await Assert.That(text).Contains($"zzinquery{id}() is called {Times} times").Because("[ ] in sql()'s query is softcode's");
		await Assert.That(text).Contains($"zzsqlcode{id}() is called {Times} times").Because("[ ] in a statement u() runs is softcode's");
		await Assert.That(text).DoesNotContain($"zzone{id}").And.DoesNotContain($"zztwo{id}")
			.Because("the @dolist registers each word of its list");
	}
}
