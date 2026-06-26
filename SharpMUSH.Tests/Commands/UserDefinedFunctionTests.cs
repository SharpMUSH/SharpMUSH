using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Integration tests for global user-defined functions (<c>@function</c>) and boot-time
/// <c>@STARTUP</c> re-registration. These hit the real DB via the shared web-app factory.
/// The <c>@function</c> registry is a process-wide singleton and attributes live on the
/// shared God object, so every test mints UNIQUE function/attribute names (<see cref="U"/>)
/// to stay conflict-free if other test classes run concurrently.
/// </summary>
[NotInParallel]
public class UserDefinedFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IUserDefinedFunctionService Registry => WebAppFactoryArg.Services.GetRequiredService<IUserDefinedFunctionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IServiceProvider Services => WebAppFactoryArg.Services;

	/// <summary>A fresh lowercase-hex token, unique per call — used to keep names collision-free.</summary>
	private static string U() => Guid.NewGuid().ToString("N");

	private Task Cmd(string command) =>
		CommandParser.CommandParse(1, ConnectionService, MModule.single(command)).AsTask();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText();

	/// <summary>Evaluates <paramref name="expression"/> with <paramref name="executor"/> as the executor (not #1).</summary>
	private async Task<string> EvalAs(DBRef executor, string expression)
	{
		var parser = new MUSHCodeParser(
			Services.GetRequiredService<ILogger<MUSHCodeParser>>(),
			Services.GetRequiredService<LibraryService<string, FunctionDefinition>>(),
			Services.GetRequiredService<LibraryService<string, CommandDefinition>>(),
			Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(),
			Services,
			state: new ParserState(
				Registers: new ConcurrentStack<Dictionary<string, MString>>([[]]),
				IterationRegisters: [],
				RegexRegisters: [],
				SwitchStack: [],
				ExecutionStack: [],
				EnvironmentRegisters: [],
				CurrentEvaluation: null,
				ParserFunctionDepth: 0,
				Function: null,
				Command: "think",
				CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
				Switches: [],
				Arguments: [],
				Executor: executor,
				Enactor: executor,
				Caller: executor,
				Handle: 1,
				CallDepth: new InvocationCounter(),
				FunctionRecursionDepths: new Dictionary<string, int>(),
				TotalInvocations: new InvocationCounter(),
				LimitExceeded: new LimitExceededFlag()));

		return (await parser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText();
	}

	private Task<DBRef> NewNonWizardPlayer() =>
		Mediator.Send(new CreatePlayerCommand($"UDFPlayer{U()}", "password", new DBRef(0), new DBRef(0), 100)).AsTask();

	[Test]
	public async ValueTask DefineThenCallEvaluatesAttribute()
	{
		var fn = $"dbl{U()}";
		var attr = $"DOUBLE{U()}";
		await Cmd($"&{attr} me=mul(%0,2)");
		await Cmd($"@function {fn}=me,{attr}");

		await Assert.That(await Eval($"{fn}(21)")).IsEqualTo("42");
	}

	[Test]
	public async ValueTask FormattingFunctionUsingCenter()
	{
		var fn = $"hdr{U()}";
		var attr = $"HDR{U()}";
		await Cmd($"&{attr} me=center(%0,78,=)");
		await Cmd($"@function {fn}=me,{attr}");

		var expected = await Eval("center(Title,78,=)");
		await Assert.That(await Eval($"{fn}(Title)")).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask UnknownFunctionReturnsNoSuchFunction()
	{
		await Assert.That(await Eval($"definitelynotafunction{U()}(1)")).Contains("COULD NOT FIND FUNCTION");
	}

	[Test]
	public async ValueTask MinMaxArgCountEnforced()
	{
		var fn = $"argbound{U()}";
		var attr = $"ARGBOUND{U()}";
		await Cmd($"&{attr} me=ok");
		await Cmd($"@function {fn}=me,{attr},2,2");

		await Assert.That((await Eval($"{fn}(a)")).ToUpperInvariant()).Contains("AT LEAST");
		await Assert.That((await Eval($"{fn}(a,b,c)")).ToUpperInvariant()).Contains("AT MOST");
		await Assert.That(await Eval($"{fn}(a,b)")).IsEqualTo("ok");
	}

	[Test]
	public async ValueTask BuiltInTakesPrecedence()
	{
		// add() is a built-in; defining a user function with the same name must not override it.
		// The name MUST be the literal built-in here — that is the collision under test.
		Registry.Delete("add");
		var attr = $"ADDATTR{U()}";
		await Cmd($"&{attr} me=overridden");
		await Cmd($"@function add=me,{attr}");

		try
		{
			await Assert.That(await Eval("add(2,3)")).IsEqualTo("5");
		}
		finally
		{
			Registry.Delete("add"); // don't leave a global user 'add' behind for other classes
		}
	}

	[Test]
	public async ValueTask DeleteRemovesFunction()
	{
		var fn = $"delfn{U()}";
		var attr = $"DELATTR{U()}";
		await Cmd($"&{attr} me=present");
		await Cmd($"@function {fn}=me,{attr}");

		await Assert.That(await Eval($"{fn}()")).IsEqualTo("present");

		await Cmd($"@function/delete {fn}");

		await Assert.That(await Eval($"{fn}()")).Contains("COULD NOT FIND FUNCTION");
	}

	[Test]
	public async ValueTask DisableThenEnable()
	{
		var fn = $"togfn{U()}";
		var attr = $"TOGATTR{U()}";
		await Cmd($"&{attr} me=alive");
		await Cmd($"@function {fn}=me,{attr}");

		await Cmd($"@function/disable {fn}");
		await Assert.That(await Eval($"{fn}()")).Contains("COULD NOT FIND FUNCTION");

		await Cmd($"@function/enable {fn}");
		await Assert.That(await Eval($"{fn}()")).IsEqualTo("alive");
	}

	[Test]
	public async ValueTask AliasResolvesToTarget()
	{
		var fn = $"aliasfn{U()}";
		var alias = $"aliasalias{U()}";
		var attr = $"ALIASATTR{U()}";
		await Cmd($"&{attr} me=aliased");
		await Cmd($"@function {fn}=me,{attr}");
		await Cmd($"@function/alias {alias}={fn}");

		await Assert.That(await Eval($"{alias}()")).IsEqualTo("aliased");
	}

	[Test]
	public async ValueTask BootStartupReregistersFunctions()
	{
		var fn = $"bootfn{U()}";
		var attr = $"BOOTATTR{U()}";
		var startupAttr = $"STARTUP{U()}";
		// A STARTUP-style attribute that registers the (uniquely-named) function. Use a unique
		// attribute name so this test's startup body never collides with a real &STARTUP another
		// test or the boot pass might run; we invoke it explicitly below.
		await Cmd($"&{attr} me=booted");
		await Cmd($"&{startupAttr} me=@function {fn}=me,{attr}");

		await Assert.That(Registry.Get(fn)).IsNull();

		// Run that startup body as a command list under God, exactly as the boot pass would.
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var attributeService = WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Known;
		await StartupAttributeRunner.RunObjectAttributeAsync(CommandParser, attributeService, god, startupAttr, god);

		await Assert.That(await Eval($"{fn}()")).IsEqualTo("booted");
	}

	[Test]
	public async ValueTask RestrictRefusesNonWizardButPermitsWizard()
	{
		var fn = $"restrictfn{U()}";
		var attr = $"RESTRICTATTR{U()}";
		await Cmd($"&{attr} me=secret");
		await Cmd($"@function {fn}=me,{attr}");

		var player = await NewNonWizardPlayer();
		await Assert.That(await EvalAs(player, $"{fn}()")).IsEqualTo("secret");

		await Cmd($"@function/restrict {fn}=wizard");

		await Assert.That((await EvalAs(player, $"{fn}()")).ToUpperInvariant()).Contains("PERMISSION DENIED");

		await Assert.That(await Eval($"{fn}()")).IsEqualTo("secret");

		await Cmd($"@function/restrict {fn}=");
		await Assert.That(await EvalAs(player, $"{fn}()")).IsEqualTo("secret");
	}

	[Test]
	public async ValueTask RestrictNegationForbidsWizard()
	{
		// "!wizard" forbids wizards; a non-wizard is still permitted.
		var fn = $"negfn{U()}";
		var attr = $"NEGATTR{U()}";
		await Cmd($"&{attr} me=plebsonly");
		await Cmd($"@function {fn}=me,{attr}");
		await Cmd($"@function/restrict {fn}=!wizard");

		await Assert.That((await Eval($"{fn}()")).ToUpperInvariant()).Contains("PERMISSION DENIED");

		var player = await NewNonWizardPlayer();
		await Assert.That(await EvalAs(player, $"{fn}()")).IsEqualTo("plebsonly");
	}

	[Test]
	public async ValueTask CloneProducesIndependentCopy()
	{
		var fn = $"clonesrc{U()}";
		var clone = $"clonedst{U()}";
		var attr = $"CLONEATTR{U()}";
		await Cmd($"&{attr} me=cloned");
		await Cmd($"@function {fn}=me,{attr}");

		await Cmd($"@function/clone {clone}={fn}");

		await Assert.That(await Eval($"{clone}()")).IsEqualTo("cloned");

		await Cmd($"@function/restrict {clone}=wizard");
		var player = await NewNonWizardPlayer();
		await Assert.That((await EvalAs(player, $"{clone}()")).ToUpperInvariant()).Contains("PERMISSION DENIED");
		await Assert.That(await EvalAs(player, $"{fn}()")).IsEqualTo("cloned");

		await Cmd($"@function/delete {clone}");
		await Assert.That(await Eval($"{clone}()")).Contains("COULD NOT FIND FUNCTION");
		await Assert.That(await Eval($"{fn}()")).IsEqualTo("cloned");
	}

	[Test]
	public async ValueTask BuiltinRestoresOriginalAfterOverride()
	{
		// Delete a built-in so it can be overridden, override it, then /builtin restores the original.
		// Use a UNIQUE attribute but the literal built-in name 'add' — clean up carefully.
		Registry.Delete("add");
		Services.GetRequiredService<IUserDefinedFunctionService>().SetBuiltinRestriction("add", null);
		var attr = $"ADDOVR{U()}";
		await Cmd($"&{attr} me=overridden");

		try
		{
			await Cmd($"@function/delete add");
			await Cmd($"@function add=me,{attr}");
			await Assert.That(await Eval("add(2,3)")).IsEqualTo("overridden");

			await Cmd("@function/builtin add");
			await Assert.That(await Eval("add(2,3)")).IsEqualTo("5");
		}
		finally
		{
			Registry.Delete("add");
			Services.GetRequiredService<IUserDefinedFunctionService>().SetBuiltinRestriction("add", null);
		}
	}

	[Test]
	public async ValueTask PreserveSurvivesRestoreReset()
	{
		// /preserve marks a user function to survive the bulk /restore * reset; an unmarked one is removed.
		var kept = $"keepfn{U()}";
		var dropped = $"dropfn{U()}";
		var attr = $"PRESATTR{U()}";
		await Cmd($"&{attr} me=here");
		await Cmd($"@function {kept}=me,{attr}");
		await Cmd($"@function {dropped}=me,{attr}");

		await Cmd($"@function/preserve {kept}");
		await Cmd("@function/restore *");

		await Assert.That(await Eval($"{kept}()")).IsEqualTo("here");
		await Assert.That(await Eval($"{dropped}()")).Contains("COULD NOT FIND FUNCTION");

		// Cleanup so the global registry doesn't leak the preserved entry to other tests.
		Registry.Delete(kept);
	}

	[Test]
	public async ValueTask RestoreSingleNameRemovesOverride()
	{
		// /restore <name> discards a single user override (like /builtin).
		var fn = $"restoresingle{U()}";
		var attr = $"RESTOREATTR{U()}";
		await Cmd($"&{attr} me=present");
		await Cmd($"@function {fn}=me,{attr}");
		await Assert.That(await Eval($"{fn}()")).IsEqualTo("present");

		await Cmd($"@function/restore {fn}");
		await Assert.That(await Eval($"{fn}()")).Contains("COULD NOT FIND FUNCTION");
	}
}
