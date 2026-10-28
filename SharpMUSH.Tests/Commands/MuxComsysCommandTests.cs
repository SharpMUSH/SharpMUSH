using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>TinyMUX's channel administration commands and functions, built in beside addcom while use_muxcomm is on.</summary>
public class MuxComsysCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Factory.Services.GetRequiredService<IMUSHCodeParser>();
	private ITaskScheduler Scheduler => Factory.Services.GetRequiredService<ITaskScheduler>();

	[Test]
	public async Task TinyMuxChannelCommandsRunAsTheirChannelCommands()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			Factory.Services, Factory.Services.GetRequiredService<IMediator>(), Connections, "MuxComsys");
		try
		{
			await Parser.CommandParse(1, Connections, MarkupText.Plain($"@set *{player.Name}=WIZARD"));
			await Scheduler.SettleForTestsAsync();
			var channel = $"Mux{TestIsolationHelpers.GenerateUniqueName("C")[^6..]}";

			await Run(player, $"@ccreate {channel}");
			await Assert.That(await Think(player, $"MC-PRIVS [sort(lcstr(clflags({channel})))]")).IsEqualTo("MC-PRIVS object player");

			await Run(player, $"@cset/mute {channel}");
			await Run(player, $"@cset/spoof {channel}");
			await Assert.That(await Think(player, $"MC-MUTE [sort(lcstr(clflags({channel})))]")).IsEqualTo("MC-MUTE nonames object player quiet");
			await Run(player, $"@cset/loud {channel}");
			await Run(player, $"@cset/nospoof {channel}");
			await Run(player, $"@cpflags {channel}=!join");
			await Assert.That(await Think(player, $"MC-LOUD [sort(lcstr(clflags({channel})))]")).IsEqualTo("MC-LOUD object");
			await Run(player, $"@cpflags {channel}=join");
			await Run(player, $"@cset/log {channel}=[add(20,30)]");
			await Assert.That(await Think(player, $"MC-JOIN [sort(lcstr(clflags({channel})))]")).IsEqualTo("MC-JOIN object player");
			await Assert.That(string.Join("\n", await Run(player, $"@channel/decompile {channel}"))).Contains($"@channel/buffer {channel} = 50");

			await Assert.That(await Run(player, $"@cset/header {channel}=x")).Contains(line => line.Contains("no channel header"));
			await Assert.That(await Run(player, $"@cset/bogus {channel}")).Contains(line => line.Contains("doesn't know switch BOGUS"));
			await Assert.That(await Run(player, $"@coflags {channel}=transmit")).Contains(line => line.Contains("no transmit flag"));

			await Run(player, $"addcom mc={channel}");
			await Run(player, $"comtitle mc=Herald");
			await Assert.That(await Think(player, $"MC-ALIAS [comalias(me,{channel})] [comtitle(me,{channel})] [chanobj({channel})]"))
				.IsEqualTo("MC-ALIAS mc Herald #-1");
			await Assert.That(await Think(player, $"MC-OTHER [comalias(#1,{channel})]")).IsEqualTo("MC-OTHER #-1 PERMISSION DENIED");

			await Run(player, "allcom off");
			await Assert.That(await Think(player, $"MC-OFF [cstatus(me,{channel})]")).Contains("GAG");
			await Run(player, "allcom on");
			await Assert.That(await Think(player, $"MC-ON [cstatus(me,{channel})]")).DoesNotContain("GAG");

			await Run(player, "clearcom");
			await Assert.That(await Think(player, $"MC-CLEAR [comalias(me,{channel})][lattr(me/CHANALIAS`*)]")).IsEqualTo("MC-CLEAR");

			await Assert.That(await Run(player, $"@cset/private {channel}")).Contains(line => line.StartsWith("SEE lock set"));
			await Assert.That(await Run(player, $"@cset/public {channel}")).Contains(line => line.StartsWith("SEE lock removed"));

			await Run(player, $"@channel/on {channel}");
			await Assert.That(await Run(player, $"@cwho {channel}/all")).Contains(line => line.Contains(player.Name));
			// Nothing is forced, so God may use them too.
			var god = Factory.ExecutorDBRef;
			var heard = Factory.Notifications.CountFor(god);
			await Parser.CommandParse(1, Connections, MarkupText.Plain($"@cwho {channel}"));
			await Scheduler.SettleForTestsAsync();
			await Assert.That(Factory.Notifications.For(god).Skip(heard)).Contains(line => line.Contains(player.Name));
			await Run(player, $"@cboot/quiet {channel}=*{player.Name}");
			await Assert.That(await Think(player, $"MC-BOOT [cstatus(me,{channel})]")).IsEqualTo("MC-BOOT OFF");
			await Assert.That(await Run(player, $"@cchown {channel}=*{player.Name}")).DoesNotContain(line => line.Contains("can't see"));


			await Run(player, $"@cdestroy {channel}");
			await Assert.That(await Think(player, $"MC-GONE [clflags({channel})]")).StartsWith("MC-GONE #-1");
		}
		finally
		{
			await Connections.Disconnect(player.Handle);
		}
	}

	[Test]
	public async Task TheChannelObjectTellsAMemberWhoJoinsLeavesOrGags()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			Factory.Services, Factory.Services.GetRequiredService<IMediator>(), Connections, "MuxComsysHooks");
		try
		{
			await Parser.CommandParse(1, Connections, MarkupText.Plain($"@set *{player.Name}=WIZARD"));
			await Scheduler.SettleForTestsAsync();
			var channel = $"Mux{TestIsolationHelpers.GenerateUniqueName("H")[^6..]}";
			var tag = TestIsolationHelpers.GenerateUniqueName("Hook");

			await Run(player, $"@ccreate {channel}");
			var created = await Think(player, "MC-OBJ [create(Channel Object)]");
			var thing = created["MC-OBJ ".Length..];
			await Run(player, $"&COMJOIN {thing}={tag} join %n");
			await Run(player, $"&COMLEAVE {thing}={tag} leave %n");
			await Run(player, $"&COMON {thing}={tag} on %n");
			await Run(player, $"&COMOFF {thing}={tag} off %n");
			await Run(player, $"@cset/object {channel}={thing}");
			await Assert.That(await Think(player, $"MC-CHANOBJ [chanobj({channel})]")).StartsWith($"MC-CHANOBJ {thing}");

			// @ccreate puts its creator on the channel.
			await Assert.That(await Run(player, $"@channel/off {channel}")).Contains($"{tag} leave {player.Name}");
			await Assert.That(await Run(player, $"addcom mh={channel}")).Contains($"{tag} join {player.Name}");
			await Assert.That(await Run(player, "allcom off")).Contains($"{tag} off {player.Name}");
			await Assert.That(await Run(player, "allcom off")).DoesNotContain(line => line.StartsWith(tag));
			await Assert.That(await Run(player, "allcom on")).Contains($"{tag} on {player.Name}");
			await Assert.That(await Run(player, "delcom mh")).Contains($"{tag} leave {player.Name}");
			await Assert.That(await Run(player, $"@channel/on {channel}")).Contains($"{tag} join {player.Name}");

			await Run(player, $"@cdestroy {channel}");
		}
		finally
		{
			await Connections.Disconnect(player.Handle);
		}
	}

	[Test]
	public async Task AMortalMayDoOnlyWhatChannelLetsThem()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			Factory.Services, Factory.Services.GetRequiredService<IMediator>(), Connections, "MuxComsysMortal");
		try
		{
			var channel = $"Mux{TestIsolationHelpers.GenerateUniqueName("M")[^6..]}";
			await Parser.CommandParse(1, Connections, MarkupText.Plain($"@channel/add {channel}=player"));
			await Scheduler.SettleForTestsAsync();

			await Run(player, $"@cdestroy {channel}");
			await Run(player, $"@cset/mute {channel}");

			await Assert.That(await Think(player, $"MC-KEPT [sort(lcstr(clflags({channel})))]")).IsEqualTo("MC-KEPT player");
		}
		finally
		{
			await Connections.Disconnect(player.Handle);
		}
	}

	private async Task<List<string>> Run(TestIsolationHelpers.TestPlayer player, string command)
	{
		var heard = Factory.Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain(command));
		await Scheduler.SettleForTestsAsync();
		return Factory.Notifications.For(player.DbRef).Skip(heard).ToList();
	}

	/// <summary>The line <c>think</c> prints for <paramref name="text"/>, found by its first word.</summary>
	private async Task<string> Think(TestIsolationHelpers.TestPlayer player, string text)
	{
		var heard = await Run(player, $"think {text}");
		var tag = text.Split(' ')[0];
		return heard.LastOrDefault(line => line.StartsWith(tag))?.TrimEnd()
			?? throw new InvalidOperationException($"'{tag}' was not printed: {string.Join(" | ", Factory.Notifications.For(player.DbRef))}");
	}
}
