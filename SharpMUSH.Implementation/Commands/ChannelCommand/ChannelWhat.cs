using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelWhat
{
	/// <summary>
	/// <c>@channel/what [&lt;prefix&gt;]</c> — sharpchat.md:187: "shows the name, description, owner,
	/// priv flags, mogrifier and buffer size for all channels, or all channels whose names begin with
	/// &lt;prefix&gt; if one is given."
	/// </summary>
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService locateService,
		IPermissionService permissionService, IMediator mediator, INotifyService notifyService, MString prefixArgument)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		if (await executor.IsGuest())
		{
			await notifyService.Notify(executor, ErrorMessages.Notifications.ChatGuestsCantModify);
			return new CallState(ErrorMessages.Returns.GuestsCannotModifyChannels);
		}

		var prefix = prefixArgument.ToPlainText().Trim();

		var channels = await mediator.CreateStream(new GetChannelListQuery())
			.Where(async (channel, _) => await permissionService.ChannelCanSeeAsync(executor, channel))
			.Where((channel, _) => ValueTask.FromResult(prefix.Length == 0
				|| channel.Name.ToPlainText().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
			.ToArrayAsync();

		if (channels.Length == 0)
		{
			await notifyService.Notify(executor, "CHAT: No channels match that.", executor);
			return new CallState(MModule.empty());
		}

		List<MString> lines = [];
		foreach (var channel in channels)
		{
			var owner = await channel.Owner.WithCancellation(CancellationToken.None);
			lines.Add(MModule.concat(MModule.single("Channel: "), channel.Name));
			lines.Add(MModule.concat(MModule.single("Description: "), channel.Description));
			lines.Add(MModule.single($"Owner: {owner.Object.Name}(#{owner.Object.DBRef.Number})"));
			lines.Add(MModule.single($"Flags: {string.Join(" ", channel.Privs)}"));
			lines.Add(MModule.single($"Mogrifier: {(string.IsNullOrEmpty(channel.Mogrifier) ? "none" : channel.Mogrifier)}"));
			lines.Add(MModule.single($"Buffer: {channel.Buffer}"));
		}

		var output = MModule.multipleWithDelimiter(MModule.single("\n"), lines);
		await notifyService.Notify(executor, output, executor);
		return new CallState(output);
	}
}
