using MarkupString.Layout;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

/// <summary>
/// <c>@channel/what [&lt;prefix&gt;]</c> — PennMUSH <c>do_chan_what</c> (<c>src/extchat.c:2740-2800</c>):
/// the name, description, owner, mogrifier, privileges, buffer and — to anyone who may decompile it — the
/// locks, for every visible channel whose name starts with the prefix. It is a read, and Penn gates it
/// no further than <c>Chan_Can_See</c>.
/// </summary>
public static class ChannelWhat
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService locateService,
		IChannelPermissionService permissionService, IMediator mediator, INotifyService notifyService, MString prefixArgument)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var prefix = prefixArgument.ToPlainText().Trim();

		List<Block> panels = [];

		await foreach (var channel in mediator.CreateStream(new GetChannelListQuery()))
		{
			if (!await permissionService.ChannelCanSeeAsync(executor, channel)
					|| (prefix.Length != 0
							&& !channel.Name.ToPlainText().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			// One unresolvable owner must not take the whole sweep with it — see
			// ChannelHelper.TryResolveOwner.
			var owner = await ChannelHelper.TryResolveOwner(mediator, channel);

			List<(string Label, MString Value)> details =
			[
				("Description", channel.Description),
				("Owner", MarkupText.Plain(owner is SharpPlayer player
					? $"{player.Object.Name}(#{player.Object.DBRef.Number})"
					: "#-1")),
			];

			// extchat.c:2757 — the mogrifier line only appears when one is set.
			if (!string.IsNullOrEmpty(channel.Mogrifier))
			{
				details.Add(("Mogrifier", MarkupText.Plain(channel.Mogrifier)));
			}

			details.Add(("Flags", MarkupText.Plain(ChannelHelper.PrivilegeNames(channel.Privs))));

			var storedLines = await mediator.Send(new CountChannelMessagesQuery(channel.Id ?? string.Empty));
			details.Add(("Recall", MarkupText.Plain($"{channel.Buffer} full lines, with {storedLines} lines stored")));

			List<Block> parts = [ServerLayout.KeyValues(details)];

			// extchat.c:2770 — the locks are shown only to someone who could decompile the channel, and only
			// the ones that are actually set.
			if (await permissionService.ChannelCanDecomposeAsync(executor, channel))
			{
				var locks = new (string Label, string Key)[]
					{
						("Mod", channel.ModLock), ("Hide", channel.HideLock), ("Join", channel.JoinLock),
						("Speak", channel.SpeakLock), ("See", channel.SeeLock)
					}
					.Where(entry => !string.IsNullOrEmpty(entry.Key))
					.Select(entry => (entry.Label, MarkupText.Plain(entry.Key)))
					.ToList();

				if (locks.Count != 0)
				{
					parts.Add(new Rule(MarkupText.Plain("Locks")));
					parts.Add(ServerLayout.KeyValues(locks));
				}
			}

			panels.Add(ServerLayout.Panel(channel.Name, [.. parts]));
		}

		if (panels.Count == 0)
		{
			await notifyService.Notify(executor, ErrorMessages.Notifications.DontRecognizeThatChannel, executor);
			return new CallState(ErrorMessages.Returns.NoSuchChannel);
		}

		var output = ServerLayout.Build(panels.Count == 1 ? panels[0] : new Stack([.. panels]), 78);
		await notifyService.Notify(executor, output, executor);
		return new CallState(output);
	}
}
