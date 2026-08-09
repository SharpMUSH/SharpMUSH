using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelList
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService,
		IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString arg0, MString arg1,
		string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var channels = Mediator.CreateStream(new GetChannelListQuery());

		var quietSwitch = switches.Contains("QUIET");
		var onSwitch = switches.Contains("ON");
		var offSwitch = switches.Contains("OFF");

		// Switches: On, Off, Or Quiet. On/off are exclusive.
		if (onSwitch && offSwitch)
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.TooManySwitches,
				notifyMessage: "You can only use one of /on or /off.",
				shouldNotify: true);
		}

		// sharpchat.md:181 — "If a <prefix> is given, only channels whose names begin with <prefix> are shown."
		var prefix = arg0.ToPlainText().Trim();

		var channelList = await channels
			.Where(async (x, _) => await PermissionService.ChannelCanSeeAsync(executor, x))
			.Where((x, _) => ValueTask.FromResult(prefix.Length == 0
				|| x.Name.ToPlainText().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
			.Where(async (x, ct) => !offSwitch || await x.Members.Value
				.AllAsync(m => m.Member.Object().Id != executor.Object().Id, ct))
			.Where(async (x, ct) => !onSwitch || await x.Members.Value
				.AnyAsync(m => m.Member.Object().Id == executor.Object().Id, ct))
			.Select((channel, _) => quietSwitch
				? channel.Name
				: MModule.concat(MModule.single("Name: "), channel.Name))
			.ToArrayAsync();

		if (channelList.Length == 0)
		{
			await NotifyService.Notify(executor, "CHAT: No channels match that.", executor);
			return new CallState(MModule.empty());
		}

		var result = MModule.multipleWithDelimiter(MModule.single("\n"), channelList);
		await NotifyService.Notify(executor, result, executor);
		return new CallState(result);
	}
}