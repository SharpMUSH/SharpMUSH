using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

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

	/// <summary>A fresh lowercase-hex token, unique per call — used to keep names collision-free.</summary>
	private static string U() => Guid.NewGuid().ToString("N");

	private Task Cmd(string command) =>
		CommandParser.CommandParse(1, ConnectionService, MModule.single(command)).AsTask();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText();

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
		await Cmd($"@function {fn}=me,{attr},2,2"); // min 2, max 2

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
}
