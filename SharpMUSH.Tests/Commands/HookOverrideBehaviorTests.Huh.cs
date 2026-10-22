using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public partial class HookOverrideBehaviorTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DefaultHuhReportsFailureWithoutStoppingTheListOrChangingOutput(bool unknownLast)
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HuhList");
		var token = TestIsolationHelpers.GenerateUniqueName("unhandled");
		try
		{
			var before = WebAppFactoryArg.Notifications.CountFor(WebAppFactoryArg.ExecutorDBRef);
			var commands = unknownLast ? $"&RESULT {obj}=yes; {token}" : $"{token}; &RESULT {obj}=yes";
			var result = await ObserveHuhList(commands + $"; &OUTPUT {obj}=%>");
			await Assert.That(result.HadErrors).IsTrue();
			await Assert.That(await ReadAttributeAsync(obj, "RESULT")).IsEqualTo("yes");
			// The immediately preceding command differs by ordering; probe HUH's %> directly as well.
			await ObserveHuhList($"{token}; &OUTPUT {obj}=%>");
			await Assert.That(await ReadAttributeAsync(obj, "OUTPUT")).IsEqualTo(ErrorMessages.Returns.Huh);
			var messages = WebAppFactoryArg.Notifications.For(WebAppFactoryArg.ExecutorDBRef).Skip(before);
			await Assert.That(messages.Count(message => message == ErrorMessages.Notifications.HuhTypeHelp)).IsEqualTo(2);
		}
		finally { await RemoveHuhFixture(obj); }
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	public async Task HuhOverrideKeepsItsOwnMatchAndFailureVerdict(int mode)
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HuhOverride");
		var holder = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HuhHolder");
		var token = TestIsolationHelpers.GenerateUniqueName("handled");
		try
		{
			// Keep OVR outside ordinary nearby/inventory command discovery. Only the explicit hook may match it.
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/inside/silent {obj}={holder}"));
			var location = await WebAppFactoryArg.Services.GetRequiredService<INavigationStore>().GetLocationRefAsync(obj);
			await Assert.That(location is DBRef actual && actual.Number == holder.Number).IsTrue();
			var pattern = mode == 2 ? token + "_other" : token;
			var body = $"&RESULT {obj}=handled" + (mode == 3 ? $"; {token}_nested_unhandled" : "");
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&OVR {obj}=${pattern}:{body}"));
			// The stored OVR must not run through ordinary $-command discovery before registration.
			var controlBefore = WebAppFactoryArg.Notifications.CountFor(WebAppFactoryArg.ExecutorDBRef);
			await ObserveHuhList(token);
			await DrainQueue();
			await Assert.That(await ReadAttributeAsync(obj, "RESULT")).IsEqualTo("");
			await Assert.That(WebAppFactoryArg.Notifications.For(WebAppFactoryArg.ExecutorDBRef).Skip(controlBefore)
				.Count(message => message == ErrorMessages.Notifications.HuhTypeHelp)).IsEqualTo(1);
			await Assert.That(await HookService.SetHookAsync("HUH_COMMAND", "OVERRIDE", obj, "OVR", inline: mode != 0)).IsTrue();
			var recipient = mode == 3 ? obj : WebAppFactoryArg.ExecutorDBRef;
			var before = WebAppFactoryArg.Notifications.CountFor(recipient);
			var result = await ObserveHuhList(token);
			await DrainQueue();
			await Assert.That(result.HadErrors).IsEqualTo(mode is 2 or 3);
			await Assert.That(await ReadAttributeAsync(obj, "RESULT")).IsEqualTo(mode == 2 ? "" : "handled");
			var messages = WebAppFactoryArg.Notifications.For(recipient).Skip(before);
			await Assert.That(messages.Count(message => message == ErrorMessages.Notifications.HuhTypeHelp)).IsEqualTo(mode is 2 or 3 ? 1 : 0);
		}
		finally
		{
			await HookService.ClearHookAsync("HUH_COMMAND", "OVERRIDE");
			await DrainQueue();
			await RemoveHuhFixture(obj);
			await RemoveHuhFixture(holder);
		}
	}

	[Test]
	public async Task OrdinaryDollarCommandStillHandlesInputWithoutHuhFailure()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HuhDollar");
		var token = TestIsolationHelpers.GenerateUniqueName("dollar");
		try
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&CMD {obj}=${token}:&RESULT {obj}=handled"));
			var before = WebAppFactoryArg.Notifications.CountFor(WebAppFactoryArg.ExecutorDBRef);
			var result = await ObserveHuhList(token);
			await DrainQueue();
			await Assert.That(result.HadErrors).IsFalse();
			await Assert.That(await ReadAttributeAsync(obj, "RESULT")).IsEqualTo("handled");
			await Assert.That(WebAppFactoryArg.Notifications.For(WebAppFactoryArg.ExecutorDBRef).Skip(before)
				.Any(message => message == ErrorMessages.Notifications.HuhTypeHelp)).IsFalse();
		}
		finally { await DrainQueue(); await RemoveHuhFixture(obj); }
	}

	private async Task<CallState> ObserveHuhList(string commands)
	{
		var completion = new TaskCompletionSource<CallState>(TaskCreationOptions.RunContinuationsAsynchronously);
		var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var admission = await WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>().AdmitSocketWork(async () =>
		{
			try
			{
				var result = await WebAppFactoryArg.CommandParserFor(new DBRef(1), 1).CommandListParse(MarkupText.Plain(commands))
					?? throw new InvalidOperationException("Expected a parser verdict");
				completion.TrySetResult(result);
				return result;
			}
			catch (Exception error) { completion.TrySetException(error); throw; }
		}, "huh-verdict-test", "test", () =>
		{
			completion.TrySetException(new InvalidOperationException("The verdict entry never ran"));
			released.TrySetResult();
		});
		if (!admission.Accepted) throw new InvalidOperationException($"Verdict entry refused: {admission.Reason}");
		try { return await completion.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
		finally { await released.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
	}

	private async Task RemoveHuhFixture(DBRef obj) =>
		await WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>().Send(new DeleteObjectCommand(obj));
}
