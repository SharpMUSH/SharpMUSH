using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The RhostMUSH and TinyMUX names SharpMUSH takes in for games that use them: vattrcnt(), size(), cloak(),
/// mailalias(), strlenraw(), totcmds(), listcommands() and listflags(); @rollback, lpage and mrpage.
/// </summary>
public class RhostCompatibilityShimTests : ServerTestBase
{
	[Test]
	public async Task VattrcntCountsOnlyAttributesSomebodyMade()
	{
		var thing = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("VattrCount")}"));
		await Cmd($"&NOTES {thing}=one");
		await Cmd($"&PLANS {thing}=two");
		await Cmd($"@describe {thing}=A thing.");

		await Assert.That(await Eval($"vattrcnt({thing})")).IsEqualTo("2");
		await Assert.That(await Eval($"vattrcnt({thing},1)")).IsEqualTo("2");
		await Assert.That(await Eval("vattrcnt(#-1)")).IsEqualTo("-1");
	}

	[Test]
	public async Task SizeIsZeroForWhatTheCallerControls()
	{
		await Assert.That(await Eval("size(me)")).IsEqualTo("0");
		await Assert.That(await Eval("size(#0,3)")).IsEqualTo("0");
		await Assert.That(await Eval($"size({TestIsolationHelpers.GenerateUniqueName("nobody")})")).IsEqualTo("#-1");
	}

	[Test]
	public async Task CloakIsHiddenFromThoseWhoCannotSeeHidden()
	{
		var hidden = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CloakHidden");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CloakViewer");
		try
		{
			await Assert.That(await Eval($"cloak({viewer.DbRef},{hidden.DbRef})")).IsEqualTo("0");

			ConnectionService.Update(hidden.Handle, "Hidden", "1");

			await Assert.That(await Eval($"cloak({viewer.DbRef},{hidden.DbRef})")).IsEqualTo("1");
			await Assert.That(await Eval($"cloak(#1,{hidden.DbRef})")).IsEqualTo("0");
			await Assert.That(await Eval($"cloak({viewer.DbRef},#-1)")).IsEqualTo("#-1");
		}
		finally
		{
			await ConnectionService.Disconnect(hidden.Handle);
			await ConnectionService.Disconnect(viewer.Handle);
		}
	}

	[Test]
	public async Task MailaliasListsMembers()
	{
		var member = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AliasMember");
		var alias = TestIsolationHelpers.GenerateUniqueName("shimalias");
		try
		{
			await Cmd($"@malias +{alias}=#1 {member.DbRef}");

			await Assert.That(await Eval($"mailalias({alias})")).IsEqualTo($"#1 #{member.DbRef.Number}");
			await Assert.That(await Eval($"mailalias(+{alias})")).IsEqualTo($"#1 #{member.DbRef.Number}");
			await Assert.That(await Eval($"mailalias({alias},1)")).IsEqualTo(string.Empty);
			await Assert.That(await Eval($"mailalias({alias}x)")).IsEqualTo(string.Empty);
		}
		finally
		{
			await ConnectionService.Disconnect(member.Handle);
		}
	}

	[Test]
	public async Task StrlenrawCountsTheColourCodes()
	{
		await Assert.That(await Eval("strlenraw(abc)")).IsEqualTo("3");
		await Assert.That(int.Parse(await Eval("strlenraw(ansi(r,abc))"))).IsGreaterThan(3);
	}

	[Test]
	public async Task TotcmdsAddsLiveSessionsToTheEndedOnes()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "TotCmds");
		var disconnected = false;
		try
		{
			await CmdAs(player.DbRef, player.Handle, "think one");
			await CmdAs(player.DbRef, player.Handle, "think two");
			await Cmd($"&TOTCMDS {player.DbRef}=100");

			var live = int.Parse(await Eval($"cmds({player.DbRef})"));
			await Assert.That(live).IsGreaterThan(0);
			await Assert.That(await Eval($"totcmds({player.DbRef})")).IsEqualTo((100 + live).ToString());

			// Hidden from a mortal who may not see hidden players.
			ConnectionService.Update(player.Handle, "Hidden", "1");
			var mortal = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "TotCmdsMortal");
			await Assert.That(await EvalAs(mortal, $"totcmds({player.DbRef})")).IsEqualTo("100");

			await ConnectionService.Disconnect(player.Handle);
			disconnected = true;
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(await Eval($"get({player.DbRef}/TOTCMDS)")).IsEqualTo((100 + live).ToString());
			await Assert.That(await Eval($"totcmds({player.DbRef})")).IsEqualTo((100 + live).ToString());
		}
		finally
		{
			if (!disconnected) await ConnectionService.Disconnect(player.Handle);
		}
	}

	[Test]
	public async Task ListcommandsListsWhatTheCallerMayRun()
	{
		var everything = (await Eval("listcommands()")).Split(' ');
		await Assert.That(everything).Contains("think");
		await Assert.That(everything).Contains("@blacklist");
		await Assert.That(everything).IsEquivalentTo(everything.Order(StringComparer.Ordinal),
			TUnit.Assertions.Enums.CollectionOrdering.Matching);

		var mortal = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "ListCmdsMortal");
		var mortals = (await EvalAs(mortal, "listcommands()")).Split(' ');
		await Assert.That(mortals).Contains("think");
		await Assert.That(mortals).DoesNotContain("@blacklist");

		await Assert.That((await Eval("listcommands(3)")).Split(' ')).Contains("@describe");
		await Assert.That(await Eval("listcommands(2)")).StartsWith(await Eval("listcommands(0)"));
	}

	[Test]
	public async Task ListflagsWritesEachFlagWithItsLetter()
	{
		var flags = (await Eval("listflags()")).Split(' ');
		await Assert.That(flags).Contains("DARK(D)");
		await Assert.That(flags).IsEquivalentTo(flags.Order(StringComparer.Ordinal),
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task RollbackRunsTheLastCommandsAgainWithNewArguments()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "Rollback");
		await AloneAsync(actor);
		var token = TestIsolationHelpers.GenerateUniqueName("rb");
		var command = TestIsolationHelpers.GenerateUniqueName("rbcmd");
		try
		{
			await CmdAs(actor.DbRef, actor.Handle,
				$"&DO me=${command} *:think {token}1-%0;think {token}2-%0;think {token}3-%0;@rollback 2/2=[add(%0,5)];think {token}end-%0");
			await CmdAs(actor.DbRef, actor.Handle, $"{command} 5");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Heard(actor, token)).IsEquivalentTo(
				["1-5", "2-5", "3-5", "2-10", "3-10", "2-15", "3-15", "end-5"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task RollbackRetryRunsEverythingBeforeItWhileTheConditionHolds()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RollbackRetry");
		await AloneAsync(actor);
		var token = TestIsolationHelpers.GenerateUniqueName("rr");
		var command = TestIsolationHelpers.GenerateUniqueName("rrcmd");
		try
		{
			await CmdAs(actor.DbRef, actor.Handle,
				$"&DO me=${command} *:think {token}1-%0;think {token}2-%0;@rollback/retry [gt(%0,1)]=[sub(%0,1)];think {token}end-%0");
			await CmdAs(actor.DbRef, actor.Handle, $"{command} 3");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Heard(actor, token)).IsEquivalentTo(
				["1-3", "2-3", "1-2", "2-2", "1-1", "2-1", "end-3"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task RollbackGivesEachArgumentItsOwnRegister()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RollbackArgs");
		await AloneAsync(actor);
		var token = TestIsolationHelpers.GenerateUniqueName("ra");
		var command = TestIsolationHelpers.GenerateUniqueName("racmd");
		try
		{
			await CmdAs(actor.DbRef, actor.Handle,
				$"&DO me=${command} *:think {token}%0-%1;@rollback 1=[add(%0,1)],[mul(%0,10)]");
			await CmdAs(actor.DbRef, actor.Handle, $"{command} 2");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Heard(actor, token)).IsEquivalentTo(
				["2-", "3-20"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task RollbackInsideAnInlineListRunsOnlyThatList()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RollbackInline");
		await AloneAsync(actor);
		var token = TestIsolationHelpers.GenerateUniqueName("ri");
		var command = TestIsolationHelpers.GenerateUniqueName("ricmd");
		try
		{
			await CmdAs(actor.DbRef, actor.Handle,
				$"&DO me=${command}:think {token}outer;@dolist/inline a b={{think {token}%i0;@rollback 5}};think {token}end");
			await CmdAs(actor.DbRef, actor.Handle, command);
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Heard(actor, token)).IsEquivalentTo(
				["outer", "a", "a", "b", "b", "end"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task RollbackRefusesLabels()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RollbackLabel");
		await AloneAsync(actor);
		var token = TestIsolationHelpers.GenerateUniqueName("rl");
		try
		{
			await CmdAs(actor.DbRef, actor.Handle, $"&DO me=$rlcmd{token}:think {token};@rollback/label foo|1");
			await CmdAs(actor.DbRef, actor.Handle, $"rlcmd{token}");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Notifications.For(actor.DbRef).Count(line => line == token)).IsEqualTo(1);
			await Assert.That(Notifications.For(actor.DbRef).Any(line => line.StartsWith("@ROLLBACK: SharpMUSH has no @goto labels")))
				.IsTrue();
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task LpagePagesWhoeverWasPagedLast()
	{
		var sender = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LpageFrom");
		var target = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LpageTo");
		var token = TestIsolationHelpers.GenerateUniqueName("lpage");
		try
		{
			await CmdAs(sender.DbRef, sender.Handle, $"page {target.DbRef}=first");
			await CmdAs(sender.DbRef, sender.Handle, $"lpage {token} 2+2=4");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Notifications.For(target.DbRef).Any(line => line.Contains($"{token} 2+2=4", StringComparison.Ordinal)))
				.IsTrue();
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(target.Handle);
		}
	}

	[Test]
	public async Task MrpageRepliesToTheSenderAndTheOtherRecipients()
	{
		var sender = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MrpageFrom");
		var replier = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MrpageMe");
		var other = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MrpageOther");
		var token = TestIsolationHelpers.GenerateUniqueName("mrpage");
		try
		{
			await CmdAs(replier.DbRef, replier.Handle, $"mrpage {token}");
			await WebAppFactoryArg.QueueBarrierAsync();
			await Assert.That(Notifications.For(replier.DbRef).Contains("No one has paged you yet.")).IsTrue();

			await CmdAs(sender.DbRef, sender.Handle, $"page {replier.DbRef} {other.DbRef}=lunch?");
			await CmdAs(replier.DbRef, replier.Handle, $"mrpage {token}=yes");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Notifications.For(sender.DbRef).Any(line => line.Contains($"{token}=yes", StringComparison.Ordinal)))
				.IsTrue();
			await Assert.That(Notifications.For(other.DbRef).Any(line => line.Contains($"{token}=yes", StringComparison.Ordinal)))
				.IsTrue();
		}
		finally
		{
			await ConnectionService.Disconnect(sender.Handle);
			await ConnectionService.Disconnect(replier.Handle);
			await ConnectionService.Disconnect(other.Handle);
		}
	}

	/// <summary>What <paramref name="actor"/> was told that starts with <paramref name="token"/>, less the token.</summary>
	private string[] Heard(TestIsolationHelpers.TestPlayer actor, string token)
		=> [.. Notifications.For(actor.DbRef).Where(line => line.StartsWith(token, StringComparison.Ordinal)).Select(line => line[token.Length..])];

	private async Task AloneAsync(TestIsolationHelpers.TestPlayer actor)
	{
		var room = DBRef.Parse(await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName("ShimRoom")}"));
		await Cmd($"@tel {actor.DbRef}={room}");
		await TestIsolationHelpers.ClearNoCommandAsync(CommandParser, ConnectionService, actor.DbRef);
	}
}

/// <summary>
/// RhostMUSH's <c>@blacklist</c> and <c>@site</c>, and TinyMUX's <c>@folder</c>. The sitelock rules are one stored
/// option, so these run one at a time with the other tests that change it.
/// </summary>
[NotInParallel]
public class RhostSiteCommandTests : ServerTestBase
{
	private IOptionsMonitor<SharpMUSHOptions> Options => WebAppFactoryArg.Services.GetRequiredService<IOptionsMonitor<SharpMUSHOptions>>();

	private IReadOnlyDictionary<string, string[]> Rules => Options.CurrentValue.SitelockRules.Rules;

	private static string TestAddress() => $"198.51.100.{Random.Shared.Next(1, 255)}";

	[Test]
	public async Task BlacklistKeepsEachListOnTheAddressRule()
	{
		var address = TestAddress();
		var network = $"{address}/32";
		try
		{
			await Cmd($"@blacklist/add {address}");
			await Assert.That(Rules[network]).IsEquivalentTo(["blacklist", "!connect"]);

			await Cmd($"@blacklist/add/noguest {address} 255.255.255.255");
			await Assert.That(Rules[network]).IsEquivalentTo(["blacklist", "!connect", "blacklist_noguest", "!guest"]);

			await Cmd($"@blacklist/del {address}");
			await Assert.That(Rules[network]).IsEquivalentTo(["blacklist_noguest", "!guest"]);

			await Cmd("@blacklist/clear/noguest");
			await Assert.That(Rules.ContainsKey(network)).IsFalse();
		}
		finally
		{
			await Cmd($"@sitelock/remove {network}");
		}
	}

	[Test]
	public async Task BlacklistMasksANetwork()
	{
		var address = TestAddress();
		try
		{
			await Cmd($"@blacklist/add/register {address} 255.255.255.0");
			await Assert.That(Rules["198.51.100.0/24"]).IsEquivalentTo(["blacklist_register", "!create", "register"]);
		}
		finally
		{
			await Cmd("@sitelock/remove 198.51.100.0/24");
		}
	}

	[Test]
	public async Task BlacklistLeavesHandWrittenRulesAlone()
	{
		var address = TestAddress();
		try
		{
			await Cmd($"@sitelock {address}/32=!create");
			await Cmd($"@blacklist/add {address}");
			await Assert.That(Rules[$"{address}/32"]).IsEquivalentTo(["!create"]);
		}
		finally
		{
			await Cmd($"@sitelock/remove {address}/32");
		}
	}

	[Test]
	public async Task SiteRemovesOneListUnderTheRhostDialect()
	{
		var address = TestAddress();
		try
		{
			await Cmd($"@sitelock {address}=!connect !guest");
			using (TestOptionsOverride.Scope(options => options with
			{
				Compatibility = options.Compatibility with { Dialect = "rhost" }
			}))
			{
				await Cmd($"@site/forbid {address}=255.255.255.255");
				await Assert.That(Rules[address]).IsEquivalentTo(["!guest"]);

				await Cmd($"@site/all {address}=/32");
				await Assert.That(Rules.ContainsKey(address)).IsFalse();
			}
		}
		finally
		{
			await Cmd($"@sitelock/remove {address}");
		}
	}

	[Test]
	public async Task SiteTakesTheBlacklistEntryWithTheRefusal()
	{
		var address = TestAddress();
		var network = $"{address}/32";
		try
		{
			await Cmd($"@blacklist/add {address}");
			await Cmd($"@blacklist/add/noguest {address}");
			using (TestOptionsOverride.Scope(options => options with
			{
				Compatibility = options.Compatibility with { Dialect = "rhost" }
			}))
			{
				await Cmd($"@site/forbid {address}=/32");
			}

			await Assert.That(Rules[network]).IsEquivalentTo(["blacklist_noguest", "!guest"]);
		}
		finally
		{
			await Cmd($"@sitelock/remove {network}");
		}
	}

	[Test]
	public async Task FolderFilesAndReadsMailUnderTheMuxDialect()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MuxFolder");
		var token = TestIsolationHelpers.GenerateUniqueName("folderbody");
		try
		{
			await CmdAs(actor.DbRef, actor.Handle, $"@mail {actor.DbRef}=Subject/{token}");
			await WebAppFactoryArg.QueueBarrierAsync();

			using (TestOptionsOverride.Scope(options => options with
			{
				Compatibility = options.Compatibility with { Dialect = "mux" }
			}))
			{
				await CmdAs(actor.DbRef, actor.Handle, "@folder/file 1=1");
				await CmdAs(actor.DbRef, actor.Handle, "@folder 1=1");
				await WebAppFactoryArg.QueueBarrierAsync();
			}

			await Assert.That(Notifications.For(actor.DbRef).Count(line => line.Contains(token, StringComparison.Ordinal)))
				.IsGreaterThanOrEqualTo(1);
			await Assert.That(await Eval($"mail({actor.DbRef},1:1)")).Contains(token);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}
}
