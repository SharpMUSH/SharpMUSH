using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.InputSessions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@INPUT", Switches = ["START", "PROMPT", "CANCEL", "RESCUE", "WILD", "REGEX"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		SingleArgumentSwitches = ["PROMPT"], Output = CommandOutput.Value, MinArgs = 0, MaxArgs = MaxInputArguments,
		ParameterNames = ["prompt", "exit-pattern", "exit-attribute", "pattern", "attribute", "timeout-seconds"])]
	public async ValueTask<Option<CallState>> Input(IMUSHCodeParser parser, SharpCommandAttribute _)
	{
		var sessions = parser.ServiceProvider.GetRequiredService<IInputSessionService>();
		var switches = parser.CurrentState.Switches.ToArray();
		var arguments = parser.CurrentState.ArgumentsOrdered;
		// /start pairs with a pattern kind; every other switch stands alone.
		var kinds = switches.Count(switchName => switchName is "WILD" or "REGEX");
		if (kinds > 1 || switches.Length - kinds > 1 || (kinds == 1 && switches.Any(switchName => switchName is not ("WILD" or "REGEX" or "START"))))
			return await InputError(parser, "#-1 INPUT ACCEPTS ONE SWITCH");
		if (switches.Contains("CANCEL"))
			return await InputResult(parser, await sessions.CancelAsync(parser));
		if (switches.Contains("RESCUE"))
			return await InputRescue(parser, arguments.GetValueOrDefault("0")?.Message.ToPlainText() ?? "");
		if (switches.Contains("PROMPT"))
			return await InputResult(parser, await sessions.PromptAsync(parser, arguments.GetValueOrDefault("0")?.Message ?? MString.Empty));
		var match = switches.Contains("REGEX") ? InputMatch.Regex : switches.Contains("WILD") ? InputMatch.Wild : InputMatch.Exact;
		return await InputStart(parser, sessions, arguments.GetValueOrDefault("0")?.Message ?? MString.Empty,
			[.. Enumerable.Range(1, arguments.Count).Select(index => arguments.GetValueOrDefault(index.ToString(CultureInfo.InvariantCulture)))
				.TakeWhile(argument => argument is not null).Select(argument => argument!.Message.ToPlainText().Trim())], match);
	}

	/// <summary>One prompt, the pattern and attribute pairs after it, and an optional trailing timeout.</summary>
	private const int MaxInputArguments = 1 + 2 * 10 + 1;

	/// <summary>
	/// <c>@input/start &lt;prompt&gt;=&lt;exit pattern&gt;,&lt;exit attribute&gt;[,&lt;pattern&gt;,&lt;attribute&gt;...][,&lt;seconds&gt;]</c>:
	/// an odd count of values after the prompt ends in the timeout.
	/// </summary>
	private async ValueTask<Option<CallState>> InputStart(IMUSHCodeParser parser, IInputSessionService sessions, MString prompt,
		string[] values, InputMatch match)
	{
		if (values.Length < 2) return await InputError(parser, InputSessionService.MissingExit);
		var seconds = 60;
		if (values.Length % 2 == 1)
		{
			if (!values[^1].All(char.IsAsciiDigit)) return await InputError(parser, InputSessionService.UnpairedPattern);
			if (!int.TryParse(values[^1], NumberStyles.None, CultureInfo.InvariantCulture, out seconds) || seconds is < 1 or > 3600)
				return await InputError(parser, InputSessionService.InvalidTimeout);
		}
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var routes = new List<InputRouteSpec>();
		for (var index = 0; index + 1 < values.Length; index += 2)
		{
			var (pattern, path) = (values[index], values[index + 1]);
			var separator = path.IndexOf('/');
			if (separator == path.Length - 1 || separator == 0) return await InputError(parser, InputSessionService.InvalidCallback);
			if (separator < 0)
			{
				routes.Add(new InputRouteSpec(pattern, executor.Object().DBRef, path));
				continue;
			}
			switch (await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, path[..separator], LocateFlags.All))
			{
				case AnySharpObject target: routes.Add(new InputRouteSpec(pattern, target.Object().DBRef, path[(separator + 1)..])); break;
				case Error<CallState> error: return error.Value;
			}
		}
		return await InputResult(parser, await sessions.StartAsync(parser, prompt, routes, match, TimeSpan.FromSeconds(seconds)));
	}

	/// <summary>Staff end someone's session early by running its timeout callback now.</summary>
	private async ValueTask<Option<CallState>> InputRescue(IMUSHCodeParser parser, string name)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (!await executor.Can(PortalPermission.PlayersModerate))
		{
			await NotifyService.Notify(executor, ErrorMessages.Returns.PermissionDenied);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}
		if (string.IsNullOrWhiteSpace(name))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InputRescueUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}
		return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, name) switch
		{
			AnySharpObject and SharpPlayer player => await RescuedAsync(parser, executor, player),
			AnySharpObject => throw new InvalidOperationException("A player lookup found something that is not a player."),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> RescuedAsync(IMUSHCodeParser parser, AnySharpObject executor, SharpPlayer player)
	{
		var count = await parser.ServiceProvider.GetRequiredService<IInputSessionService>().RescueAsync(player.Object.DBRef);
		await NotifyService.NotifyLocalized(executor, count == 0
			? nameof(ErrorMessages.Notifications.InputRescueNoneFormat)
			: nameof(ErrorMessages.Notifications.InputRescuedFormat), executor, player.Object.Name);
		return new CallState(count.ToString(CultureInfo.InvariantCulture));
	}

	private async ValueTask<Option<CallState>> InputResult(IMUSHCodeParser parser, string? error)
		=> error is null ? CallState.Empty : await InputError(parser, error);

	private async ValueTask<Option<CallState>> InputError(IMUSHCodeParser parser, string error)
	{
		if (parser.CurrentState.Handle is { } handle) await NotifyService.Notify(handle, error);
		return new CallState(error);
	}
}
