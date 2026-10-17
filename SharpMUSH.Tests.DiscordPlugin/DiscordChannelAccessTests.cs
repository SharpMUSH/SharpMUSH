using Discord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.DiscriminatedUnions;
using NSubstitute;
using SharpMUSH.Plugins.Discord.Links;
using SharpMUSH.Plugins.Discord.Members;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordChannelAccessTests
{
	private sealed class Channel : IDiscordPermissionChannel
	{
		public ulong LinkedRoleId { get; set; } = 2;
		public ulong EveryoneRoleId => 1;
		public Dictionary<ulong, OverwritePermissions> Values { get; } = new() { [1] = OverwritePermissions.InheritAll, [2] = OverwritePermissions.InheritAll };
		public bool Fail { get; set; }
		public Task EnsureLinkedRoleAsync(CancellationToken ct) => Task.CompletedTask;
		public Task<OverwritePermissions?> ReadAsync(ulong role, CancellationToken ct) => Task.FromResult<OverwritePermissions?>(Values.TryGetValue(role, out var value) ? value : null);
		public Task WriteAsync(ulong role, Func<OverwritePermissions, OverwritePermissions> change, CancellationToken ct)
		{
			if (Fail) throw new HttpRequestException("Synthetic Discord outage");
			if (Values.TryGetValue(role, out var current)) Values[role] = change(current);
			return Task.CompletedTask;
		}
	}
	[Test]
	public async Task ReplacingADeletedLinkedRoleCapturesTheNewRoleBeforeReapplyingTheGate()
	{
		var channel = new Channel();
		var store = Substitute.For<IDiscordLinkStore>();
		var link = new DiscordChannelLink(42, 9, 8, "help", "Guild", DateTimeOffset.UtcNow, "#1:1", PermissionHistoryKnown: true); store.Get(42).Returns(_ => link);
		store.UpdateAsync(42, Arg.Any<Func<DiscordChannelLink, DiscordChannelLink>>(), Arg.Any<CancellationToken>()).Returns(call =>
		{ link = call.Arg<Func<DiscordChannelLink, DiscordChannelLink>>()(link); return ValueTask.FromResult<DiscordChannelLink?>(link); });
		var channels = Substitute.For<IDiscordPermissionChannels>(); channels.OpenAsync(42, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IDiscordPermissionChannel>(channel));
		var access = new DiscordChannelAccess(store, channels);
		await access.GateAsync(42, default);
		channel.Values.Remove(2); channel.LinkedRoleId = 3; channel.Values[3] = OverwritePermissions.InheritAll;
		await access.GateAsync(42, default);
		await access.RestoreAsync(42, default);
		await Assert.That(channel.Values[3].SendMessages).IsEqualTo(PermValue.Inherit);
		await Assert.That(channel.Values[1].SendMessages).IsEqualTo(PermValue.Inherit);
	}

	[Test]
	public async Task LegacyCleanupReleasesOnlyMatchingHistoricalBotValues()
	{
		var channel = new Channel();
		channel.Values[1] = channel.Values[1].Modify(sendMessages: PermValue.Deny, sendMessagesInThreads: PermValue.Allow);
		channel.Values[2] = channel.Values[2].Modify(sendMessages: PermValue.Allow, sendMessagesInThreads: PermValue.Deny);
		var store = Substitute.For<IDiscordLinkStore>();
		var link = new DiscordChannelLink(42, 9, 8, "help", "Guild", DateTimeOffset.UtcNow, "#1:1"); store.Get(42).Returns(_ => link);
		store.UpdateAsync(42, Arg.Any<Func<DiscordChannelLink, DiscordChannelLink>>(), Arg.Any<CancellationToken>()).Returns(call =>
		{ link = call.Arg<Func<DiscordChannelLink, DiscordChannelLink>>()(link); return ValueTask.FromResult<DiscordChannelLink?>(link); });
		var channels = Substitute.For<IDiscordPermissionChannels>(); channels.OpenAsync(42, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IDiscordPermissionChannel>(channel));
		await Assert.That(await new DiscordChannelAccess(store, channels).RestoreAsync(42, default)).IsNull();
		await Assert.That(channel.Values[1].SendMessages).IsEqualTo(PermValue.Inherit);
		await Assert.That(channel.Values[1].SendMessagesInThreads).IsEqualTo(PermValue.Allow);
		await Assert.That(channel.Values[2].SendMessages).IsEqualTo(PermValue.Inherit);
		await Assert.That(channel.Values[2].SendMessagesInThreads).IsEqualTo(PermValue.Deny);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ActualPermissionAdapterUsesFreshRestStateAndSkipsDeletedSavedRole(bool deleteRole)
	{
		var everyone = Substitute.For<IRole>(); everyone.Id.Returns(1UL);
		var linked = Substitute.For<IRole>(); linked.Id.Returns(2UL);
		var roles = new Dictionary<ulong, IRole> { [1] = everyone, [2] = linked };
		var live = new Dictionary<ulong, OverwritePermissions> { [1] = OverwritePermissions.InheritAll, [2] = OverwritePermissions.InheritAll };
		var rest = Substitute.For<IGuildChannel>();
		rest.GetPermissionOverwrite(Arg.Any<IRole>()).Returns(call => live.GetValueOrDefault(call.Arg<IRole>().Id));
		rest.AddPermissionOverwriteAsync(Arg.Any<IRole>(), Arg.Any<OverwritePermissions>(), Arg.Any<RequestOptions>()).Returns(call =>
		{ live[call.Arg<IRole>().Id] = call.Arg<OverwritePermissions>(); return Task.CompletedTask; });
		var reads = 0; var roleCreations = 0;
		var adapter = new DiscordPermissionChannel(1, 2,
			_ => { roleCreations++; return Task.FromResult(linked); },
			_ => { reads++; return Task.FromResult(rest); },
			(id, _) => Task.FromResult<IRole?>(roles.GetValueOrDefault(id)));
		var store = Substitute.For<IDiscordLinkStore>();
		var link = new DiscordChannelLink(42, 9, 8, "help", "Guild", DateTimeOffset.UtcNow, "#1:1", PermissionHistoryKnown: true);
		store.Get(42).Returns(_ => link);
		store.UpdateAsync(42, Arg.Any<Func<DiscordChannelLink, DiscordChannelLink>>(), Arg.Any<CancellationToken>()).Returns(call =>
		{ link = call.Arg<Func<DiscordChannelLink, DiscordChannelLink>>()(link); return ValueTask.FromResult<DiscordChannelLink?>(link); });
		var channels = Substitute.For<IDiscordPermissionChannels>(); channels.OpenAsync(42, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IDiscordPermissionChannel>(adapter));
		var access = new DiscordChannelAccess(store, channels);
		await access.GateAsync(42, default);
		await Assert.That(live[1].SendMessages).IsEqualTo(PermValue.Deny);
		// Gateway cache deliberately never advances. Live REST state also carries a new unrelated administrator bit.
		live[1] = live[1].Modify(manageMessages: PermValue.Allow);
		if (!deleteRole)
		{
			await adapter.ReadAsync(1, default);
			live[1] = live[1].Modify(sendMessages: PermValue.Allow);
		}
		if (deleteRole) roles.Remove(2);
		await Assert.That(await access.RestoreAsync(42, default)).IsNull();
		await Assert.That(live[1].SendMessages).IsEqualTo(deleteRole ? PermValue.Inherit : PermValue.Allow);
		await Assert.That(live[1].ManageMessages).IsEqualTo(PermValue.Allow);
		if (!deleteRole) await Assert.That(live[2].SendMessages).IsEqualTo(PermValue.Inherit);
		await Assert.That(roleCreations).IsEqualTo(1).Because("restore must not create a replacement Linked role");
		await Assert.That(reads > 4).IsTrue();
	}

	[Test]
	public async Task RegisteredRelayRefusesUnlinkWhenItsPermissionAdapterFails()
	{
		var services = new ServiceCollection();
		new SharpMUSH.Plugins.Discord.DiscordPlugin().RegisterServices(services);
		var store = Substitute.For<IDiscordLinkStore>();
		store.Get(42).Returns(new DiscordChannelLink(42, 9, 8, "help", "Guild", DateTimeOffset.UtcNow, "#1:1"));
		var access = Substitute.For<IDiscordChannelGate>(); access.RestoreAsync(42, Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>("Synthetic permission failure"));
		var secrets = Substitute.For<ISecretStore>();
		services.AddLogging().AddSingleton(store).AddSingleton(access).AddSingleton(secrets)
			.AddSingleton(Substitute.For<IDiscordMessagePairs>()).AddSingleton(Substitute.For<IDiscordMessages>())
			.AddSingleton(new SharpMUSH.Plugins.Discord.DiscordGatewayService(secrets, [], NullLogger<SharpMUSH.Plugins.Discord.DiscordGatewayService>.Instance));
		using var provider = services.BuildServiceProvider();
		var result = await provider.GetRequiredService<DiscordRelay>().UnlinkAsync(42, default);
		await Assert.That(result is Error<string>).IsTrue();
		await store.DidNotReceive().RemoveAsync(42, Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task GateAndRestorePreserveOriginalBitsAndLaterAdministratorChanges()
	{
		var channel = new Channel();
		channel.Values[1] = channel.Values[1].Modify(sendMessages: PermValue.Allow, manageMessages: PermValue.Allow);
		var links = Substitute.For<IDiscordLinkStore>();
		var link = new DiscordChannelLink(42, 9, 8, "help", "Guild", DateTimeOffset.UtcNow, "#1:1");
		links.Get(42).Returns(_ => link);
		links.SaveAsync(Arg.Any<DiscordChannelLink>(), Arg.Any<CancellationToken>()).Returns(call => { link = call.Arg<DiscordChannelLink>(); return ValueTask.CompletedTask; });
		links.UpdateAsync(42, Arg.Any<Func<DiscordChannelLink, DiscordChannelLink>>(), Arg.Any<CancellationToken>()).Returns(call => { link = call.Arg<Func<DiscordChannelLink, DiscordChannelLink>>()(link); return ValueTask.FromResult<DiscordChannelLink?>(link); });
		var channels = Substitute.For<IDiscordPermissionChannels>();
		channels.OpenAsync(42, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IDiscordPermissionChannel>(channel));
		var access = new DiscordChannelAccess(links, channels);
		await Assert.That(await access.GateAsync(42, default)).IsNull();
		channel.Values[2] = channel.Values[2].Modify(sendMessagesInThreads: PermValue.Deny, manageMessages: PermValue.Deny);
		await Assert.That(await access.SetUnrestrictedAsync(42, true, default)).IsNull();
		await Assert.That(link.Unrestricted).IsTrue();
		await Assert.That(channel.Values[1].SendMessages).IsEqualTo(PermValue.Allow);
		await Assert.That(channel.Values[1].ManageMessages).IsEqualTo(PermValue.Allow);
		await Assert.That(channel.Values[2].SendMessages).IsEqualTo(PermValue.Inherit);
		await Assert.That(channel.Values[2].SendMessagesInThreads).IsEqualTo(PermValue.Deny);
		await Assert.That(channel.Values[2].ManageMessages).IsEqualTo(PermValue.Deny);
	}
	[Test]
	public async Task FailedRestoreRetainsBaselineAndRestrictedModeForRetry()
	{
		var channel = new Channel();
		var links = Substitute.For<IDiscordLinkStore>();
		var link = new DiscordChannelLink(42, 9, 8, "help", "Guild", DateTimeOffset.UtcNow, "#1:1");
		links.Get(42).Returns(_ => link);
		links.SaveAsync(Arg.Any<DiscordChannelLink>(), Arg.Any<CancellationToken>()).Returns(call => { link = call.Arg<DiscordChannelLink>(); return ValueTask.CompletedTask; });
		links.UpdateAsync(42, Arg.Any<Func<DiscordChannelLink, DiscordChannelLink>>(), Arg.Any<CancellationToken>()).Returns(call => { link = call.Arg<Func<DiscordChannelLink, DiscordChannelLink>>()(link); return ValueTask.FromResult<DiscordChannelLink?>(link); });
		var channels = Substitute.For<IDiscordPermissionChannels>();
		channels.OpenAsync(42, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IDiscordPermissionChannel>(channel));
		var access = new DiscordChannelAccess(links, channels);
		await access.GateAsync(42, default);
		channel.Fail = true;
		await Assert.That(await access.SetUnrestrictedAsync(42, true, default)).IsNotNull();
		await Assert.That(link.Unrestricted).IsFalse();
		await Assert.That(link.GateSnapshot).IsNotNull();
		channel.Fail = false;
		await Assert.That(await access.SetUnrestrictedAsync(42, true, default)).IsNull();
	}
}
