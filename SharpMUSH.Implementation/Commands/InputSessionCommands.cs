using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
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
	[SharpCommand(Name = "@INPUT", Switches = ["START", "PROMPT", "CANCEL"],
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
		if (switches.Contains("PROMPT"))
			return await InputResult(parser, await sessions.PromptAsync(parser, arguments.GetValueOrDefault("0")?.Message ?? MString.Empty));
		var path = arguments.GetValueOrDefault("0")?.Message?.Text ?? "";
		var separator = path.IndexOf('/');
		if (separator < 1 || separator == path.Length - 1 || !arguments.ContainsKey("1"))
			return await InputError(parser, InputSessionService.InvalidCallback);
		var seconds = 60;
		if (arguments.TryGetValue("2", out var timeout)
			&& (!int.TryParse(timeout.Message?.Text, NumberStyles.None, CultureInfo.InvariantCulture, out seconds) || seconds is < 1 or > 3600))
			return await InputError(parser, InputSessionService.InvalidTimeout);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var target = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, path[..separator], LocateFlags.All);
		if (target.IsError) return target.AsError;
		return await InputResult(parser, await sessions.StartAsync(parser, target.AsSharpObject.Object().DBRef,
			path[(separator + 1)..], arguments["1"].Message ?? MString.Empty, TimeSpan.FromSeconds(seconds)));
	}

	private async ValueTask<Option<CallState>> InputResult(IMUSHCodeParser parser, string? error)
		=> error is null ? CallState.Empty : await InputError(parser, error);

	private async ValueTask<Option<CallState>> InputError(IMUSHCodeParser parser, string error)
	{
		if (parser.CurrentState.Handle is { } handle) await NotifyService.Notify(handle, error);
		return new CallState(error);
	}
}
