using Discord;
using Discord.Net;
using SharpMUSH.Plugins.Discord.Links;

namespace SharpMUSH.Plugins.Discord.Members;

/// <summary>The Discord adapter exposes complete current overwrites; the coordinator changes only posting bits.</summary>
public interface IDiscordPermissionChannel
{
	ulong EveryoneRoleId { get; }
	ulong LinkedRoleId { get; }
	Task EnsureLinkedRoleAsync(CancellationToken ct);
	Task<OverwritePermissions?> ReadAsync(ulong roleId, CancellationToken ct);
	Task WriteAsync(ulong roleId, Func<OverwritePermissions, OverwritePermissions> change, CancellationToken ct);
}
public interface IDiscordPermissionChannels
{
	Task<IDiscordPermissionChannel> OpenAsync(ulong channelId, CancellationToken ct);
	IReadOnlyList<string>? OpenTo(ulong channelId);
}
public sealed record DiscordGateRole(ulong RoleId, PermValue Messages, PermValue Threads);
public sealed record DiscordGateSnapshot(DiscordGateRole Everyone, DiscordGateRole Linked);

/// <summary>Persists undo information before touching Discord; retrying restores only values still owned by the bot.</summary>
public sealed class DiscordChannelAccess(IDiscordLinkStore links, IDiscordPermissionChannels channels, DiscordLinkMutationGate? mutations = null) : IDiscordChannelGate
{
	private readonly DiscordLinkMutationGate changing = mutations ?? new();
	public IReadOnlyList<string>? OpenTo(ulong channelId) => channels.OpenTo(channelId);
	public Task<string?> GateAsync(ulong channelId, CancellationToken cancellationToken) => SetUnrestrictedAsync(channelId, false, cancellationToken);
	public Task<string?> SetUnrestrictedAsync(ulong channelId, bool unrestricted, CancellationToken cancellationToken)
		=> ChangeAsync(channelId, unrestricted, cancellationToken);
	public Task<string?> RestoreAsync(ulong channelId, CancellationToken cancellationToken) => ChangeAsync(channelId, null, cancellationToken);

	public Task<string?> ReconcileAsync(ulong channelId, CancellationToken ct)
		=> changing.RunAsync<string?>(() => links.Get(channelId) is { } current
			? ChangeAsync(channelId, current.Unrestricted, ct) : Task.FromResult<string?>("That channel is not linked."), ct);

	private Task<string?> ChangeAsync(ulong channelId, bool? unrestricted, CancellationToken ct)
		=> changing.RunAsync<string?>(async () =>
		{
			if (links.Get(channelId) is not { } link) return "That channel is not linked.";
			try
			{
				var channel = await channels.OpenAsync(channelId, ct);
				if (unrestricted == false)
				{
					await channel.EnsureLinkedRoleAsync(ct);
					if (link.GateSnapshot is null)
					{
						link = link with { GateSnapshot = new(await Capture(channel.EveryoneRoleId, PermValue.Deny), await Capture(channel.LinkedRoleId, PermValue.Allow)) };
						await links.UpdateAsync(channelId, current => current with { GateSnapshot = link.GateSnapshot, Unrestricted = link.Unrestricted }, ct);
					}
					if (link.GateSnapshot is { } previous && previous.Linked.RoleId != channel.LinkedRoleId)
					{
						await Restore(previous.Linked, PermValue.Allow);
						link = link with { GateSnapshot = previous with { Linked = await Capture(channel.LinkedRoleId, PermValue.Allow) } };
						await links.UpdateAsync(channelId, current => current with { GateSnapshot = link.GateSnapshot }, ct);
					}

					link = link with { Unrestricted = false };
					await links.UpdateAsync(channelId, current => current with { GateSnapshot = link.GateSnapshot, Unrestricted = link.Unrestricted }, ct);
					await Set(channel.LinkedRoleId, PermValue.Allow);
					await Set(channel.EveryoneRoleId, PermValue.Deny);
				}
				else if (link.GateSnapshot is { } original)
				{
					await Restore(original.Everyone, PermValue.Deny);
					await Restore(original.Linked, PermValue.Allow);
				}
				else if (!link.Unrestricted)
				{
					// Older links have no baseline. Only release the exact four historical bot values.
					await Restore(new(channel.EveryoneRoleId, PermValue.Inherit, PermValue.Inherit), PermValue.Deny);
					await Restore(new(channel.LinkedRoleId, PermValue.Inherit, PermValue.Inherit), PermValue.Allow);
				}
				await links.UpdateAsync(channelId, current => current with { Unrestricted = unrestricted ?? link.Unrestricted, GateSnapshot = unrestricted == false ? link.GateSnapshot : null, PermissionProblem = null, PermissionHistoryKnown = true }, ct);
				return null;

				async Task<DiscordGateRole> Capture(ulong role, PermValue applied)
				{
					var current = await channel.ReadAsync(role, ct) ?? OverwritePermissions.InheritAll;
					return new(role, !link.PermissionHistoryKnown && current.SendMessages == applied ? PermValue.Inherit : current.SendMessages,
						!link.PermissionHistoryKnown && current.SendMessagesInThreads == applied ? PermValue.Inherit : current.SendMessagesInThreads);
				}
				Task Set(ulong role, PermValue value) => channel.WriteAsync(role,
					current => current.Modify(sendMessages: value, sendMessagesInThreads: value), ct);
				Task Restore(DiscordGateRole baseline, PermValue applied) => channel.WriteAsync(baseline.RoleId,
					current => current.Modify(
						sendMessages: current.SendMessages == applied ? baseline.Messages : current.SendMessages,
						sendMessagesInThreads: current.SendMessagesInThreads == applied ? baseline.Threads : current.SendMessagesInThreads), ct);
			}

			catch (Exception error) when (error is HttpException or HttpRequestException or TimeoutException or InvalidOperationException)
			{
				var problem = "Discord permission update failed: " + error.Message + " Retry the operation after fixing the bot's permissions.";
				await links.UpdateAsync(channelId, current => current with { PermissionProblem = problem }, CancellationToken.None);
				return problem;
			}
		}, ct);
}
