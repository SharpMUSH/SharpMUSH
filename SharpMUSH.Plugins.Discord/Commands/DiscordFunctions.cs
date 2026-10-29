using System.Globalization;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Plugins.Discord.Links;

namespace SharpMUSH.Plugins.Discord.Commands;

/// <summary>Softcode's reads of the Discord link, for packages that build on it.</summary>
public static class DiscordFunctions
{
	/// <summary>
	/// <c>discordlink(&lt;channel id&gt;)</c>: <c>restricted</c> or <c>unrestricted</c> for a linked Discord channel,
	/// <c>#-1 NO SUCH LINK</c> for one that is not. Needs <c>discord.admin</c>, as <c>@discord</c> does.
	/// </summary>
	[SharpFunction(Name = "discordlink", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["channel id"])]
	public static async ValueTask<CallState> DiscordLink(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var services = parser.ServiceProvider;
		var executor = await parser.CurrentState.KnownExecutorObject(services.GetRequiredService<IMediator>());
		if (!await executor.Can(DiscordPlugin.AdminPermission))
		{
			return new CallState("#-1 PERMISSION DENIED");
		}

		var argument = parser.CurrentState.Arguments["0"].Message.ToPlainText().Trim();
		if (!ulong.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var channelId))
		{
			return new CallState("#-1 INVALID CHANNEL ID");
		}

		return services.GetRequiredService<IDiscordLinkStore>().Get(channelId) switch
		{
			{ Unrestricted: true } => new CallState("unrestricted"),
			{ } => new CallState("restricted"),
			null => new CallState("#-1 NO SUCH LINK"),
		};
	}
}
