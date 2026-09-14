using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class ChannelLockBindingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService Connections => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private ILockService Locks => WebAppFactoryArg.Services.GetRequiredService<ILockService>();

	[Test]
	[Arguments("join")]
	[Arguments("speak")]
	[Arguments("see")]
	[Arguments("hide")]
	[Arguments("mod")]
	public async Task MeCapturesSetterAndSurvivesChannelOwnershipChange(string type)
	{
		var (player, name) = await CreateChannelAsync();
		await RunAsync(player.Handle, $"@clock/{type} {name}=me");
		var channel = (await Mediator.Send(new GetChannelQuery(name)))!;
		await Assert.That(Expression(channel, type)).IsEqualTo($"#{player.DbRef.Number}");
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		await Mediator.Send(new UpdateChannelOwnerCommand(channel, god));
		channel = (await Mediator.Send(new GetChannelQuery(name)))!;
		var setter = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<AnySharpObject>();
		await Assert.That(await Locks.Evaluate(Expression(channel, type), channel, setter)).IsTrue();
		await Assert.That(await Locks.Evaluate(Expression(channel, type), channel, new AnySharpObject(god))).IsFalse();
	}

	[Test]
	[Arguments("join", "#TRUE&")]
	[Arguments("speak", "NoSuchChannelLockOperand")]
	[Arguments("see", "#TRUE&")]
	[Arguments("hide", "NoSuchChannelLockOperand")]
	[Arguments("mod", "#TRUE&")]
	public async Task InvalidReplacementPreservesPreviousLock(string type, string invalid)
	{
		var (player, name) = await CreateChannelAsync();
		await RunAsync(player.Handle, $"@clock/{type} {name}=#FALSE");
		await RunAsync(player.Handle, $"@clock/{type} {name}={invalid}");
		var channel = (await Mediator.Send(new GetChannelQuery(name)))!;
		await Assert.That(Expression(channel, type)).IsEqualTo("#FALSE");
	}

	[Test]
	[Arguments("join")]
	[Arguments("speak")]
	[Arguments("see")]
	[Arguments("hide")]
	[Arguments("mod")]
	public async Task EmptyKeyRemovesChannelLock(string type)
	{
		var (player, name) = await CreateChannelAsync();
		await RunAsync(player.Handle, $"@clock/{type} {name}=#FALSE");
		await RunAsync(player.Handle, $"@clock/{type} {name}=");
		var channel = (await Mediator.Send(new GetChannelQuery(name)))!;
		await Assert.That(string.IsNullOrEmpty(Expression(channel, type))).IsTrue();
		var setter = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<AnySharpObject>();
		await Assert.That(await Locks.Evaluate(Expression(channel, type), channel, setter)).IsTrue();
	}

	private async Task<(TestIsolationHelpers.TestPlayer Player, string Name)> CreateChannelAsync()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, Connections, "ClockBinding");
		var owner = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<SharpPlayer>();
		var name = TestIsolationHelpers.GenerateUniqueName("ClockBinding").Replace("_", string.Empty);
		await Mediator.Send(new CreateChannelCommand(MarkupText.Plain(name), ["Player"], owner));
		var channel = (await Mediator.Send(new GetChannelQuery(name)))!;
		await Mediator.Send(new AddUserToChannelCommand(channel, new AnySharpObject(owner)));
		return (player, name);
	}

	private async Task RunAsync(long handle, string command)
		=> await WebAppFactoryArg.CommandParser.CommandParse(handle, Connections, MarkupText.Plain(command));

	private static string Expression(SharpChannel channel, string type) => type switch
	{
		"join" => channel.JoinLock ?? string.Empty,
		"speak" => channel.SpeakLock ?? string.Empty,
		"see" => channel.SeeLock ?? string.Empty,
		"hide" => channel.HideLock ?? string.Empty,
		"mod" => channel.ModLock ?? string.Empty,
		_ => throw new ArgumentOutOfRangeException(nameof(type))
	};
}
