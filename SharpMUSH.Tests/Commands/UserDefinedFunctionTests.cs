using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
/// to stay conflict-free if other test classes run concurrently. The few that have to touch a
/// built-in or the whole registry run alone.
/// </summary>
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
		CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

	/// <summary>Evaluates <paramref name="expression"/> with <paramref name="executor"/> as the executor (not #1).</summary>
	private async Task<string> EvalAs(DBRef executor, string expression)
	{
		var parser = new MUSHCodeParser(
			Services.GetRequiredService<ILogger<MUSHCodeParser>>(),
			Services.GetRequiredService<LibraryService<string, FunctionDefinition>>(),
			Services.GetRequiredService<LibraryService<string, CommandDefinition>>(),
			Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(),
			Services,
			state: ParserState.RootFor(executor) with { Command = "think", Handle = 1 });

		return (await parser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();
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

	/// <summary>
	/// <c>do_function</c> does not look for the attribute (<c>function.c:1687-1692</c>); the call reads it
	/// with <c>atr_get</c> (<c>parse.c:3048</c>), so an attribute the object inherits serves.
	/// </summary>
	[Test]
	public async ValueTask AFunctionMayNameAnInheritedAttribute()
	{
		var fn = $"inh{U()}";
		var attr = $"INHERITED{U()}";
		var parent = await Eval($"create(UDFParent{U()})");
		var child = await Eval($"create(UDFChild{U()})");
		await Cmd($"&{attr} {parent}=mul(%0,3)");
		await Cmd($"@parent {child}={parent}");
		await Cmd($"@function {fn}={child},{attr}");

		await Assert.That(await Eval($"{fn}(7)")).IsEqualTo("21");
	}

	/// <summary>
	/// An attribute that is nowhere to be found is reported when the function is called, in PennMUSH's
	/// words (<c>parse.c:3049-3059</c>), not when it is defined.
	/// </summary>
	[Test]
	public async ValueTask AFunctionWhoseAttributeIsMissingSaysSoWhenCalled()
	{
		var fn = $"gone{U()}";
		var attr = $"GONE{U()}";
		var holder = await Eval($"create(UDFHolder{U()})");
		await Cmd($"@function {fn}={holder},{attr}");

		await Assert.That(await Eval($"{fn}()"))
			.IsEqualTo($"#-1 @FUNCTION ({fn.ToUpperInvariant()}) MISSING ATTRIBUTE (#{DBRef.Parse(holder).Number}/{attr.ToUpperInvariant()})");
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
		await Assert.That(await Eval($"[definitelynotafunction{U()}(1)]")).Contains("NOT FOUND");
	}

	[Test]
	public async ValueTask MinMaxArgCountEnforced()
	{
		var fn = $"argbound{U()}";
		var attr = $"ARGBOUND{U()}";
		await Cmd($"&{attr} me=ok");
		await Cmd($"@function {fn}=me,{attr},2,2");

		await Assert.That((await Eval($"{fn}(a)")).ToUpperInvariant()).Contains("EXPECTS 2 ARGUMENTS BUT GOT 1");
		await Assert.That((await Eval($"{fn}(a,b,c)")).ToUpperInvariant()).Contains("EXPECTS 2 ARGUMENTS BUT GOT 3");
		await Assert.That(await Eval($"{fn}(a,b)")).IsEqualTo("ok");
	}

	[Test]
	[NotInParallel] // overrides add(), which every other test may call
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

		await Assert.That(await Eval($"[{fn}()]")).Contains("NOT FOUND");
	}

	[Test]
	public async ValueTask DisableThenEnable()
	{
		var fn = $"togfn{U()}";
		var attr = $"TOGATTR{U()}";
		await Cmd($"&{attr} me=alive");
		await Cmd($"@function {fn}=me,{attr}");

		await Cmd($"@function/disable {fn}");
		await Assert.That(await Eval($"[{fn}()]")).Contains("NOT FOUND");

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
		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
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
	public async ValueTask RestrictNobodyDisablesTheFunction()
	{
		// nobody is FN_DISABLED (src/function.c): the call answers e_disabled, for a wizard too, rather
		// than e_perm (src/parse.c).
		var fn = $"nobodyfn{U()}";
		var attr = $"NOBODYATTR{U()}";
		await Cmd($"&{attr} me=never");
		await Cmd($"@function {fn}=me,{attr}");
		await Cmd($"@function/restrict {fn}=nobody");

		var player = await NewNonWizardPlayer();
		await Assert.That(await EvalAs(player, $"{fn}()")).IsEqualTo(ErrorMessages.Returns.FunctionDisabled);
		await Assert.That(await Eval($"{fn}()")).IsEqualTo(ErrorMessages.Returns.FunctionDisabled);
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
		await Assert.That(await Eval($"[{clone}()]")).Contains("NOT FOUND");
		await Assert.That(await Eval($"{fn}()")).IsEqualTo("cloned");
	}

	[Test]
	[NotInParallel] // overrides add(), which every other test may call
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
	[NotInParallel] // overrides add(), which every other test may call
	public async ValueTask RestrictBuiltinRestrictsTheBuiltinNotTheOverride()
	{
		// PennMUSH: /restrict with /builtin restricts the built-in of that name, even while a @function
		// overrides it; it does not run /builtin (which would drop the override).
		Registry.Delete("add");
		Registry.SetBuiltinRestriction("add", null);
		var attr = $"ADDRB{U()}";
		await Cmd($"&{attr} me=overridden");

		try
		{
			await Cmd("@function/delete add");
			await Cmd($"@function add=me,{attr}");

			await Cmd("@function/restrict/builtin add=wizard");

			await Assert.That(Registry.GetBuiltinRestriction("add")).IsEqualTo("wizard");
			await Assert.That(Registry.Get("add")?.Restriction).IsNull();
			await Assert.That(await Eval("add(2,3)")).IsEqualTo("overridden");
		}
		finally
		{
			// /builtin also puts the built-in back in the function library, which /delete took it out of.
			await Cmd("@function/builtin add");
		}
	}

	[Test]
	[NotInParallel] // @function/restore * drops every other test's user function
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
		await Assert.That(await Eval($"[{dropped}()]")).Contains("NOT FOUND");

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
		await Assert.That(await Eval($"[{fn}()]")).Contains("NOT FOUND");
	}
	[Test]
	[NotInParallel] // overrides add(), which every other test may call
	public async ValueTask FnCallsADeletedBuiltin()
	{
		// PennMUSH's fn() calls the built-in even while @function/delete has a @function in its place.
		Registry.Delete("add");
		Registry.SetBuiltinRestriction("add", null);
		var attr = $"ADDFN{U()}";
		await Cmd($"&{attr} me=overridden");

		try
		{
			await Cmd("@function/delete add");
			await Cmd($"@function add=me,{attr}");

			await Assert.That(await Eval("add(2,3)")).IsEqualTo("overridden");
			await Assert.That(await Eval("fn(add,2,3)")).IsEqualTo("5");
		}
		finally
		{
			await Cmd("@function/builtin add");
		}
	}

	[Test]
	public async ValueTask DefinitionTakesRestrictionWords()
	{
		var fn = $"defrestrict{U()}";
		var attr = $"DEFRESTRICT{U()}";
		await Cmd($"&{attr} me=secret");
		await Cmd($"@function {fn}=me,{attr},0,1,wizard");

		await Assert.That(Registry.Get(fn)?.Restriction).IsEqualTo("wizard");
		await Assert.That((await EvalAs(await NewNonWizardPlayer(), $"{fn}()")).ToUpperInvariant()).Contains("PERMISSION DENIED");
		await Assert.That(await Eval($"{fn}()")).IsEqualTo("secret");
	}

	[Test]
	public async ValueTask DefinitionTakesObjectSlashAttribute()
	{
		var fn = $"slashform{U()}";
		var attr = $"SLASHFORM{U()}";
		await Cmd($"&{attr} me=slashed");
		await Cmd($"@function {fn}=me/{attr}");

		await Assert.That(await Eval($"{fn}()")).IsEqualTo("slashed");
	}

	[Test]
	public async ValueTask ASingleArgumentWithoutASlashDefinesNothing()
	{
		var fn = $"noslash{U()}";
		await Cmd($"@function {fn}=me");

		await Assert.That(Registry.Get(fn)).IsNull();
	}

	[Test]
	public async ValueTask NosidefxOnABuiltinDisablesItsSideEffects()
	{
		var clone = $"pemitclone{U()}";
		await Cmd($"@function/clone {clone}=pemit");
		await Cmd($"@function/restrict {clone}=nosidefx");

		await Assert.That(await Eval($"{clone}(me,hi)")).IsEqualTo(ErrorMessages.Returns.FunctionDisabled);
	}

	[Test]
	public async ValueTask LocalizeOnABuiltinRestoresTheRegisters()
	{
		var clone = $"setrclone{U()}";
		await Cmd($"@function/clone {clone}=setr");
		await Cmd($"@function/restrict {clone}=localize");

		await Assert.That(await Eval($"[setq(a,outer)][{clone}(a,inner)]-%qa")).IsEqualTo("inner-outer");
	}

	private static IDisposable FunctionCaller(bool on)
		=> TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { MuxFunctionCaller = on }
		});

	/// <summary>Defines a function on a new object whose attribute answers who runs it; returns its name and the object.</summary>
	private async Task<(string Function, string Holder)> DefineWhoRunsFunction(string switches = "")
	{
		var fn = $"who{U()}";
		var attr = $"WHO{U()}";
		var holder = await Eval($"create(UDFHolder{U()})");
		await Cmd($"&{attr} {holder}=%!");
		await Cmd($"@function{switches} {fn}={holder},{attr}");
		return (fn, holder);
	}

	/// <summary>PennMUSH, and the default: a global function runs as the object that holds it.</summary>
	[Test]
	public async ValueTask AGlobalFunctionRunsAsItsHolderByDefault()
	{
		var (fn, holder) = await DefineWhoRunsFunction();
		var player = await NewNonWizardPlayer();

		using (FunctionCaller(false))
		{
			await Assert.That(await EvalAs(player, $"{fn}()")).IsEqualTo(holder);
		}
	}

	/// <summary>TinyMUX: with <c>mux_function_caller</c> on, a function defined without /privileged runs as its caller.</summary>
	[Test]
	public async ValueTask WithMuxFunctionCallerAnUnprivilegedFunctionRunsAsItsCaller()
	{
		var (fn, _) = await DefineWhoRunsFunction();
		var player = await NewNonWizardPlayer();

		using (FunctionCaller(true))
		{
			await Assert.That(await EvalAs(player, $"{fn}()")).IsEqualTo($"#{player.Number}");
		}
	}

	/// <summary>TinyMUX: /privileged runs the function as the object that holds it.</summary>
	[Test]
	public async ValueTask WithMuxFunctionCallerAPrivilegedFunctionRunsAsItsHolder()
	{
		var (fn, holder) = await DefineWhoRunsFunction("/privileged");
		var player = await NewNonWizardPlayer();

		using (FunctionCaller(true))
		{
			await Assert.That(await EvalAs(player, $"{fn}()")).IsEqualTo(holder);
		}

		await Assert.That(Registry.Get(fn)!.Privileged).IsTrue();
	}

	/// <summary>
	/// Run as the caller, the function has only the caller's permissions: it cannot read an attribute the
	/// caller cannot, though the object that holds it can.
	/// </summary>
	[Test]
	public async ValueTask AFunctionRunAsItsCallerHasTheCallersPermissions()
	{
		var fn = $"peek{U()}";
		var attr = $"PEEK{U()}";
		var holder = await Eval($"create(UDFHolder{U()})");
		await Cmd($"&SECRET {holder}=hidden");
		await Cmd($"&{attr} {holder}=[get({holder}/SECRET)]");
		await Cmd($"@function {fn}={holder},{attr}");
		var player = await NewNonWizardPlayer();

		using (FunctionCaller(false))
		{
			await Assert.That(await EvalAs(player, $"{fn}()")).IsEqualTo("hidden");
		}

		using (FunctionCaller(true))
		{
			await Assert.That(await EvalAs(player, $"{fn}()")).IsNotEqualTo("hidden");
		}
	}

	/// <summary>
	/// TinyMUX's <c>@function/preserve</c> takes a definition and keeps the caller's registers; with
	/// <c>/privileged</c> beside it, the function is still defined and stored as privileged.
	/// </summary>
	[Test]
	public async ValueTask PreserveWithADefinitionDefinesALocalizedFunction()
	{
		var fn = $"pres{U()}";
		var attr = $"PRES{U()}";
		await Cmd($"&{attr} me=[setq(a,inner)]%qa");
		await Cmd($"@function/preserve/privileged {fn}=me/{attr}");

		await Assert.That(await Eval($"[setq(a,outer)][{fn}()]-%qa")).IsEqualTo("inner-outer");
		var defined = Registry.Get(fn)!;
		await Assert.That(defined.Privileged).IsTrue();
		await Assert.That(defined.Restriction).IsEqualTo("localize");
		await Assert.That(defined.Preserved).IsFalse();
	}

	/// <summary>PennMUSH's <c>@function/preserve &lt;name&gt;</c>, with no definition, still marks the function.</summary>
	[Test]
	public async ValueTask PreserveWithANameAloneMarksTheFunction()
	{
		var fn = $"mark{U()}";
		var attr = $"MARK{U()}";
		await Cmd($"&{attr} me=1");
		await Cmd($"@function {fn}=me/{attr}");
		await Cmd($"@function/preserve {fn}");

		await Assert.That(Registry.Get(fn)!.Preserved).IsTrue();
		await Assert.That(Registry.Get(fn)!.Restriction).IsNull();
	}
}
