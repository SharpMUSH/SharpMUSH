using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Configuration.Options;
using Mediator;
using NSubstitute;
using SharpMUSH.Library.ParserInterfaces;
using MarkupString;

namespace SharpMUSH.Tests.Services;

public partial class PrivateListenerTests
{
	private async Task<DBRef> ListenThing(string name = "ParentCase") => await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, name);
	private async Task<ListenMatch[]> InheritedMatches(DBRef child, DBRef speaker, uint depth = 10, DBRef? ancestor = null, bool enabled = true)
	{
		var baseline = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		var options = new FixedOptions(baseline with
		{
			Limit = baseline.Limit with { MaxParents = depth },
			Database = baseline.Database with { AncestorThing = ancestor is { } reference ? (uint)reference.Number : null }
		});
		return await new ListenPatternMatcher(Mediator, options).MatchListenPatternsAsync(await Node(child), "hello world", await Node(speaker), enabled);
	}

	[Test]
	[Arguments("Use")]
	[Arguments("Listen")]
	public async Task InheritedActionUsesChildLocksIdentityAndStyledCapturedSnapshot(string lockName)
	{
		var actor = await Player();
		var child = await ListenThing();
		var parent = await ListenThing();
		await Admin($"@parent {child}={parent}");
		await Admin($"@set {child}=MONITOR LISTEN_PARENT");
		await Admin($"&TREE`ACTION {parent}=^hello *:&CAPTURE me=%0|%!|%#|%@");
		await Admin($"@lock/{lockName} {child}=#FALSE");
		var pipeline = await Build(actor, actor.DbRef);
		var styled = (await pipeline.Parser.FunctionParse(MarkupText.Plain("ansi(r,hello world)")))!.Message!;
		await pipeline.Notify.Notify(child, styled, await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue).IsEmpty();
		await Admin($"@lock/{lockName} {child}=#TRUE");
		await Admin($"@lock/{lockName} {parent}=#FALSE");
		await pipeline.Notify.Notify(child, styled, await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue).HasSingleItem();
		var queued = pipeline.Queue[0];
		await Assert.That(queued.State.CurrentEvaluation).IsEqualTo(new DBAttribute(child, "TREE`ACTION"));
		await Assert.That(queued.State.Executor).IsEqualTo(child);
		await Assert.That(queued.State.Enactor).IsEqualTo(actor.DbRef);
		await Assert.That(queued.State.Caller).IsEqualTo(actor.DbRef);
		await Assert.That(queued.State.EnvironmentRegisters["0"].Message!.Render(MarkupFormat.Ansi)).Contains("\u001b[");
		await Admin($"&TREE`ACTION {parent}=changed");
		await pipeline.Parser.FromState(queued.State).CommandListParse(queued.Command);
		var captured = await Factory.Services.GetRequiredService<IAttributeService>().GetAttributeAsync(await Node(child), await Node(child),
			"CAPTURE", IAttributeService.AttributeMode.Read, false);
		await Assert.That(captured.Expect<SharpAttribute[]>().Last().Value.ToPlainText())
			.IsEqualTo($"world|#{child.Number}|#{actor.DbRef.Number}|#{actor.DbRef.Number}");
	}

	[Test]
	[Arguments("enabled", 1)]
	[Arguments("disabled", 0)]
	[Arguments("ordinary-parent", 1)]
	[Arguments("cycle", 1)]
	[Arguments("plain-shadow", 0)]
	[Arguments("private-local-shadow", 0)]
	[Arguments("no-command", 0)]
	[Arguments("private-ancestor", 0)]
	public async Task ConfiguredAncestorUsesSharedVisibilityAndVisitedState(string mode, int expected)
	{
		var actor = await Player();
		var child = await ListenThing();
		var ancestor = await ListenThing();
		await Admin($"&TREE`ACTION {ancestor}=^hello *:ancestor");
		if (mode == "ordinary-parent") await Admin($"@parent {child}={ancestor}");
		if (mode == "cycle") await Mediator.Send(new SetObjectParentCommand(await Node(ancestor), await Node(ancestor)));
		if (mode is "plain-shadow" or "private-local-shadow") await Admin($"&TREE`ACTION {child}=plain");
		if (mode == "private-local-shadow") await Admin($"@set {child}/TREE`ACTION=NO_INHERIT");
		if (mode == "no-command")
		{
			await Admin($"&TREE {child}=block");
			await Admin($"@set {child}/TREE=NO_COMMAND");
		}
		if (mode == "private-ancestor") await Admin($"@set {ancestor}/TREE=NO_INHERIT");
		await Assert.That((await InheritedMatches(child, actor.DbRef, ancestor: ancestor, enabled: mode != "disabled")).Length).IsEqualTo(expected);
	}

	[Test]
	public async Task AncestorParentEditsAndLinksRemainLiveAfterWarmSearch()
	{
		var actor = await Player();
		var child = await ListenThing();
		var ancestor = await ListenThing();
		var parent = await ListenThing();
		await Admin($"@parent {ancestor}={parent}");
		await Assert.That(await InheritedMatches(child, actor.DbRef, ancestor: ancestor)).IsEmpty();
		await Admin($"&ACTION {parent}=^hello *:parent");
		await Assert.That(await InheritedMatches(child, actor.DbRef, ancestor: ancestor)).HasSingleItem();
		await Admin($"@set {parent}/ACTION=NO_INHERIT");
		await Assert.That(await InheritedMatches(child, actor.DbRef, ancestor: ancestor)).IsEmpty();
		await Admin($"@set {parent}/ACTION=!NO_INHERIT");
		await Assert.That(await InheritedMatches(child, actor.DbRef, ancestor: ancestor)).HasSingleItem();
		await Mediator.Send(new UnsetObjectParentCommand(await Node(ancestor)));
		await Assert.That(await InheritedMatches(child, actor.DbRef, ancestor: ancestor)).IsEmpty();
	}

	[Test]
	public async Task ParentReadCancellationPropagates()
	{
		var child = await ListenThing();
		var parent = await ListenThing();
		await Admin($"@parent {child}={parent}");
		using var cancellation = new CancellationTokenSource();
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			var query = call.Arg<GetObjectNodeQuery>();
			if (query.DBRef == parent) cancellation.Cancel();
			return Mediator.Send(query, call.Arg<CancellationToken>());
		});
		mediator.Send(Arg.Any<GetListenAttributeSnapshotQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => Mediator.Send(call.Arg<GetListenAttributeSnapshotQuery>(), call.Arg<CancellationToken>()));
		await Assert.ThrowsAsync<OperationCanceledException>(async () =>
			await new ListenAttributeSearch().ReadPhaseAsync(mediator, child, 10, false, cancellation.Token));
	}

	[Test]
	public async Task FlagOnlyOnParentDoesNotEnableRouting()
	{
		var actor = await Player();
		var child = await ListenThing();
		var parent = await ListenThing();
		await Admin($"@parent {child}={parent}");
		await Admin($"@set {child}=MONITOR");
		await Admin($"@set {parent}=LISTEN_PARENT");
		await Admin($"&ACTION {parent}=^hello *:think inherited");
		var pipeline = await Build(actor, actor.DbRef);
		await pipeline.Notify.Notify(child, "hello world", await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue).IsEmpty();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task PrivateInheritedTreesDoNotMaskFartherDefinitions(bool noCommand)
	{
		var actor = await Player();
		var child = await ListenThing();
		var parent = await ListenThing();
		var grandparent = await ListenThing();
		await Admin($"@parent {child}={parent}");
		await Admin($"@parent {parent}={grandparent}");
		await Admin($"&TREE {parent}=private root");
		await Admin($"&TREE`ACTION {parent}=^hello *:think private");
		await Admin($"@set {parent}/TREE=NO_INHERIT");
		if (noCommand) await Admin($"@set {parent}/TREE=NO_COMMAND");
		await Admin($"&TREE`ACTION {grandparent}=^hello *:think visible");
		var matches = await InheritedMatches(child, actor.DbRef);
		await Assert.That(matches).HasSingleItem();
		await Assert.That(matches[0].Attribute.Value.ToPlainText()).IsEqualTo("^hello *:think visible");
		if (!noCommand)
		{
			var local = await InheritedMatches(parent, actor.DbRef, enabled: false);
			await Assert.That(local).HasSingleItem();
			await Assert.That(local[0].Attribute.Value.ToPlainText()).IsEqualTo("^hello *:think private");
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task NoCommandTreeMasksIgnoreOrderAndPlainNearerRoot(bool reverse)
	{
		var child = await ListenThing();
		var parent = await ListenThing();
		await Admin($"&TREE {child}=plain");
		await Admin($"&TREE {parent}=block");
		await Admin($"&TREE`ACTION {parent}=^hello *:blocked");
		await Admin($"&TREEHOUSE {parent}=^hello *:visible");
		await Admin($"@set {parent}/TREE=NO_COMMAND");
		var search = new ListenAttributeSearch();
		_ = search.Visible(await (await Node(child)).Object().AllAttributes.Value.ToArrayAsync(), false).ToArray();
		var attributes = await (await Node(parent)).Object().AllAttributes.Value.ToArrayAsync();
		if (reverse) Array.Reverse(attributes);
		attributes = attributes.Select(attribute => attribute with { LongName = attribute.LongName.ToLowerInvariant() }).ToArray();
		var patterns = ListenAttributeSearch.Compile(search.Visible(attributes, true));
		await Assert.That(patterns).HasSingleItem();
		await Assert.That(patterns[0].Attribute.LongName).IsEqualTo("treehouse");
	}

	[Test]
	[Arguments(0, 0)]
	[Arguments(1, 1)]
	[Arguments(2, 2)]
	public async Task ParentDepthUsesConfiguredBound(int limit, int expected)
	{
		var actor = await Player();
		var child = await ListenThing();
		var parent = await ListenThing();
		var grandparent = await ListenThing();
		await Admin($"@parent {child}={parent}");
		await Admin($"@parent {parent}={grandparent}");
		await Admin($"&PARENT_ACTION {parent}=^hello *:parent");
		await Admin($"&GRAND_ACTION {grandparent}=^hello *:grandparent");
		await Assert.That((await InheritedMatches(child, actor.DbRef, (uint)limit)).Length).IsEqualTo(expected);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ParentCyclesVisitEachObjectOnce(bool self)
	{
		var actor = await Player();
		var child = await ListenThing();
		var parent = self ? child : await ListenThing();
		await Mediator.Send(new SetObjectParentCommand(await Node(child), await Node(parent)));
		if (!self) await Mediator.Send(new SetObjectParentCommand(await Node(parent), await Node(child)));
		await Admin($"&ACTION {child}=^hello *:child");
		await Assert.That(await InheritedMatches(child, actor.DbRef)).HasSingleItem();
	}

	[Test]
	public async Task WarmSnapshotsObserveParentAttributeAndParentLinkMutations()
	{
		var actor = await Player();
		var child = await ListenThing();
		var first = await ListenThing();
		var second = await ListenThing();
		await Admin($"@parent {child}={first}");
		await Assert.That(await InheritedMatches(child, actor.DbRef)).IsEmpty();
		await Admin($"&ACTION {first}=^hello *:first");
		await Assert.That(await InheritedMatches(child, actor.DbRef)).HasSingleItem();
		await Admin($"@set {first}/ACTION=NO_COMMAND");
		await Assert.That(await InheritedMatches(child, actor.DbRef)).IsEmpty();
		await Admin($"@set {first}/ACTION=!NO_COMMAND");
		await Assert.That(await InheritedMatches(child, actor.DbRef)).HasSingleItem();
		await Admin($"&ACTION {first}");
		await Assert.That(await InheritedMatches(child, actor.DbRef)).IsEmpty();
		await Admin($"&ACTION {second}=^hello *:second");
		await Admin($"@parent {child}={second}");
		await Assert.That(await InheritedMatches(child, actor.DbRef)).HasSingleItem();
	}
	[Test]
	[Arguments("thing", false)]
	[Arguments("room", false)]
	[Arguments("player", false)]
	[Arguments("thing", true)]
	[Arguments("room", true)]
	[Arguments("player", true)]
	public async Task ListenParentOnOriginalListenerEnablesUnflaggedParents(string type, bool prompt)
	{
		var actor = await Player();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "ListenParent");
		var child = type == "player" ? (await Player()).DbRef
			: type == "room" ? SharpMUSH.Library.Models.DBRef.Parse((await Admin($"@dig {Guid.NewGuid():N}")).Message!.ToPlainText().Trim())
			: await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "ListenChild");
		await Admin($"@parent {child}={parent}");
		await Admin($"@set {child}=MONITOR LISTEN_PARENT");
		await Assert.That(await (await Node(child)).HasFlag("LISTEN_PARENT")).IsTrue();
		await Admin($"&INHERITED {parent}=^hello *:&CAPTURE me=%0");
		var pipeline = await Build(actor, actor.DbRef);
		if (prompt) await pipeline.Notify.Prompt(child, "hello world", await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		else await pipeline.Notify.Notify(child, "hello world", await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue.Count).IsEqualTo(1);
		await Assert.That(pipeline.Queue[0].State.Executor).IsEqualTo(child);
		await Assert.That(pipeline.Queue[0].State.CurrentEvaluation!.Name).IsEqualTo("INHERITED");
		await Assert.That(pipeline.Queue[0].State.EnvironmentRegisters["0"].Message!.ToPlainText()).IsEqualTo("world");
	}

	[Test]
	[Arguments("plain")]
	[Arguments("empty")]
	[Arguments("nonmatching")]
	[Arguments("invalid")]
	[Arguments("opposite")]
	public async Task LocalNonMatchingDefinitionsStillShadowInheritedPatterns(string mode)
	{
		var actor = await Player();
		var child = await Player();
		var parent = await Player();
		await Admin($"@parent {child.DbRef}={parent.DbRef}");
		await Admin($"@set {child.DbRef}=LISTEN_PARENT");
		await Admin($"@set {parent.DbRef}=LISTEN_PARENT");
		await Admin($"&ACTION {parent.DbRef}=^hello *:parent");
		var value = mode switch { "plain" => "plain", "empty" => "", "nonmatching" => "^different:child", "invalid" => "^[:child", _ => "^hello *:child" };
		await Factory.Services.GetRequiredService<IAttributeService>().SetAttributeAsync(await Node(child.DbRef), await Node(child.DbRef), "ACTION", MarkupText.Plain(value));
		if (mode == "invalid") await Admin($"@set {child.DbRef}/ACTION=REGEXP");
		if (mode == "opposite") await Admin($"@set {child.DbRef}/ACTION=AMHEAR");
		var matches = await Factory.Services.GetRequiredService<IListenPatternMatcher>().MatchListenPatternsAsync(
			await Node(child.DbRef), "hello world", await Node(actor.DbRef), checkParents: true);
		await Assert.That(matches).IsEmpty();
	}

	[Test]
	public async Task ListenHearActionsAreAdmittedBeforeMonitorActions()
	{
		var actor = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "ListenOrder");
		await Admin($"@set {listener}=MONITOR");
		await Admin($"@listen {listener}=hello *");
		await Admin($"@ahear {listener}=think first");
		await Admin($"@aahear {listener}=think second");
		await Admin($"&MONITOR_ACTION {listener}=^hello *:think third");
		var pipeline = await Build(actor, actor.DbRef);
		await pipeline.Notify.Notify(listener, "hello world", await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue.Select(item => item.Command.ToPlainText()).ToArray()).IsEquivalentTo(["think first", "think second", "think third"]);
		await Assert.That(pipeline.Queue[0].Command.ToPlainText()).IsEqualTo("think first");
		await Assert.That(pipeline.Queue[1].Command.ToPlainText()).IsEqualTo("think second");
	}
}
