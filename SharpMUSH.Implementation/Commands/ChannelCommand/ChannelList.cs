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
		var channels = await Mediator.Send(new GetChannelListQuery());

		var quietSwitch = switches.Contains("QUIET");
		var onSwitch = switches.Contains("ON");
		var offSwitch = switches.Contains("OFF");

		// Switches: On, Off, Or Quiet. On/off are exclusive.
		if (onSwitch && offSwitch)
		{
			await NotifyService.Notify(executor, "You can only use one of /on or /off.");
			return new CallState(Errors.ErrorTooManySwitches);
		}

		var channelList = await channels
			.ToAsyncEnumerable()
			.Where(async (x, _) => await PermissionService.ChannelCanSeeAsync(executor, x))
			.Where(async (x, ct) => !offSwitch || await x.Members.Value
				.AllAsync(m => m.Member.Object().Id != executor.Object().Id, ct))
			.Where(async (x, ct) => !onSwitch || await x.Members.Value
				.AnyAsync(m => m.Member.Object().Id == executor.Object().Id, ct))
			.Select((channel, _) => quietSwitch
				? channel.Name
				: MModule.concat(MModule.single("Name: "), channel.Name))
			.ToArrayAsync();

		return new CallState(MModule.multiple(channelList));
	}
}