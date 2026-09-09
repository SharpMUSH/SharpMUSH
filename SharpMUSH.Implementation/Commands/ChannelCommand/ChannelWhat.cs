using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
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
		IPermissionService permissionService, IMediator mediator, INotifyService notifyService, MString prefixArgument)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var prefix = prefixArgument.ToPlainText().Trim();

		List<MString> lines = [];

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
			var owner = await ChannelHelper.TryResolveOwner(channel);

			lines.Add(channel.Name);
			lines.Add(MarkupText.Concat(MarkupText.Plain("Description: "), channel.Description));
			lines.Add(MarkupText.Plain(owner is null
				? "Owner: #-1"
				: $"Owner: {owner.Object.Name}(#{owner.Object.DBRef.Number})"));

			// extchat.c:2757 — the mogrifier line only appears when one is set.
			if (!string.IsNullOrEmpty(channel.Mogrifier))
			{
				lines.Add(MarkupText.Plain($"Mogrifier: {channel.Mogrifier}"));
			}

			lines.Add(MarkupText.Plain($"Flags: {ChannelHelper.PrivilegeNames(channel.Privs)}"));

			var storedLines = await mediator.Send(new CountChannelMessagesQuery(channel.Id ?? string.Empty));
			lines.Add(MarkupText.Plain(
				string.Format(ErrorMessages.Notifications.ChatRecallBufferSummary, channel.Buffer, storedLines)));

			// extchat.c:2770 — the locks are shown only to someone who could decompile the channel, and only
			// the ones that are actually set.
			if (await permissionService.ChannelCanDecomposeAsync(executor, channel))
			{
				List<string> locks = [];
				void Add(string label, string key)
				{
					if (!string.IsNullOrEmpty(key))
					{
						locks.Add($"\n{label,7}: {key}");
					}
				}

				Add("mod", channel.ModLock);
				Add("hide", channel.HideLock);
				Add("join", channel.JoinLock);
				Add("speak", channel.SpeakLock);
				Add("see", channel.SeeLock);

				if (locks.Count != 0)
				{
					lines.Add(MarkupText.Plain($"Locks:{string.Concat(locks)}"));
				}
			}
		}

		if (lines.Count == 0)
		{
			await notifyService.Notify(executor, ErrorMessages.Notifications.DontRecognizeThatChannel, executor);
			return new CallState(ErrorMessages.Returns.NoSuchChannel);
		}

		var output = MarkupText.Join(MarkupText.NewLine, lines);
		await notifyService.Notify(executor, output, executor);
		return new CallState(output);
	}
}
