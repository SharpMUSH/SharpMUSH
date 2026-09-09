using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

/// <summary>
/// <c>@channel/decompile[/brief] &lt;channel&gt;</c> — PennMUSH <c>do_chan_decompile</c>
/// (<c>src/extchat.c:2810-2880</c>): the commands that would recreate the channel — privileges, owner,
/// mogrifier, locks, description, buffer size and membership, in that order.
///
/// <para>The gate is <c>Chan_Can_Decomp</c>, the same right <c>@channel/what</c>'s lock section and
/// <c>clock()</c> answer to, rather than <c>Chan_Can_Modify</c>.</para>
/// </summary>
public static class ChannelDecompile
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService,
		IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService,
		IConnectionService ConnectionService, MString channelName, MString brief, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		var name = channel.Name.ToPlainText();

		// extchat.c:2824 — Chan_Can_Decomp, and the refusal names the channel because the viewer can
		// already see it.
		if (!await PermissionService.ChannelCanDecomposeAsync(executor, channel))
		{
			var refusal = string.Format(ErrorMessages.Notifications.ChatCannotDecompile, name);
			await NotifyService.Notify(executor, refusal, executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var owner = await channel.Owner.WithCancellation(CancellationToken.None);

		List<MString> commands =
		[
			MarkupText.Plain($"@channel/add {name} = {ChannelHelper.PrivilegeNames(channel.Privs)}"),
			MarkupText.Plain($"@channel/chown {name} = {owner.Object.Name}")
		];

		void AddIfSet(string command, string value)
		{
			if (!string.IsNullOrEmpty(value))
			{
				commands.Add(MarkupText.Plain($"{command} {name} = {value}"));
			}
		}

		AddIfSet("@channel/mogrifier", channel.Mogrifier);
		AddIfSet("@clock/mod", channel.ModLock);
		AddIfSet("@clock/hide", channel.HideLock);
		AddIfSet("@clock/join", channel.JoinLock);
		AddIfSet("@clock/speak", channel.SpeakLock);
		AddIfSet("@clock/see", channel.SeeLock);

		// The switch is DESCRIBE — this dispatcher matches switch names exactly, where PennMUSH abbreviates
		// them, so Penn's "@channel/desc" would not replay here. The description keeps its markup, since
		// replaying a decompile has to reproduce the colour too.
		if (channel.Description.Length != 0)
		{
			commands.Add(MarkupText.Concat(
				MarkupText.Plain($"@channel/describe {name} = "), channel.Description));
		}

		if (channel.Buffer != 0)
		{
			commands.Add(MarkupText.Plain($"@channel/buffer {name} = {channel.Buffer}"));
		}

		// extchat.c:2867 — /brief stops before the membership, and a member hiding on the channel is left
		// out of it unless the decompiler could have seen them on @channel/who anyway.
		if (!switches.Contains("BRIEF"))
		{
			var privilegedWho = await ChannelHelper.PrivilegedWho(executor);

			foreach (var member in await ChannelHelper.ChannelMembers(ConnectionService, channel))
			{
				if (member.Hidden && !privilegedWho)
				{
					continue;
				}

				commands.Add(MarkupText.Plain(member.Object.IsPlayer
					? $"@channel/on {name} = *{member.Object.Object().Name}"
					: $"@channel/on {name} = #{member.Object.Object().DBRef.Number}"));
			}
		}

		var output = MarkupText.Join(MarkupText.NewLine, commands);
		await NotifyService.Notify(executor, output, executor);
		return new CallState(output);
	}
}
