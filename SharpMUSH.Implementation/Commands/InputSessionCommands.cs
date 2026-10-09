using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
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
	[SharpCommand(Name = "@INPUT", Switches = ["START", "PROMPT", "CANCEL", "RESCUE"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		SingleArgumentSwitches = ["PROMPT"], MinArgs = 0, MaxArgs = 3, ParameterNames = ["object/attribute", "prompt", "timeout-seconds"])]
	public async ValueTask<Option<CallState>> Input(IMUSHCodeParser parser, SharpCommandAttribute _)
	{
		var sessions = parser.ServiceProvider.GetRequiredService<IInputSessionService>();
		var switches = parser.CurrentState.Switches.ToArray();
		var arguments = parser.CurrentState.ArgumentsOrdered;
		if (switches.Length > 1) return await InputError(parser, "#-1 INPUT ACCEPTS ONE SWITCH");
		if (switches.Contains("CANCEL"))
			return await InputResult(parser, await sessions.CancelAsync(parser));
		if (switches.Contains("RESCUE"))
			return await InputRescue(parser, arguments.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "");
		if (switches.Contains("PROMPT"))
			return await InputResult(parser, await sessions.PromptAsync(parser, arguments.GetValueOrDefault("0")?.Message ?? MString.Empty));
		var path = arguments.GetValueOrDefault("0")?.Message?.Text ?? "";
		var separator = path.IndexOf('/');
		if (separator < 1 || separator == path.Length - 1 || !arguments.TryGetValue("1", out var prompt))
			return await InputError(parser, InputSessionService.InvalidCallback);
		var seconds = 60;
		if (arguments.TryGetValue("2", out var timeout)
			&& (!int.TryParse(timeout.Message?.Text, NumberStyles.None, CultureInfo.InvariantCulture, out seconds) || seconds is < 1 or > 3600))
			return await InputError(parser, InputSessionService.InvalidTimeout);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, path[..separator], LocateFlags.All) switch
		{
			AnySharpObject target => await InputResult(parser, await sessions.StartAsync(parser, target.Object().DBRef,
				path[(separator + 1)..], prompt.Message ?? MString.Empty, TimeSpan.FromSeconds(seconds))),
			Error<CallState> error => error.Value
		};
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
