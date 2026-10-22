using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The caller checks PennMUSH makes in <c>mtime()</c>/<c>msecs()</c> (<c>src/fundb.c:1503-1529</c>),
/// <c>iname()</c> (<c>src/fundb.c:1868-1890</c>) and <c>controls()</c> (<c>src/fundb.c:915-950</c>).
/// Every refusal is asked as a mortal, since the shared fixtures run as God.
/// </summary>
public class FunctionPermissionGateTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private Task<TestIsolationHelpers.TestPlayer> Mortal(string prefix)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private async Task<DBRef> Thing(string prefix)
		=> await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, prefix);

	private async Task God(string command)
		=> await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private async Task<string> As(TestIsolationHelpers.TestPlayer who, string expression)
		=> (await WebAppFactoryArg.CommandParserFor(who.DbRef, who.Handle)
			.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	private async Task<string> AsGod(string expression)
		=> (await Parser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	[Test]
	[Arguments("mtime")]
	[Arguments("msecs")]
	public async ValueTask ModifiedTimeNeedsExamine(string function)
	{
		var mortal = await Mortal($"Gate{function}");
		var godsThing = await Thing($"Gate{function}Item");

		await Assert.That(await As(mortal, $"{function}({godsThing})")).IsEqualTo("#-1 PERMISSION DENIED");
		await Assert.That(await AsGod($"{function}({godsThing})")).IsNotEqualTo("#-1 PERMISSION DENIED");
	}

	[Test]
	public async ValueTask InameNeedsControlSeeAllOrBeingInside()
	{
		var mortal = await Mortal("GateIname");
		var godsThing = await Thing("GateInameItem");

		await Assert.That(await As(mortal, $"iname({godsThing})")).IsEqualTo("#-1 PERMISSION DENIED");

		await God($"@teleport/silent {mortal.DbRef}={godsThing}");
		await Assert.That(await As(mortal, $"iname({godsThing})")).StartsWith("GateInameItem");
	}

	[Test]
	public async ValueTask InameUsesNameformat()
	{
		var item = await Thing("GateInameFormat");
		await God($"&NAMEFORMAT {item}=Inside %1 (%0)");

		await Assert.That(await AsGod($"iname({item})")).IsEqualTo($"Inside {await AsGod($"name({item})")} ({item})");

		await God($"&NAMEFORMAT {item}=[null(empty)]");
		await Assert.That(await AsGod($"iname({item})")).IsEqualTo(string.Empty);
	}

	[Test]
	public async ValueTask ControlsNamesTheMissingSide()
	{
		await Assert.That(await AsGod("controls(#-5,me)")).IsEqualTo("#-1 ARG1 NOT FOUND");
		await Assert.That(await AsGod("controls(me,#-5)")).IsEqualTo("#-1 ARG2 NOT FOUND");
		await Assert.That(await AsGod("controls(me,me/`BAD)")).IsEqualTo("#-1 BAD ATTR NAME");
	}

	[Test]
	public async ValueTask ControlsNeedsTheCallerToControlASide()
	{
		var mortal = await Mortal("GateControls");
		var first = await Thing("GateControlsA");
		var second = await Thing("GateControlsB");

		await Assert.That(await As(mortal, $"controls({first},{second})")).IsEqualTo("#-1 PERMISSION DENIED");
		await Assert.That(await As(mortal, $"controls({mortal.DbRef},{first})")).IsEqualTo("0");
	}
}
