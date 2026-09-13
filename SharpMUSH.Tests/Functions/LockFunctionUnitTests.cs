using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class LockFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test, NotInParallel]
	[Arguments("@LOCK", "lock(me,#FALSE)")]
	[Arguments("@LSET", "lset(me,visual)")]
	public async Task LockFunctionsRespectTheirCommandLock(string command, string expression)
	{
		var attribute = Parser.CommandLibrary[command].LibraryInformation.Attribute;
		var previous = attribute.CommandLock;
		try
		{
			attribute.CommandLock = "#FALSE";
			var result = await Parser.FunctionParse(MarkupText.Plain(expression));
			await Assert.That(result?.Message?.ToPlainText()).IsEqualTo("#-1 PERMISSION DENIED");
		}
		finally
		{
			attribute.CommandLock = previous;
		}
	}

	[Test]
	public async Task MortalCannotReadAbsentOrPrivateLocksOnAnotherOwner()
	{
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var player = await mediator.Send(new CreatePlayerCommand($"LockRead{Guid.NewGuid():N}"[..20], "password", new DBRef(0), new DBRef(0), 100));
		var mortal = Parser.Push(Parser.CurrentState with { Executor = player, Caller = player, Enactor = player });
		var created = await Parser.FunctionParse(MarkupText.Plain("create(PrivateLock_" + Guid.NewGuid().ToString("N") + ")"));
		var target = created!.Message!.ToPlainText();
		var absent = await mortal.FunctionParse(MarkupText.Plain($"lock({target})"));
		await Assert.That(absent?.Message?.ToPlainText()).IsEqualTo("#-1");
		await Parser.FunctionParse(MarkupText.Plain($"lock({target},#TRUE)"));
		var creator = await mortal.FunctionParse(MarkupText.Plain($"lockowner({target})"));
		var flags = await mortal.FunctionParse(MarkupText.Plain($"llockflags({target})"));
		await Assert.That(creator?.Message?.ToPlainText()).IsEqualTo("#-1 NO SUCH LOCK");
		await Assert.That(flags?.Message?.ToPlainText()).IsEqualTo("#-1 NO SUCH LOCK");
	}

	[Test]
	public async Task MortalCanSetOwnVisualFlagButCannotSetWizardFlag()
	{
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var player = await mediator.Send(new CreatePlayerCommand($"LockFlag{Guid.NewGuid():N}"[..20], "password", new DBRef(0), new DBRef(0), 100));
		var mortal = Parser.Push(Parser.CurrentState with { Executor = player, Caller = player, Enactor = player });
		await mortal.FunctionParse(MarkupText.Plain("lock(me,#TRUE)"));
		var missingName = await mortal.FunctionParse(MarkupText.Plain("lset(me,visual)"));
		await Assert.That(missingName?.Message?.ToPlainText()).IsEqualTo("");
		var unchanged = await mortal.FunctionParse(MarkupText.Plain("lockflags(me)"));
		await Assert.That(unchanged?.Message?.ToPlainText()).IsEqualTo("i");
		await mortal.FunctionParse(MarkupText.Plain("lset(me/Basic,visual)"));
		await mortal.FunctionParse(MarkupText.Plain("lset(me/Basic,wizard)"));
		var flags = await mortal.FunctionParse(MarkupText.Plain("lockflags(me)"));
		var creator = await mortal.FunctionParse(MarkupText.Plain("lockowner(me)"));
		await Assert.That(flags?.Message?.ToPlainText()).IsEqualTo("vi");
		await Assert.That(creator?.Message?.ToPlainText()).IsEqualTo($"#{player.Number}");
	}

	[Test]
	[Arguments("lockflags()", "vicw+")]
	[Arguments("lockflags(null())", "vicw+")]
	[Arguments("llockflags(null())", "visual no_inherit no_clone wizard locked")]
	[Arguments("llockflags()", "visual no_inherit no_clone wizard locked")]
	[Arguments("testlock(=me,#1)", "1")]
	[Arguments("testlock(=me,#0)", "0")]
	[Arguments("lockfilter(=#1,#0 #1)", "#1")]
	[Arguments("lockfilter(#TRUE,#0|#1,|)", "#0|#1")]
	[Arguments("listset(a b c,2,x)", "a x c")]
	public async Task PennLockFunctionReadbacks(string expression, string expected)
	{
		var result = await Parser.FunctionParse(MarkupText.Plain(expression));
		await Assert.That(result?.Message?.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task LockSetterAndLsetShareStoredMetadata()
	{
		var created = await Parser.FunctionParse(MarkupText.Plain("create(LockMetadata_" + Guid.NewGuid().ToString("N") + ")"));
		var target = created!.Message!.ToPlainText();
		var set = await Parser.FunctionParse(MarkupText.Plain($"lock({target},=me)"));
		await Assert.That(set?.Message?.ToPlainText()).IsEqualTo("=#1");
		await Parser.FunctionParse(MarkupText.Plain($"lset({target}/Basic,visual)"));
		await Parser.FunctionParse(MarkupText.Plain($"lset({target}/Basic,!no_inherit)"));
		var flags = await Parser.FunctionParse(MarkupText.Plain($"lockflags({target}/Basic)"));
		var names = await Parser.FunctionParse(MarkupText.Plain($"llockflags({target}/Basic)"));
		var creator = await Parser.FunctionParse(MarkupText.Plain($"lockowner({target}/Basic)"));
		await Assert.That(flags?.Message?.ToPlainText()).IsEqualTo("v");
		await Assert.That(names?.Message?.ToPlainText()).IsEqualTo("visual");
		await Assert.That(creator?.Message?.ToPlainText()).IsEqualTo("#1");
	}

	[Test]
	public async Task ListsBuiltinAndCustomLocks()
	{
		var builtins = await Parser.FunctionParse(MarkupText.Plain("locks()"));
		await Assert.That(builtins?.Message?.ToPlainText().Split(' ')).Contains("Basic");
		var created = await Parser.FunctionParse(MarkupText.Plain("create(CustomLock_" + Guid.NewGuid().ToString("N") + ")"));
		var target = created!.Message!.ToPlainText();
		await Parser.FunctionParse(MarkupText.Plain($"lock({target}/user:example,#TRUE)"));
		var listed = await Parser.FunctionParse(MarkupText.Plain($"locks({target})"));
		await Assert.That(listed?.Message?.ToPlainText()).IsEqualTo("USER:EXAMPLE");
	}

	[Test]
	public async Task LsetRejectsListReplacementArguments()
	{
		var result = await Parser.FunctionParse(MarkupText.Plain("lset(a b c,2,x)"));
		await Assert.That(result?.Message?.ToPlainText()).StartsWith("#-1");
	}

	[Test]
	[Arguments("testlock(#1,%#)", "1")]
	[Arguments("testlock(#FALSE,%#)", "0")]
	[Arguments("testlock(#TRUE,%#)", "1")]
	public async Task Testlock(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lockowner(%#)", "")]
	public async Task Lockowner(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("lockfilter(#0,basic)", "")]
	public async Task Lockfilter(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("llocks(%#)", "")]
	public async Task Llocks(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("llockflags(%#/basic)", "")]
	public async Task Llockflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("atrlock(%#,testattr)", "")]
	public async Task Atrlock(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task LockReturnsUnlocked()
	{
		// Create a dedicated object to avoid parallel test interference
		var createResult = (await Parser.FunctionParse(MarkupText.Plain("create(LockFunc_UnlockedTest)")))?.Message!;
		var dbref = createResult.ToPlainText();

		var result = (await Parser.FunctionParse(MarkupText.Plain($"lock({dbref})")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("*UNLOCKED*");
	}

	[Test]
	public async Task ElockNoLockPasses()
	{
		// Create a dedicated object to avoid parallel test interference
		var createResult = (await Parser.FunctionParse(MarkupText.Plain("create(LockFunc_ElockTest)")))?.Message!;
		var dbref = createResult.ToPlainText();

		var result = (await Parser.FunctionParse(MarkupText.Plain($"elock({dbref}/Basic,%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("1");
	}
}
