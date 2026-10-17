using System.Text;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using EngineCommands = SharpMUSH.Implementation.Commands.Commands;

namespace SharpMUSH.Tests.Commands;

public class NativePostBindFailureServerFactory : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
}

public class NativePostBindFailureTests
{
	[ClassDataSource<NativePostBindFailureServerFactory>(Shared = SharedType.PerTestSession)]
	public required NativePostBindFailureServerFactory Factory { get; init; }

	private async Task<(DBRef Character, string AccountId, NativeLoginSecretMint Mint, long Handle)> PrepareAsync()
	{
		var services = Factory.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var accounts = services.GetRequiredService<IAccountService>();
		var sessions = services.GetRequiredService<IAccountSessionStore>();
		var character = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "postbind");
		var account = (await accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("postbindowner"), null, "account-password")).Expect<SharpAccount>();
		await accounts.LinkCharacterAsync(account.Id!, character);
		var session = await sessions.CreateTokenAsync(account.Id!, TimeSpan.FromMinutes(15), "203.0.113.8");
		var authority = (await mediator.Send(new GetNativeCharacterAuthorityQuery(account.Id!, character))).Expect<NativeCharacterAuthority>();
		var grant = services.GetRequiredService<IRecentAccountProofStore>().Issue(account.Id!, session,
			AccountSecurityOperation.NativeLogin, new("password", null, authority.AccountCredentialEpoch)).Expect<RecentAccountProof>();
		var mint = (await services.GetRequiredService<NativeLoginService>().MintAsync(account.Id!, session, character, "203.0.113.8", grant.Grant)).Expect<NativeLoginSecretMint>();
		var handle = TestIsolationHelpers.GenerateUniqueHandle();
		await services.GetRequiredService<IConnectionService>().Register(handle, "203.0.113.9", "client.example.test", "telnet",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		return (character, account.Id!, mint, handle);
	}

	private IMUSHCodeParser Parser(long handle, string secret) => Factory.CommandParser.FromState(Factory.CommandParser.CurrentState with
	{
		Handle = handle,
		Arguments = new Dictionary<string, CallState> { ["0"] = new("token " + secret) }
	});

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task CompletionEventFailureCannotTurnACommittedNativeBindingIntoCredentialFailure(bool canceled)
	{
		var prepared = await PrepareAsync();
		var services = Factory.Services;
		var connections = services.GetRequiredService<IConnectionService>();
		var events = Substitute.For<IEventService>();
		var logger = Substitute.For<ILogger<EngineCommands>>();
		events.TriggerEventAsync(SharpEvents.PlayerConnect, Arg.Any<DBRef?>(), Arg.Any<string[]>())
			.Returns(_ => ValueTask.FromException(canceled ? new OperationCanceledException(prepared.Mint.Secret) : new InvalidOperationException(prepared.Mint.Secret)));
		var commands = ActivatorUtilities.CreateInstance<EngineCommands>(services, events, logger);
		try
		{
			var result = await commands.Connect(Parser(prepared.Handle, prepared.Mint.Secret), new SharpCommandAttribute { Name = "CONNECT" });
			await Assert.That(connections.Get(prepared.Handle)!.Ref).IsEqualTo(prepared.Character);
			await Assert.That(result.Expect<CallState>().Message.ToString()).IsEqualTo(new CallState(prepared.Character).Message.ToString());
			await events.Received(1).TriggerEventAsync(SharpEvents.PlayerConnect, Arg.Any<DBRef?>(), Arg.Any<string[]>());
			await events.DidNotReceive().TriggerEventAsync(SharpEvents.SocketLoginFail, Arg.Any<DBRef?>(), Arg.Any<string[]>());
			await Assert.That(Factory.Notifications.ForHandle(prepared.Handle).Any(message => message.Contains("Invalid or expired login token", StringComparison.Ordinal))).IsFalse();
			var logs = logger.ReceivedCalls().Where(call => call.GetMethodInfo().Name == "Log").Select(call => call.GetArguments()).ToArray();
			await Assert.That(logs.Any(arguments => arguments[0] is LogLevel.Warning)).IsTrue();
			await Assert.That(logs.Any(arguments => arguments[3] is not null || arguments.Any(argument => argument?.ToString()?.Contains(prepared.Mint.Secret, StringComparison.Ordinal) == true))).IsFalse();
		}
		finally
		{
			await connections.Disconnect(prepared.Handle);
			await services.GetRequiredService<IAccountSessionStore>().RevokeAllForAccountAsync(prepared.AccountId);
		}
	}

	[Test]
	public async Task CancellationBeforeTheBindingPropagatesWithoutCredentialFailureNotification()
	{
		var prepared = await PrepareAsync();
		var services = Factory.Services;
		var connections = services.GetRequiredService<IConnectionService>();
		var policy = Substitute.For<INativeLoginPolicy>();
		policy.CanConnectAsync(Arg.Any<DBRef>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(_ => ValueTask.FromException<bool>(new OperationCanceledException(prepared.Mint.Secret)));
		var native = new NativeLoginService(services.GetRequiredService<INativeLoginSecretStore>(), services.GetRequiredService<IAccountSessionStore>(),
			services.GetRequiredService<IRecentAccountProofStore>(), services.GetRequiredService<IMediator>(), services.GetRequiredService<AccountCredentialGate>(),
			policy, services.GetRequiredService<IPasswordService>(), services.GetRequiredService<IValidateService>(), services.GetRequiredService<TimeProvider>(),
			services.GetService<IAccountProofAuthorityValidator>());
		var events = Substitute.For<IEventService>();
		var commands = ActivatorUtilities.CreateInstance<EngineCommands>(services, native, events);
		try
		{
			await Assert.That(async () => await commands.Connect(Parser(prepared.Handle, prepared.Mint.Secret), new SharpCommandAttribute { Name = "CONNECT" })).Throws<OperationCanceledException>();
			await Assert.That(connections.Get(prepared.Handle)!.Ref).IsNull();
			await events.DidNotReceive().TriggerEventAsync(SharpEvents.SocketLoginFail, Arg.Any<DBRef?>(), Arg.Any<string[]>());
		}
		finally
		{
			await connections.Disconnect(prepared.Handle);
			await services.GetRequiredService<IAccountSessionStore>().RevokeAllForAccountAsync(prepared.AccountId);
		}
	}
}
