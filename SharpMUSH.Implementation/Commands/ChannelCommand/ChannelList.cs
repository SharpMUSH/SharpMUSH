using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelList
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString arg0, MString arg1, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var channels = await parser.Mediator.Send(new GetChannelListQuery());

		var quietSwitch = switches.Contains("QUIET");
		var onSwitch = switches.Contains("ON");
		var offSwitch = switches.Contains("OFF");

		// Switches: On, Off, Or Quiet. On/off are exclusive.
		if (onSwitch && offSwitch)
		{
			await parser.NotifyService.Notify(executor, "You can only use one of /on or /off.");
			return new CallState(Errors.ErrorTooManySwitches);
		}

		var channelList = await channels
			.ToAsyncEnumerable()
			.Where(async (x,_) => await parser.PermissionService.ChannelCanSeeAsync(executor,x))
			.Where(async (x,_) => !offSwitch || (await x.Members.WithCancellation(CancellationToken.None))
				.All(m => m.Member.Object().Id != executor.Object().Id))
			.Where(async (x,_) => !onSwitch || (await x.Members.WithCancellation(CancellationToken.None))
				.Any(m => m.Member.Object().Id == executor.Object().Id))
			.Select((channel,_) => quietSwitch 
				? channel.Name
				: MModule.concat(MModule.single("Name: "), channel.Name))
			.ToArrayAsync();
		
		return new CallState(MModule.multiple(channelList));
	}
}