using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A character on no account is told after the MOTD what an account is for and how to put the character on one;
/// a character already on an account is not.
/// </summary>
public class NoAccountMessageTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IOptionsWrapper<SharpMUSHOptions> Configuration => WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();

	private const string NotOnAnAccount = "is not on an account";

	[Test]
	public async Task ACharacterOnNoAccountIsToldHowToGetOne()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("NoAcct");
		var home = new DBRef((int)Configuration.CurrentValue.Database.DefaultHome);
		await Mediator.Send(new CreatePlayerCommand(name, "no-account-pass-1", home, home,
			(int)Configuration.CurrentValue.Limit.StartingQuota));

		var handle = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService);
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"connect {name} no-account-pass-1"));

		var shown = string.Join("\n", ConnectScreenTests.NotificationsTo(WebAppFactoryArg, handle));
		await Assert.That(ConnectionService.Get(handle)?.Ref).IsNotNull();
		await Assert.That(shown).Contains(NotOnAnAccount);
		await Assert.That(shown).Contains("claim ");
	}

	[Test, NotInParallel("ConfigMutation")]
	public async Task ACharacterOnAnAccountIsNotTold()
	{
		var username = TestIsolationHelpers.GenerateUniqueName("hasacct");
		var characterName = $"Acc{Guid.NewGuid():N}"[..12];

		var handle = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService);
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"register {username} some-password-1"));
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"create {characterName} some-password-1"));

		await Assert.That(ConnectionService.Get(handle)?.Ref).IsNotNull();
		await Assert.That(ConnectScreenTests.NotificationsTo(WebAppFactoryArg, handle).Any(m => m.Contains(NotOnAnAccount)))
			.IsFalse();
	}
}
