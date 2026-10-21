using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>owner()</c> with a new owner (<c>src/fundb.c:1732-1754</c>) is <c>@chown</c>, or
/// <c>@atrchown</c> for an attribute, and answers 1 when the owner changed and 0 when it did not.
/// </summary>
public class OwnerFunctionTests
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

	private async Task<string> AsGod(string expression)
		=> (await Parser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	private async Task<string> As(TestIsolationHelpers.TestPlayer who, string expression)
		=> (await WebAppFactoryArg.CommandParserFor(who.DbRef, who.Handle)
			.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	[Test]
	public async ValueTask ChangesTheObjectsOwnerAndHaltsIt()
	{
		var receiver = await Mortal("OwnerFnReceiver");
		var item = await Thing("OwnerFnItem");

		await Assert.That(await AsGod($"owner({item},{receiver.DbRef})")).IsEqualTo("1");
		await Assert.That(await AsGod($"owner({item})")).IsEqualTo($"#{receiver.DbRef.Number}");
		await Assert.That(await AsGod($"hasflag({item},HALT)")).IsEqualTo("1");
	}

	[Test]
	public async ValueTask PreserveLeavesTheObjectRunning()
	{
		var receiver = await Mortal("OwnerFnPreserve");
		var item = await Thing("OwnerFnPreserveItem");

		await Assert.That(await AsGod($"owner({item},{receiver.DbRef},pres)")).IsEqualTo("1");
		await Assert.That(await AsGod($"owner({item})")).IsEqualTo($"#{receiver.DbRef.Number}");
		await Assert.That(await AsGod($"hasflag({item},HALT)")).IsEqualTo("0");
	}

	[Test]
	public async ValueTask ChangesAnAttributesOwner()
	{
		var receiver = await Mortal("OwnerFnAttr");
		var item = await Thing("OwnerFnAttrItem");
		await AsGod($"attrib_set({item}/OWNERFNATTR,value)");

		await Assert.That(await AsGod($"owner({item}/OWNERFNATTR,{receiver.DbRef})")).IsEqualTo("1");
		await Assert.That(await AsGod($"owner({item}/OWNERFNATTR)")).IsEqualTo($"#{receiver.DbRef.Number}");
	}

	[Test]
	public async ValueTask ARefusalAnswersZero()
	{
		var mortal = await Mortal("OwnerFnRefused");
		var godsThing = await Thing("OwnerFnRefusedItem");

		await Assert.That(await As(mortal, $"owner({godsThing},me)")).IsEqualTo("0");
		await Assert.That(await AsGod($"owner({godsThing})")).IsNotEqualTo($"#{mortal.DbRef.Number}");
	}
}
