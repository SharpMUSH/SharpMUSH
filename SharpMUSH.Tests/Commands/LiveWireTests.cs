using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// RhostMUSH's <c>@livewire/funceval</c>: one object's own function invocation limit, its owner's when it has
/// none, lower always and higher only with <c>/funcover</c>.
/// </summary>
public class LiveWireTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();

	/// <summary>200 add() calls, plus iter(), lnum() and strlen(): 399 when it finishes.</summary>
	private const string Light = "[strlen(iter(lnum(200),add(1,1)))]";

	/// <summary>30000 add() calls, more than the game's 25000: 59999 when it finishes.</summary>
	private const string Heavy = "[strlen(iter(lnum(30000),add(1,1)))]";

	private async Task<string> Read(string expression)
		=> (await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

	private async Task Run(string command)
		=> await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(command));

	/// <summary>The lines #1 was told while <paramref name="command"/> ran that hold <paramref name="marker"/>.</summary>
	private async Task<string[]> Output(string command, string marker)
	{
		var recipient = Factory.ExecutorDBRef;
		var count = Factory.Notifications.CountFor(recipient);
		await Run(command);
		return [.. Factory.Notifications.For(recipient).Skip(count).Where(line => line.Contains(marker))];
	}

	/// <summary>A thing holding <see cref="Light"/> and <see cref="Heavy"/>, so <c>u()</c> runs them as the thing.</summary>
	private async Task<string> CodeHolderAsync()
	{
		var thing = (await Read($"create({TestIsolationHelpers.GenerateUniqueName("LiveWire")})")).Split(':')[0];
		await Run($"&LIGHT {thing}={Light}");
		await Run($"&HEAVY {thing}={Heavy}");
		return thing;
	}

	[Test]
	public async Task ALowerLimitAppliesWithoutTheOverride()
	{
		var thing = await CodeHolderAsync();
		await Assert.That(await Read($"u({thing}/LIGHT)")).IsEqualTo("399");

		await Run($"@livewire/funceval {thing}=100");

		await Assert.That(await Read($"u({thing}/LIGHT)")).Contains(ErrorMessages.Returns.Invoke);
		await Assert.That(await Read("strlen(iter(lnum(200),add(1,1)))")).IsEqualTo("399");
	}

	[Test]
	public async Task AHigherLimitAppliesOnlyWithTheOverride()
	{
		var thing = await CodeHolderAsync();
		await Assert.That(await Read($"u({thing}/HEAVY)")).Contains(ErrorMessages.Returns.Invoke);

		await Run($"@livewire/funceval {thing}=40000");
		await Assert.That(await Read($"u({thing}/HEAVY)")).Contains(ErrorMessages.Returns.Invoke);

		await Run($"@livewire/funcover {thing}=1");
		await Assert.That(await Read($"u({thing}/HEAVY)")).IsEqualTo("59999");

		await Run($"@livewire/funceval {thing}");
		await Assert.That(await Read($"u({thing}/HEAVY)")).Contains(ErrorMessages.Returns.Invoke);
	}

	[Test]
	public async Task AnObjectWithoutALimitUsesItsOwners()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(Factory.Services, Mediator, "LiveWireOwner");
		var owned = await CodeHolderAsync();
		var own = await CodeHolderAsync();
		// @chown sets HALT, as PennMUSH's does.
		await Run($"@chown {owned}={player}");
		await Run($"@chown {own}={player}");
		await Run($"@set {owned}=!halt");
		await Run($"@set {own}=!halt");
		await Run($"@livewire/funceval {player}=100");
		await Run($"@livewire/funceval {own}=1000");

		await Assert.That(await Read($"u({owned}/LIGHT)")).Contains(ErrorMessages.Returns.Invoke);
		await Assert.That(await Read($"u({own}/LIGHT)")).IsEqualTo("399");
	}

	[Test]
	public async Task TheCommandAndFunctionReportTheLimit()
	{
		var thing = await CodeHolderAsync();
		var number = thing.TrimStart('#').Split(':')[0];

		await Assert.That(await Output($"@livewire/funceval {thing}=100000", $"(#{number})"))
			.Contains(line => line.EndsWith($"(#{number}) FuncEval changed [0 -> 100000]"));
		await Assert.That(await Output($"@livewire/funceval {thing}=100000", "same as previous"))
			.Contains("@livewire: Evaluation value is same as previous value. Ignoring.");
		await Assert.That(await Output($"@livewire/funcover {thing}=1", $"(#{number})"))
			.Contains(line => line.EndsWith($"(#{number}) FuncOverride changed [0 -> 1]"));
		await Assert.That(await Output($"@livewire/list {thing}", $"(#{number})"))
			.Contains(line => line.EndsWith("-- FuncEval 100000, FuncEvalOverride 1"));

		await Assert.That(await Read($"livewire(funceval,{thing})")).IsEqualTo("25000 100000 1");
		await Assert.That(await Read($"livewire(func,{thing},1)")).IsEqualTo("25000 100000 1 0 0");
		await Assert.That(await Read($"livewire(,{thing})")).IsEqualTo("Valid livewires: funceval");
		await Assert.That(await Read($"livewire(queuemax,{thing})")).IsEqualTo("#-1 INVALID LIVEWIRE ARGUMENT");
	}

	[Test]
	public async Task SoftcodeCannotReadOrWriteTheStoredLimit()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "LiveWireWriter");
		var thing = await CodeHolderAsync();
		await Run($"@chown {thing}={player.DbRef}");
		await Run($"@livewire/funceval {thing}=100");

		await Factory.CommandParserFor(player.DbRef, player.Handle).CommandParse(player.Handle, Connections,
			MarkupText.Plain($"&{LiveWire.AttributeName} {thing}=1000000000 1"));
		var asOwner = Factory.FunctionParserFor(player.DbRef);

		await Assert.That(await Read($"livewire(funceval,{thing})")).IsEqualTo("25000 100 0");
		await Assert.That((await asOwner.EvaluateAsync(MarkupText.Plain($"get({thing}/{LiveWire.AttributeName})"))).ToPlainText())
			.DoesNotContain("100");
		await Assert.That((await asOwner.EvaluateAsync(MarkupText.Plain($"lattr({thing})"))).ToPlainText())
			.DoesNotContain(LiveWire.AttributeName);
	}

	[Test]
	public async Task OnlyConfigAdministratorsMaySetIt()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "LiveWireMortal");
		var thing = await CodeHolderAsync();
		await Run($"@chown {thing}={player.DbRef}");

		await Factory.CommandParserFor(player.DbRef, player.Handle).CommandParse(player.Handle, Connections,
			MarkupText.Plain($"@livewire/funceval {thing}=100"));

		await Assert.That(await Read($"livewire(funceval,{thing})")).IsEqualTo("25000 0 0");
		await Assert.That((await Factory.FunctionParserFor(player.DbRef).EvaluateAsync(MarkupText.Plain($"livewire(funceval,{thing})")))
			.ToPlainText()).IsEqualTo("25000 0 0");
		await Assert.That((await Factory.FunctionParserFor(player.DbRef).EvaluateAsync(MarkupText.Plain($"livewire(funceval,{Factory.ExecutorDBRef})")))
			.ToPlainText()).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	[Test]
	[Arguments("100000 1", 100000u, true)]
	[Arguments("5 0", 5u, false)]
	[Arguments("-3 1", 0u, true)]
	[Arguments("2000000000 0", LiveWire.MaximumLimit, false)]
	[Arguments("100000", 0u, false)]
	[Arguments("lots 1", 0u, false)]
	[Arguments("", 0u, false)]
	public async Task TheStoredTextReadsAsRhostMushReadsIt(string text, uint limit, bool overriding)
	{
		await Assert.That(LiveWire.Parse(text)).IsEqualTo(new LiveWire(limit, overriding));
	}

	[Test]
	[Arguments(100u, false, 25000u, true)]
	[Arguments(30000u, false, 25000u, false)]
	[Arguments(30000u, true, 25000u, true)]
	[Arguments(25000u, true, 25000u, false)]
	[Arguments(0u, true, 25000u, false)]
	public async Task ALimitReplacesTheGamesWhenLowerOrOverridden(uint limit, bool overriding, uint game, bool replaces)
	{
		await Assert.That(new LiveWire(limit, overriding).Replaces(game)).IsEqualTo(replaces);
	}
}
