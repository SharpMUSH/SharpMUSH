using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>Game-wide commands and other objects' private lists are closed to an ordinary player.</summary>
public class PrivilegedCommandPermissionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();

	private async Task<string> RunAsMortalAsync(string command)
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "Mortal");
		try
		{
			var result = await Factory.CommandParser.CommandParse(mortal.Handle, Connections, MarkupText.Plain(command));
			return result.Message.ToPlainText();
		}
		finally
		{
			await Connections.Disconnect(mortal.Handle);
		}
	}

	[Test]
	[Arguments("@mapsql me/row=SELECT 1", "#-1 PERMISSION DENIED")]
	[Arguments("@function/delete abs", "#-1 PERMISSION DENIED: NEEDS CONFIG.ADMIN OR GAME.FUNCTIONS")]
	[Arguments("@function/disable abs", "#-1 PERMISSION DENIED: NEEDS CONFIG.ADMIN OR GAME.FUNCTIONS")]
	[Arguments("@function/enable abs", "#-1 PERMISSION DENIED: NEEDS CONFIG.ADMIN OR GAME.FUNCTIONS")]
	[Arguments("@function/alias absalias=abs", "#-1 PERMISSION DENIED: NEEDS CONFIG.ADMIN OR GAME.FUNCTIONS")]
	[Arguments("@function mortalfn=me,fn", "#-1 PERMISSION DENIED: NEEDS CONFIG.ADMIN OR GAME.FUNCTIONS")]
	[Arguments("@suggest/add words=mortalword", "#-1 PERMISSION DENIED: NEEDS CONFIG.ADMIN")]
	[Arguments("@suggest/delete words=mortalword", "#-1 PERMISSION DENIED: NEEDS CONFIG.ADMIN")]
	public async Task GameWideCommandsRefuseAnOrdinaryPlayer(string command, string refusal)
	{
		await Assert.That(await RunAsMortalAsync(command)).IsEqualTo(refusal);
	}

	[Test]
	public async Task AnOrdinaryPlayerCannotDeleteABuiltInFunction()
	{
		await RunAsMortalAsync("@function/delete abs");

		var result = await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain("think abs(-3)"));
		await Assert.That(result.Message.ToPlainText()).IsEqualTo("3");
	}

	[Test]
	[Arguments("fnpower")]
	[Arguments("fnpower-sa5-ufap3f0")]
	public async Task AHolderOfTheFunctionsPowerCanDefineAGlobalFunction(string prefix)
	{
		// The run id is Base64Url, and a function name cannot contain its "-".
		var name = new string([.. TestIsolationHelpers.GenerateUniqueName(prefix).Where(char.IsAsciiLetterOrDigit)]).ToLowerInvariant();
		var holder = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "FnPower");
		await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@power {holder.DbRef}=Functions"));
		try
		{
			var parser = Factory.CommandParserFor(holder.DbRef, holder.Handle);
			await parser.CommandParse(holder.Handle, Connections, MarkupText.Plain("&FN me=defined"));
			await parser.CommandParse(holder.Handle, Connections, MarkupText.Plain($"@function {name}=me,FN"));

			var result = await parser.CommandParse(holder.Handle, Connections, MarkupText.Plain($"think {name}()"));
			await Assert.That(result.Message.ToPlainText()).IsEqualTo("defined");
		}
		finally
		{
			await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@function/delete {name}"));
			await Connections.Disconnect(holder.Handle);
		}
	}

	[Test]
	[Arguments("followers", "#-1 PERMISSION DENIED: NOT CONTROLLED")]
	[Arguments("lpids", "#-1 PERMISSION DENIED")]
	public async Task AnUncontrolledObjectsListIsRefused(string function, string refusal)
	{
		var target = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "Private");

		await Assert.That(await RunAsMortalAsync($"think {function}({target})")).IsEqualTo(refusal);
	}
}
