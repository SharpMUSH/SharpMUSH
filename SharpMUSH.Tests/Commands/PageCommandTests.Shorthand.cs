using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public partial class PageCommandTests
{
	[Test]
	[Arguments("page Following up", "Following up")]
	[Arguments("page =Following up", "Following up")]
	[Arguments("page/noeval [add(1,2)]", "[add(1,2)]")]
	public async ValueTask ShorthandReusesLastOutgoingTargets(string command, string message)
	{
		var sender = await CreatePlayerAsync("PageShortSender");
		var target = await CreatePlayerAsync("PageShortTarget");
		try
		{
			await PageAsync(sender, target.Name, "First");
			var start = Notifications.CountFor(target.DbRef);
			await CommandAsync(sender, command);
			await Assert.That(Notifications.For(target.DbRef).Skip(start)).Contains($"{sender.Name} pages: {message}");
		}
		finally { await DisconnectPages(sender, target); }
	}

	[Test]
	[Arguments("page Missing context", "You haven't paged anyone since connecting.")]
	[Arguments("page =Missing context", "You haven't paged anyone since connecting.")]
	[Arguments("page/reply Missing context", "No one has paged you yet.")]
	public async ValueTask ShorthandWithoutItsOwnContextSendsNothing(string command, string expected)
	{
		var sender = await CreatePlayerAsync("PageShortEmpty");
		try
		{
			var start = Notifications.CountFor(sender.DbRef);
			await CommandAsync(sender, command);
			await AssertLinesAsync(sender, start, expected);
			await Assert.That(await Context(sender, "LASTPAGED")).IsEqualTo(string.Empty);
		}
		finally { await DisconnectPages(sender); }
	}

	[Test]
	public async ValueTask ReplyAndOutgoingShorthandUseDistinctContexts()
	{
		var sender = await CreatePlayerAsync("PageSplitSender");
		var outgoing = await CreatePlayerAsync("PageSplitOutgoing");
		var incoming = await CreatePlayerAsync("PageSplitIncoming");
		try
		{
			await PageAsync(sender, outgoing.Name, "Outgoing first");
			await PageAsync(incoming, sender.Name, "Incoming first");
			var outStart = Notifications.CountFor(outgoing.DbRef);
			var inStart = Notifications.CountFor(incoming.DbRef);
			await CommandAsync(sender, "page Continuing outgoing");
			await CommandAsync(sender, "page/reply Answer=with an equals sign");
			await Assert.That(Notifications.For(outgoing.DbRef).Skip(outStart)).Contains($"{sender.Name} pages: Continuing outgoing");
			await Assert.That(Notifications.For(outgoing.DbRef).Skip(outStart).Any(line => line.Contains("Answer="))).IsFalse();
			await Assert.That(Notifications.For(incoming.DbRef).Skip(inStart)).Contains($"{sender.Name} pages: Answer=with an equals sign");
			await Assert.That(await Context(sender, "LASTPAGED")).IsEqualTo(incoming.DbRef.ToString());
			await Assert.That(await Context(sender, "LASTPAGER")).IsEqualTo(incoming.DbRef.ToString());
		}
		finally { await DisconnectPages(sender, outgoing, incoming); }
	}

	[Test]
	public async ValueTask GroupPageReusesItsOutgoingGroupButReplyOnlyTargetsItsSender()
	{
		var sender = await CreatePlayerAsync("PageGroupSender");
		var first = await CreatePlayerAsync("PageGroupFirst");
		var second = await CreatePlayerAsync("PageGroupSecond");
		try
		{
			await PageAsync(sender, $"{second.Name} {first.Name}", "Group");
			await Assert.That(await Context(sender, "LASTPAGED")).IsEqualTo($"{second.DbRef} {first.DbRef}");
			await CommandAsync(sender, "page Continued group");
			var senderStart = Notifications.CountFor(sender.DbRef);
			var secondStart = Notifications.CountFor(second.DbRef);
			await CommandAsync(first, "page/reply Only the sender");
			await Assert.That(Notifications.For(sender.DbRef).Skip(senderStart)).Contains($"{first.Name} pages: Only the sender");
			await Assert.That(Notifications.For(second.DbRef).Skip(secondStart).Any(line => line.Contains("Only the sender"))).IsFalse();
		}
		finally { await DisconnectPages(sender, first, second); }
	}

	[Test]
	public async ValueTask FailedPagesDoNotReplaceOutgoingOrIncomingContexts()
	{
		var sender = await CreatePlayerAsync("PageFailSender");
		var previous = await CreatePlayerAsync("PageFailPrevious");
		var blocked = await CreatePlayerAsync("PageFailBlocked");
		try
		{
			await PageAsync(sender, previous.Name, "Original outgoing");
			await PageAsync(previous, blocked.Name, "Original incoming");
			await GodCommandAsync($"@set {blocked.DbRef}=HAVEN");
			await PageAsync(sender, blocked.Name, "Refused");
			await Assert.That(await Context(sender, "LASTPAGED")).IsEqualTo(previous.DbRef.ToString());
			await Assert.That(await Context(blocked, "LASTPAGER")).IsEqualTo(previous.DbRef.ToString());
		}
		finally { await DisconnectPages(sender, previous, blocked); }
	}

	[Test]
	public async ValueTask PartialPageRemembersOnlyAdmittedRecipientsAndDoesNotSetBlockedIncomingState()
	{
		var sender = await CreatePlayerAsync("PagePartialContextSender");
		var admitted = await CreatePlayerAsync("PagePartialContextGood");
		var blocked = await CreatePlayerAsync("PagePartialContextBad");
		try
		{
			await GodCommandAsync($"@set {blocked.DbRef}=HAVEN");
			await PageAsync(sender, $"{blocked.Name} {admitted.Name}", "Partially delivered");
			await Assert.That(await Context(sender, "LASTPAGED")).IsEqualTo(admitted.DbRef.ToString());
			await Assert.That(await Context(admitted, "LASTPAGER")).IsEqualTo(sender.DbRef.ToString());
			await Assert.That(await Context(blocked, "LASTPAGER")).IsEqualTo(string.Empty);
		}
		finally { await DisconnectPages(sender, admitted, blocked); }
	}

	[Test]
	public async ValueTask ReplyKeepsNormalHavenAndOverrideAuthorityChecks()
	{
		var sender = await CreatePlayerAsync("PageReplyGateSender");
		var incoming = await CreatePlayerAsync("PageReplyGateIncoming");
		try
		{
			await PageAsync(incoming, sender.Name, "Establish incoming");
			await GodCommandAsync($"@set {incoming.DbRef}=HAVEN");
			var start = Notifications.CountFor(incoming.DbRef);
			await CommandAsync(sender, "page/reply Refused reply");
			await CommandAsync(sender, "page/reply/override Still refused reply");
			await Assert.That(Notifications.For(incoming.DbRef).Skip(start).Any(line => line.Contains("refused reply", StringComparison.OrdinalIgnoreCase))).IsFalse();
			await GodCommandAsync($"@power {sender.DbRef}=Pemit_All");
			await CommandAsync(sender, "page/reply/override Authorized reply");
			await Assert.That(Notifications.For(incoming.DbRef).Skip(start)).Contains($"{sender.Name} pages: Authorized reply");
		}
		finally { await DisconnectPages(sender, incoming); }
	}

	[Test]
	public async ValueTask OrdinaryOverrideCannotBypassHavenWithoutPemitAll()
	{
		var sender = await CreatePlayerAsync("PageOverrideSender");
		var target = await CreatePlayerAsync("PageOverrideTarget");
		try
		{
			await GodCommandAsync($"@set {target.DbRef}=HAVEN");
			var start = Notifications.CountFor(target.DbRef);
			await CommandAsync(sender, $"page/override {target.Name}=Unauthorized bypass");
			await Assert.That(Notifications.For(target.DbRef).Skip(start).Any(line => line.Contains("Unauthorized bypass"))).IsFalse();
		}
		finally { await DisconnectPages(sender, target); }
	}

	[Test]
	public async ValueTask ReplyUsesCurrentIdentityAfterRenameAndRejectsStaleOrMultipleStoredTargets()
	{
		var sender = await CreatePlayerAsync("PageReplyIdSender");
		var incoming = await CreatePlayerAsync("PageReplyIdIncoming");
		try
		{
			await PageAsync(incoming, sender.Name, "Before rename");
			await GodCommandAsync($"@name {incoming.DbRef}={TestIsolationHelpers.GenerateUniqueName("PageRenamed")}");
			var start = Notifications.CountFor(incoming.DbRef);
			await CommandAsync(sender, "page/reply After rename");
			await Assert.That(Notifications.For(incoming.DbRef).Skip(start)).Contains($"{sender.Name} pages: After rename");
			var stale = new DBRef(incoming.DbRef.Number, incoming.DbRef.CreationMilliseconds!.Value + 1);
			foreach (var context in new[] { stale.ToString(), $"{incoming.DbRef} {sender.DbRef}", $"#{incoming.DbRef.Number}" })
			{
				await GodCommandAsync($"&LASTPAGER {sender.DbRef}={context}");
				start = Notifications.CountFor(incoming.DbRef);
				await CommandAsync(sender, "page/reply Must not retarget");
				await Assert.That(Notifications.For(incoming.DbRef).Skip(start).Any(line => line.Contains("Must not retarget"))).IsFalse();
			}
		}
		finally { await DisconnectPages(sender, incoming); }
	}

	[Test]
	public async ValueTask ContextBelongsToCharacterAcrossConnectionsReconnectAndRebinding()
	{
		var sender = await CreatePlayerAsync("PageBindingSender");
		var target = await CreatePlayerAsync("PageBindingTarget");
		var other = await CreatePlayerAsync("PageBindingOther");
		long? second = null;
		try
		{
			await PageAsync(target, sender.Name, "Incoming context");
			second = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, sender.DbRef);
			await CommandAsync(sender with { Handle = second.Value }, "page/reply Shared character context");
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(second.Value);
			second = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, sender.DbRef);
			var start = Notifications.CountFor(target.DbRef);
			await CommandAsync(sender with { Handle = second.Value }, "page Outgoing after reconnect");
			await Assert.That(Notifications.For(target.DbRef).Skip(start)).Contains($"{sender.Name} pages: Outgoing after reconnect");
			await ConnectionService.Unbind(second.Value);
			await ConnectionService.Bind(second.Value, other.DbRef);
			var otherStart = Notifications.CountFor(other.DbRef);
			await CommandAsync(other with { Handle = second.Value }, "page/reply No leaked context");
			await AssertLinesAsync(other, otherStart, "No one has paged you yet.");
		}
		finally
		{
			if (second is { } handle) await ConnectionService.Disconnect(handle);
			await DisconnectPages(sender, target, other);
		}
	}

	[Test]
	public async ValueTask ReplyNoEvalPreservesLiteralTextAndIncomingContextIsProtected()
	{
		var sender = await CreatePlayerAsync("PageReplyLiteralSender");
		var incoming = await CreatePlayerAsync("PageReplyLiteralIncoming");
		try
		{
			await PageAsync(incoming, sender.Name, "Incoming");
			var start = Notifications.CountFor(incoming.DbRef);
			await CommandAsync(sender, "page/reply/noeval [add(1,2)]=literal");
			await Assert.That(Notifications.For(incoming.DbRef).Skip(start)).Contains($"{sender.Name} pages: [add(1,2)]=literal");
			await CommandAsync(sender, $"&LASTPAGER me={sender.DbRef}");
			await Assert.That(await Context(sender, "LASTPAGER")).IsEqualTo(incoming.DbRef.ToString());
		}
		finally { await DisconnectPages(sender, incoming); }
	}

	[Test]
	[NotInParallel]
	[Arguments("")]
	[Arguments("mortal_dark")]
	[Arguments("internal")]
	public async ValueTask LegacyUnprotectedIncomingContextIsRejectedAndGenuineDeliveryProtectsItAtomically(string legacyFlag)
	{
		var sender = await CreatePlayerAsync("PageLegacySender");
		var incoming = await CreatePlayerAsync("PageLegacyIncoming");
		var forged = await CreatePlayerAsync("PageLegacyForged");
		try
		{
			// Model a custom pre-upgrade LASTPAGER node: migration seeds definitions, not existing-node flags.
			await GodCommandAsync($"&LASTPAGER {sender.DbRef}={forged.DbRef}");
			await GodCommandAsync($"@set {sender.DbRef}/LASTPAGER=!wizard !locked !no_clone !prefixmatch");
			if (legacyFlag.Length > 0) await GodCommandAsync($"@set {sender.DbRef}/LASTPAGER={legacyFlag}");
			await WebAppFactoryArg.Services.GetRequiredService<LightningDatabase>().Migrate();
			var forgedStart = Notifications.CountFor(forged.DbRef);
			await CommandAsync(sender, "page/reply Forged legacy reply");
			await Assert.That(Notifications.For(forged.DbRef).Skip(forgedStart).Any(line => line.Contains("Forged legacy reply"))).IsFalse();
			await Assert.That((await StoredIncomingContext(sender)).Value.ToPlainText()).IsEqualTo(forged.DbRef.ToString());
			await PageAsync(incoming, sender.Name, "Genuine incoming");
			var established = await StoredIncomingContext(sender);
			var expectedFlags = new[] { "wizard", "locked", "no_clone", "prefixmatch" }.Concat(legacyFlag.Length > 0 ? [legacyFlag] : []);
			await Assert.That(established.Flags.Select(flag => flag.Name.ToLowerInvariant())).IsEquivalentTo(expectedFlags);
			await Assert.That((await established.Owner.WithCancellation(CancellationToken.None))?.Object.Key).IsEqualTo((int?)1);
			await CommandAsync(sender, $"&LASTPAGER me={forged.DbRef}");
			await Assert.That((await StoredIncomingContext(sender)).Value.ToPlainText()).IsEqualTo(incoming.DbRef.ToString());
			var incomingStart = Notifications.CountFor(incoming.DbRef);
			await CommandAsync(sender, "page/reply Genuine protected reply");
			await Assert.That(Notifications.For(incoming.DbRef).Skip(incomingStart)).Contains($"{sender.Name} pages: Genuine protected reply");
			await Assert.That(Notifications.For(forged.DbRef).Skip(forgedStart).Any(line => line.Contains("Genuine protected reply"))).IsFalse();
		}
		finally { await DisconnectPages(sender, incoming, forged); }
	}

	[Test]
	[Arguments("list")]
	[Arguments("recall")]
	[Arguments("conversations")]
	[Arguments("timestamps")]
	[Arguments("port")]
	public async ValueTask ReplyRejectsIncompatibleSwitches(string incompatible)
	{
		var sender = await CreatePlayerAsync("PageReplySwitches");
		try
		{
			var start = Notifications.CountFor(sender.DbRef);
			await CommandAsync(sender, $"page/reply/{incompatible} text");
			await AssertLinesAsync(sender, start, "The /reply switch cannot be combined with /list, /recall, /conversations, /timestamps, or /port.");
		}
		finally { await DisconnectPages(sender); }
	}

	private async Task<SharpAttribute> StoredIncomingContext(PagePlayer player)
		=> await Mediator.CreateStream(new GetAttributeQuery(player.DbRef, ["LASTPAGER"])).SingleAsync();

	private async Task<string> Context(PagePlayer player, string name)
		=> await AttributeService.GetAttributeAsync(player.Object, player.Object, name, IAttributeService.AttributeMode.Read, false)
			is SharpAttribute[] attributes ? attributes.Last().Value.ToPlainText() : string.Empty;

	private async Task DisconnectPages(params PagePlayer[] players)
	{
		foreach (var player in players) await ConnectionService.Disconnect(player.Handle);
	}
}
