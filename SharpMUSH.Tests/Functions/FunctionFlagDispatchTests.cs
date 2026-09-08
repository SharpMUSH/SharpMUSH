using Microsoft.Extensions.DependencyInjection;
using Mediator;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class FunctionFlagDispatchTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private sealed record Options(SharpMUSHOptions CurrentValue) : IOptionsWrapper<SharpMUSHOptions>;

	private MUSHCodeParser Parser(FunctionFlags flags, bool sideEffects = true)
	{
		var original = (MUSHCodeParser)WebAppFactoryArg.FunctionParser;
		var library = new FunctionLibraryService();
		foreach (var entry in original.FunctionLibrary) library.Add(entry.Key, entry.Value);
		library.Add("flagprobe", (new FunctionDefinition(
			new SharpFunctionAttribute { Name = "flagprobe", Flags = flags, MinArgs = 1, MaxArgs = 1 },
			p => ValueTask.FromResult(new CallState(p.CurrentState.Arguments["0"].Message!))), true));
		var config = original.Configuration.CurrentValue;
		return original with
		{
			FunctionLibrary = library,
			Configuration = new Options(config with { Function = config.Function with { FunctionSideEffects = sideEffects } })
		};
	}

	[Test]
	[Arguments(FunctionFlags.Disabled, true, "#-1 FUNCTION DISABLED")]
	[Arguments(FunctionFlags.HasSideFX, false, "#-1 FUNCTION DISABLED")]
	[Arguments(FunctionFlags.HasSideFX, true, "hello")]
	[Arguments(FunctionFlags.Regular, false, "hello")]
	public async Task DispatchGates(FunctionFlags flags, bool enabled, string expected)
	{
		var result = await Parser(flags, enabled).FunctionParse(MarkupText.Plain("flagprobe(hello)"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments(FunctionFlags.IntegersOnly, "1.5", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments(FunctionFlags.PositiveIntegersOnly, "-1", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	[Arguments(FunctionFlags.PositiveIntegersOnly, "0", "0")]
	[Arguments(FunctionFlags.PositiveIntegersOnly, "18446744073709551615", "18446744073709551615")]
	[Arguments(FunctionFlags.DecimalsOnly, "word", "#-1 ARGUMENT MUST BE NUMBER")]
	[Arguments(FunctionFlags.DecimalsOnly, "add(1,2)", "3")]
	public async Task NumericFlagsValidateEvaluatedArguments(FunctionFlags flags, string input, string expected)
	{
		var result = await Parser(flags).FunctionParse(MarkupText.Plain($"flagprobe({input})"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task ArgumentMaskContainsBothParseModes()
	{
		var mask = FunctionFlags.Arg_Mask;
		await Assert.That(mask).IsEqualTo(FunctionFlags.NoParse | FunctionFlags.Literal);
	}

	[Test]
	[Arguments("e(0)", "1")]
	[Arguments("iter(1 2 3,%i0[ibreak(0)])", "1 2 3")]
	[Arguments("iter(1 2 3,iter(a b,%i1%i0[ibreak()]))", "1a 2a 3a")]
	[Arguments("iter(1 2 3,iter(a b,%i1%i0[ibreak(2)]))", "1a")]
	[Arguments("ibreak(-1)", "#-1 OUT OF RANGE")]
	[Arguments("ibreak(word)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments("ibreak(18446744073709551615)", "#-1 ARGUMENT MUST BE INTEGER")]
	public async Task AuditedDeclarationsHandleBoundaries(string input, string expected)
	{
		var result = await Parser(FunctionFlags.Regular).FunctionParse(MarkupText.Plain(input));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}
	[Test]
	[Arguments("name(me,newname)")]
	[Arguments("parent(me,#0)")]
	[Arguments("zone(me,#0)")]
	[Arguments("powers(me,Guest)")]
	[Arguments("emit(hello)")]
	[Arguments("lemit(hello)")]
	[Arguments("nsemit(hello)")]
	[Arguments("nslemit(hello)")]
	[Arguments("wsjson({})")]
	[Arguments("wshtml(hello)")]
	[Arguments("oob(me,test)")]
	[Arguments("create(FlagSideEffectProbe)")]
	[Arguments("pcreate(FlagSideEffectProbe,password)")]
	[Arguments("clone(me)")]
	[Arguments("dig(FlagSideEffectProbe)")]
	[Arguments("open(FlagSideEffectProbe)")]
	[Arguments("link(me,#0)")]
	[Arguments("wipe(me/FLAGPROBE)")]
	[Arguments("tel(me,#0)")]
	[Arguments("pemit(me,hello)")]
	[Arguments("mailsend(me,hello)")]
	[Arguments("set(me,FLAGPROBE:value)")]
	public async Task RealMutatorsRespectGlobalSwitch(string input)
	{
		var result = await Parser(FunctionFlags.Regular, false).FunctionParse(MarkupText.Plain(input));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("#-1 FUNCTION DISABLED");
	}

	[Test]
	public async Task LocalizePreservesIncomingRegistersAndRestoresChanges()
	{
		var parser = Parser(FunctionFlags.Localize);
		parser = (MUSHCodeParser)parser.Push(parser.CurrentState with { Registers = new([new() { ["X"] = MarkupText.Plain("outer") }]) });
		var definition = parser.FunctionLibrary["flagprobe"].LibraryInformation;
		parser.FunctionLibrary["flagprobe"] = (definition with
		{
			Function = p =>
			{
				var before = p.CurrentState.Registers.First()["X"];
				p.CurrentState.Registers.First()["X"] = MarkupText.Plain("inner");
				return ValueTask.FromResult(new CallState(before));
			}
		}, true);
		var result = await parser.FunctionParse(MarkupText.Plain("flagprobe(hello)"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("outer");
		await Assert.That(parser.CurrentState.Registers.First()["X"].ToPlainText()).IsEqualTo("outer");
	}

	[Test]
	public async Task ApplyCannotBypassDisabledFlag()
	{
		var result = await Parser(FunctionFlags.Disabled).FunctionParse(MarkupText.Plain("map(#apply/flagprobe,hello)"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("#-1 FUNCTION DISABLED");
	}

	[Test]
	[Arguments(FunctionFlags.NoFixed, "FIXED", "#-1 PERMISSION DENIED")]
	[Arguments(FunctionFlags.AdminOnly, "ROYALTY", "hello")]
	[Arguments(FunctionFlags.GodOnly, "ROYALTY", "#-1 PERMISSION DENIED")]
	public async Task ExecutorFlagsGovernPermission(FunctionFlags flags, string objectFlag, string expected)
	{
		var parser = Parser(flags);
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var player = await mediator.Send(new CreatePlayerCommand($"Flag{Guid.NewGuid():N}"[..20],
			"password", new DBRef(0), new DBRef(0), 100));
		var connection = WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
		await WebAppFactoryArg.CommandParser.CommandParse(1, connection, MarkupText.Plain($"@set {player}={objectFlag}"));
		parser = (MUSHCodeParser)parser.Push(parser.CurrentState with { Executor = player, Caller = player, Enactor = player });
		var result = await parser.FunctionParse(MarkupText.Plain("flagprobe(hello)"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("ulocal", "me/FLAGLOCAL")]
	[Arguments("uldefault", "me/FLAGLOCAL,fallback")]
	[Arguments("localize", "%qx[setq(x,inner)]%qx")]
	public async Task LocalizedAttributeCallsInheritAndRestoreRegisters(string function, string arguments)
	{
		var parser = Parser(FunctionFlags.Regular);
		parser = (MUSHCodeParser)parser.Push(parser.CurrentState with { Registers = new([new() { ["X"] = MarkupText.Plain("outer") }]) });
		var connection = WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
		await WebAppFactoryArg.CommandParser.CommandParse(1, connection,
			MarkupText.Plain("&FLAGLOCAL me=%qx[setq(x,inner)]%qx"));
		var result = await parser.FunctionParse(MarkupText.Plain($"{function}({arguments})"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("outerinner");
		await Assert.That(parser.CurrentState.Registers.First()["X"].ToPlainText()).IsEqualTo("outer");
	}

	[Test]
	public async Task LocalizedDefaultCannotLeakRegisterWrites()
	{
		var parser = Parser(FunctionFlags.Regular);
		parser = (MUSHCodeParser)parser.Push(parser.CurrentState with { Registers = new([new() { ["X"] = MarkupText.Plain("outer") }]) });
		var result = await parser.FunctionParse(MarkupText.Plain("uldefault(me/NONEXISTENTFLAGDEFAULT,%qx[setq(x,inner)]%qx)"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("outerinner");
		await Assert.That(parser.CurrentState.Registers.First()["X"].ToPlainText()).IsEqualTo("outer");
	}

	private sealed class RecordingLogger : ILogger
	{
		public List<string> Messages { get; } = [];
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => true;
		public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error,
			Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, error));
	}

	[Test]
	[Arguments(FunctionFlags.LogName, false)]
	[Arguments(FunctionFlags.LogName | FunctionFlags.LogArgs, true)]
	public async Task LoggingFlagsSelectRecordedDetail(FunctionFlags flags, bool includesArguments)
	{
		var parser = Parser(flags);
		parser = (MUSHCodeParser)parser.Push(parser.CurrentState with
		{
			Arguments = new() { ["0"] = new CallState("log-argument-probe") }
		});
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var executor = (await mediator.Send(new GetObjectNodeQuery(WebAppFactoryArg.ExecutorDBRef))).Known;
		var logger = new RecordingLogger();
		await FunctionDispatcher.InvokeAsync(parser, parser.FunctionLibrary["flagprobe"].LibraryInformation,
			executor, true, Substitute.For<INotifyService>(), logger);
		await Assert.That(logger.Messages.Count).IsEqualTo(1);
		await Assert.That(logger.Messages[0]).Contains("FLAGPROBE");
		await Assert.That(logger.Messages[0].Contains("log-argument-probe")).IsEqualTo(includesArguments);
	}

	[Test]
	public async Task DeprecatedFlagNotifiesOwner()
	{
		var parser = Parser(FunctionFlags.Deprecated);
		parser = (MUSHCodeParser)parser.Push(parser.CurrentState with { Arguments = new() { ["0"] = new CallState("hello") } });
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var executor = (await mediator.Send(new GetObjectNodeQuery(WebAppFactoryArg.ExecutorDBRef))).Known;
		var notify = Substitute.For<INotifyService>();
		await FunctionDispatcher.InvokeAsync(parser, parser.FunctionLibrary["flagprobe"].LibraryInformation,
			executor, true, notify, new RecordingLogger());
		var owner = await executor.Object().Owner.WithCancellation(CancellationToken.None);
		await notify.Received(1).Notify(owner.Object.DBRef,
			$"Deprecated function FLAGPROBE being used on object {executor.Object().DBRef}.");
	}

	[Test]
	[Arguments("name(me)")]
	[Arguments("parent(me)")]
	[Arguments("zone(me)")]
	[Arguments("powers(me)")]
	public async Task GetterFormsRemainAvailableWithoutSideEffects(string input)
	{
		var result = await Parser(FunctionFlags.Regular, false).FunctionParse(MarkupText.Plain(input));
		await Assert.That(result!.Message!.ToPlainText()).DoesNotContain("DISABLED");
	}

	[Test]
	public async Task DeferredLocalizedArgumentsUseLocalRegisters()
	{
		var parser = Parser(FunctionFlags.NoParse | FunctionFlags.Localize);
		parser = (MUSHCodeParser)parser.Push(parser.CurrentState with { Registers = new([new() { ["X"] = MarkupText.Plain("outer") }]) });
		var definition = parser.FunctionLibrary["flagprobe"].LibraryInformation;
		parser.FunctionLibrary["flagprobe"] = (definition with
		{
			Function = async p => new CallState(await p.CurrentState.Arguments["0"].ParsedMessage())
		}, true);
		var result = await parser.FunctionParse(MarkupText.Plain("flagprobe(%qx[setq(x,inner)]%qx)"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("outerinner");
		await Assert.That(parser.CurrentState.Registers.First()["X"].ToPlainText()).IsEqualTo("outer");
	}

	[Test]
	[Arguments(FunctionFlags.NoFixed, "FIXED", "flagprobe(hello)")]
	[Arguments(FunctionFlags.NoGagged, "GAGGED", "flagprobe(hello)")]
	[Arguments(FunctionFlags.Regular, "GAGGED", "emit(hello)")]
	[Arguments(FunctionFlags.Regular, "GAGGED", "create(FlagGaggedProbe)")]
	public async Task OwnerFlagsRestrictOwnedObjects(FunctionFlags flags, string objectFlag, string expression)
	{
		var parser = Parser(flags);
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var playerRef = await mediator.Send(new CreatePlayerCommand($"Owner{Guid.NewGuid():N}"[..20],
			"password", new DBRef(0), new DBRef(0), 100));
		var player = (await mediator.Send(new GetObjectNodeQuery(playerRef))).Known.AsPlayer;
		var room = (await mediator.Send(new GetObjectNodeQuery(new DBRef(0)))).Known.AsRoom;
		var thing = await mediator.Send(new CreateThingCommand("FlagOwnedObject", room, player, room));
		var connection = WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
		await WebAppFactoryArg.CommandParser.CommandParse(1, connection, MarkupText.Plain($"@set {playerRef}={objectFlag}"));
		parser = (MUSHCodeParser)parser.Push(parser.CurrentState with { Executor = thing, Caller = playerRef, Enactor = playerRef });
		var result = await parser.FunctionParse(MarkupText.Plain(expression));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("#-1 PERMISSION DENIED");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ApplyValidatesArityBeforeSideEffects(bool parity)
	{
		var flags = FunctionFlags.HasSideFX | (parity ? FunctionFlags.EvenArgsOnly : FunctionFlags.Regular);
		var parser = Parser(flags, false);
		var definition = parser.FunctionLibrary["flagprobe"].LibraryInformation;
		definition.Attribute.MinArgs = parity ? 0 : 2;
		definition.Attribute.MaxArgs = 3;
		var parsed = await parser.FunctionParse(MarkupText.Plain("flagprobe(hello)"));
		var expected = parity
			? string.Format(ErrorMessages.Returns.GotUnEvenArgs, "FLAGPROBE")
			: string.Format(ErrorMessages.Returns.TooFewArguments, "FLAGPROBE", 2, 1);
		await Assert.That(parsed!.Message!.ToPlainText()).IsEqualTo(expected);
		parser = (MUSHCodeParser)parser.Push(parser.CurrentState with { Arguments = new() { ["0"] = new CallState("hello") } });
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var executor = (await mediator.Send(new GetObjectNodeQuery(WebAppFactoryArg.ExecutorDBRef))).Known;
		var applied = await FunctionDispatcher.InvokeAsync(parser, definition, executor,
			false, Substitute.For<INotifyService>(), new RecordingLogger());
		await Assert.That(applied.Message!.ToPlainText()).IsEqualTo(expected);
	}

}
