using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Implementation;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class InputCallbackReplacementTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private T Get<T>() where T : notnull => Factory.Services.GetRequiredService<T>();
	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
		public override DateTimeOffset GetUtcNow() => Now;
	}

	[Test]
	[Arguments(false, "failure")]
	[Arguments(true, "failure")]
	[Arguments(false, "success")]
	[Arguments(true, "success")]
	[Arguments(false, "external")]
	[Arguments(true, "external")]
	public async Task CallbackFailureRetiresOnlyItsOwnReplacement(bool timeout, string outcome)
	{
		var connections = Get<IConnectionService>();
		var mediator = Get<IMediator>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, connections, "CallbackReplacement");
		var clock = new Clock();
		var sessions = new InputSessionService(connections, mediator, Get<IAttributeService>(), Get<IPermissionService>(), Get<INotifyService>(), clock);
		var originalParser = (MUSHCodeParser)Get<IMUSHCodeParser>();
		var commands = new CommandLibraryService();
		foreach (var pair in originalParser.CommandLibrary) commands.Add(pair.Key, pair.Value);
		var finish = "@finishreplacement" + Guid.NewGuid().ToString("N");
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Guid? callbackReplacement = null;
		commands.Add(finish, (new CommandDefinition(
			new SharpCommandAttribute { Name = finish, Behavior = CommandBehavior.Default, MinArgs = 0, MaxArgs = 0 },
			async _ =>
			{
				callbackReplacement = sessions.GetCapturing(player.Handle)?.Id;
				entered.TrySetResult();
				if (outcome == "external") await release.Task;
				if (outcome != "success") throw new InvalidOperationException("callback failed after starting replacement");
				return Option<CallState>.FromOption(CallState.Empty);
			}), true));
		var provider = Substitute.For<IServiceProvider>();
		provider.GetService(Arg.Any<Type>()).Returns(call => call.Arg<Type>() == typeof(IInputSessionService)
			? sessions : Factory.Services.GetService(call.Arg<Type>()));
		var parser = new MUSHCodeParser(originalParser.Logger, originalParser.FunctionLibrary, commands, originalParser.Configuration, provider);
		Task<CallState?>? delivery = null;
		try
		{
			var actor = (await mediator.Send(new GetObjectNodeQuery(player.DbRef))).Known;
			await Get<IAttributeService>().SetAttributeAsync(actor, actor, "REPLACEMENT", MarkupText.Plain("think next"));
			await Get<IAttributeService>().SetAttributeAsync(actor, actor, "EXTERNAL", MarkupText.Plain("think unrelated"));
			await Get<IAttributeService>().SetAttributeAsync(actor, actor, "CALLBACK", MarkupText.Plain($"@input/start me/REPLACEMENT=Next:,120; {finish}"));
			await parser.CommandParse(player.Handle, connections, MarkupText.Plain("@input/start me/CALLBACK=First:,120"));
			var session = sessions.GetCapturing(player.Handle)!;
			await Assert.That(session).IsNotNull();
			if (timeout)
			{
				clock.Now += TimeSpan.FromMinutes(3);
				await Assert.That(sessions.TakeExpired().Single().Id).IsEqualTo(session.Id);
			}
			delivery = sessions.DeliverAsync(parser, session, MarkupText.Plain("reply"), timeout).AsTask();
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			await Assert.That(callbackReplacement).IsNotNull();
			await Assert.That(callbackReplacement).IsNotEqualTo(session.Id);
			Guid? external = null;
			if (outcome == "external")
			{
				await parser.CommandParse(player.Handle, connections, MarkupText.Plain("@input/start me/EXTERNAL=Unrelated:,120"));
				external = sessions.GetCapturing(player.Handle)?.Id;
				await Assert.That(external).IsNotNull();
				await Assert.That(external).IsNotEqualTo(callbackReplacement);
				release.TrySetResult();
			}
			var result = await delivery.WaitAsync(TimeSpan.FromSeconds(5));
			await Assert.That(result?.HadErrors).IsEqualTo(outcome != "success");
			await Assert.That(sessions.GetCapturing(player.Handle)?.Id)
				.IsEqualTo(outcome == "success" ? callbackReplacement : external);
		}
		finally
		{
			release.TrySetResult();
			if (delivery is not null) await delivery.WaitAsync(TimeSpan.FromSeconds(5));
			await connections.Disconnect(player.Handle);
		}
	}
}
