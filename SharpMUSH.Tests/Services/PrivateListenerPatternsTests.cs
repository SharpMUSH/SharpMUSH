using MarkupString;
using MarkupString.Ansi;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public partial class PrivateListenerTests
{
	[Test]
	[Arguments("", false, true)]
	[Arguments("", true, false)]
	[Arguments("AAHEAR", false, true)]
	[Arguments("AAHEAR", true, true)]
	[Arguments("AMHEAR", false, false)]
	[Arguments("AMHEAR", true, true)]
	public async Task MonitorActionFlagsSelectSelfAndOtherSpeakers(string flag, bool self, bool admitted)
	{
		var actor = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "FlaggedMonitor");
		await Admin($"@set {listener}=MONITOR");
		await Admin($"&PATTERN {listener}=^*:&HEARD me=%0");
		if (flag.Length > 0) await Admin($"@set {listener}/PATTERN={flag}");
		var pipeline = await Build(actor, actor.DbRef);
		await pipeline.Notify.Notify(listener, "hello", await Node(self ? listener : actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue.Count).IsEqualTo(admitted ? 1 : 0);
	}

	[Test]
	[Arguments("HALT")]
	[Arguments("NO_COMMAND")]
	public async Task MonitorDoesNotAdmitHaltedOrDisabledPatterns(string flag)
	{
		var actor = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "DisabledMonitor");
		await Admin($"@set {listener}=MONITOR");
		await Admin($"&PATTERN {listener}=^*:&HEARD me=%0");
		await Admin(flag == "HALT" ? $"@set {listener}=HALT" : $"@set {listener}/PATTERN=NO_COMMAND");
		var pipeline = await Build(actor, actor.DbRef);
		await pipeline.Notify.Notify(listener, "hello", await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue.Count).IsEqualTo(0);
	}

	[Test]
	[Arguments("^")]
	[Arguments("$")]
	public async Task ListenActionStripsItsOwnCommandPatternPrefix(string sigil)
	{
		var actor = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "PrefixedAction");
		await Admin($"@listen {listener}=*");
		await Admin($"@ahear {listener}={sigil}escaped\\:pattern:&HEARD me=%0");
		var pipeline = await Build(actor, actor.DbRef);
		await pipeline.Notify.Notify(listener, "hello", await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue.Single().Command.ToPlainText()).IsEqualTo("&HEARD me=%0");
	}

	[Test]
	[Arguments(false, false, false)]
	[Arguments(false, false, true)]
	[Arguments(false, true, false)]
	[Arguments(false, true, true)]
	[Arguments(true, false, false)]
	[Arguments(true, false, true)]
	[Arguments(true, true, false)]
	[Arguments(true, true, true)]
	public async Task ListenAndMonitorPreserveDialectCaseAndStyledCaptures(bool monitor, bool regexp, bool caseSensitive)
	{
		var actor = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "ListenDialect");
		var pattern = regexp ? "Hello (.*) and (.*)" : "Hello * and *";
		var attribute = monitor ? "PATTERN" : "LISTEN";
		if (monitor)
		{
			await Admin($"@set {listener}=MONITOR");
			await Admin($"&PATTERN {listener}=^{pattern}:&RESULT me=%0|%1|%2");
		}
		else
		{
			await Admin($"@listen {listener}={pattern}");
			await Admin($"@ahear {listener}=&RESULT me=%0|%1|%2");
		}
		if (regexp) await Admin($"@set {listener}/{attribute}=REGEXP");
		if (caseSensitive) await Admin($"@set {listener}/{attribute}=CASE");
		var pipeline = await Build(actor, actor.DbRef);
		await pipeline.Notify.Notify(listener, "hello one and two", await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue.Count).IsEqualTo(caseSensitive ? 0 : 1);
		pipeline.Queue.Clear();
		var styled = MarkupText.Concat([MarkupText.Plain("Hello "), MarkupText.Wrap(AnsiMarkup.Create(foreground: System.Drawing.Color.Red.ToAnsiColor()), MarkupText.Plain("one")),
			MarkupText.Plain(" and two")]);
		await pipeline.Notify.Notify(listener, styled, await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue.Count).IsEqualTo(1);
		var queued = pipeline.Queue.Single();
		await Assert.That(queued.Command.ToPlainText()).StartsWith("&RESULT me=");
		await Assert.That(queued.State.EnvironmentRegisters[regexp ? "1" : "0"].Message!.Render(MarkupFormat.Ansi)).Contains("\u001b[");
		await Assert.That(queued.State.EnvironmentRegisters["0"].Message!.ToPlainText()).IsEqualTo(regexp ? "Hello one and two" : "one");
		await pipeline.Parser.FromState(queued.State).CommandListParse(queued.Command);
		var result = (await Factory.Services.GetRequiredService<IAttributeService>().GetAttributeAsync(await Node(listener), await Node(listener),
			"RESULT", IAttributeService.AttributeMode.Read, false)).Expect<SharpAttribute[]>().Last().Value;
		await Assert.That(result.ToPlainText()).IsEqualTo(regexp ? "Hello one and two|one|two" : "one|two|");
		await Assert.That(result.Render(MarkupFormat.Ansi)).Contains("\u001b[");
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task AahearAddsToOnlyTheCorrectSelfOrOtherAction(bool self, bool amhearExists)
	{
		var actor = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "SelfListen");
		await Admin($"@listen {listener}=*");
		await Admin($"@ahear {listener}=&OTHER me=1");
		await Admin($"@aahear {listener}=&ANY me=1");
		if (amhearExists) await Admin($"@amhear {listener}=&SELF me=1");
		var pipeline = await Build(actor, actor.DbRef);
		await pipeline.Notify.Notify(listener, "hello", await Node(self ? listener : actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		var commands = pipeline.Queue.Select(item => item.Command.ToPlainText()).ToArray();
		await Assert.That(commands).Contains("&ANY me=1");
		await Assert.That(commands.Contains("&OTHER me=1")).IsEqualTo(!self);
		await Assert.That(commands.Contains("&SELF me=1")).IsEqualTo(self && amhearExists);
		await Assert.That(commands.Length).IsEqualTo(self && !amhearExists ? 1 : 2);
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task PrivatePlayerConfigurationSeparatesMonitorFromAhear(bool playerListen, bool playerAHear)
	{
		var actor = await Player();
		var listener = await Player();
		await Admin($"@listen {listener.DbRef}=*");
		await Admin($"@ahear {listener.DbRef}=&HEARD me=1");
		await Admin($"@aahear {listener.DbRef}=&ALSO me=1");
		await Admin($"&PATTERN {listener.DbRef}=^*:&MONITORED me=1");
		await Admin($"@set {listener.DbRef}=MONITOR");
		var pipeline = await Build(actor, listener.DbRef, playerListen, playerAHear);
		await pipeline.Notify.Notify(listener.DbRef, "hello", await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue.Count).IsEqualTo(playerListen ? playerAHear ? 3 : 1 : 0);
		pipeline.Queue.Clear();
		await pipeline.Notify.Notify(listener.DbRef, "hello", await Node(actor.DbRef), INotifyService.NotificationType.Say);
		await Assert.That(pipeline.Queue.Count).IsEqualTo(playerAHear ? 3 : 1);
	}

	[Test]
	[Arguments("hello\\:*", "hello:one", "one", false)]
	[Arguments("hello\\\\", "hello\\", "hello\\", true)]
	public async Task MonitorSlicesOnlyTheRealPatternSeparator(string pattern, string input, string capture, bool regexp)
	{
		var actor = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "SeparatorListen");
		await Admin($"@set {listener}=MONITOR");
		await Admin($"&PATTERN {listener}=^{pattern}:&RESULT me=%0");
		if (regexp) await Admin($"@set {listener}/PATTERN=REGEXP");
		var pipeline = await Build(actor, actor.DbRef);
		await pipeline.Notify.Notify(listener, input, await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue.Count).IsEqualTo(1);
		await Assert.That(pipeline.Queue[0].Command.ToPlainText()).IsEqualTo("&RESULT me=%0");
		await Assert.That(pipeline.Queue[0].State.EnvironmentRegisters.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "").IsEqualTo(capture);
	}

	[Test]
	public async Task AdministrativeAndEmptyPrivateOutputStayInertAndListenLockDeniesActions()
	{
		var actor = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "QuietListen");
		await Admin($"@listen {listener}=*");
		await Admin($"@ahear {listener}=&HEARD me=1");
		var pipeline = await Build(actor, actor.DbRef);
		await pipeline.Notify.Notify(listener, "admin", await Node(actor.DbRef));
		await pipeline.Parser.CommandParse(actor.Handle, Connections, MarkupText.Plain($"@pemit {listener}="));
		await Assert.That(pipeline.Queue.Count).IsEqualTo(0);
		await Admin($"@lock/listen {listener}=#FALSE");
		await pipeline.Notify.Notify(listener, "locked", await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue.Count).IsEqualTo(0);
	}
}
