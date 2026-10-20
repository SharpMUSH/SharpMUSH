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
	[Arguments("@mapsql me/row=SELECT 1")]
	[Arguments("@function/delete abs")]
	[Arguments("@function/disable abs")]
	[Arguments("@function/enable abs")]
	[Arguments("@function/alias absalias=abs")]
	[Arguments("@function mortalfn=me,fn")]
	[Arguments("@suggest/add words=mortalword")]
	[Arguments("@suggest/delete words=mortalword")]
	public async Task GameWideCommandsRefuseAnOrdinaryPlayer(string command)
	{
		await Assert.That(await RunAsMortalAsync(command)).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	[Test]
	public async Task AnOrdinaryPlayerCannotDeleteABuiltInFunction()
	{
		await RunAsMortalAsync("@function/delete abs");

		var result = await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain("think abs(-3)"));
		await Assert.That(result.Message.ToPlainText()).IsEqualTo("3");
	}

	[Test]
	[Arguments("followers")]
	[Arguments("lpids")]
	public async Task AnUncontrolledObjectsListIsRefused(string function)
	{
		var target = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "Private");

		await Assert.That(await RunAsMortalAsync($"think {function}({target})")).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}
}
