using System.Text.RegularExpressions;
using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>Persisted record that a game's starter world was offered and applied (expanded server data).</summary>
public sealed class StarterWorldState
{
	public bool Applied { get; set; }
}

/// <summary>
/// What a new world has to show its first player, made once from the setup wizard: the room new characters arrive
/// in (<c>player_start</c>) gets a description and an exit, <c>Tutorial</c>, to a room that shows the basics, whose
/// <c>Out</c> leads back to it, and a Newbie channel opens for questions. Two zones split the world: <c>OOC</c> holds the
/// arrival and tutorial rooms, <c>IC</c> the first in-character room, which <c>In Character</c> leads to from the
/// arrival room for approved characters and staff only. Each zone is a World map with a legend for its rooms. Every part goes through the game's own commands as God, and each is left out
/// when the game already has it: an arrival room with a description or an exit, or a channel named Newbie. An
/// imported world is therefore left as it was.
/// </summary>
public sealed partial class StarterWorldService(
	IMediator mediator,
	IOptionsWrapper<SharpMUSHOptions> options,
	IAttributeService attributes,
	IEngineCommandInvoker commands,
	IExpandedObjectDataService serverData)
{
	public const string ArrivalDescription =
		"New characters arrive here. Type look to see this room again, help for the list of commands, and "
		+ "@channel/on Newbie to ask other players a question. Tutorial leads to a room that shows the basics.";

	public const string TutorialName = "Tutorial";

	public const string TutorialDescription =
		"The basics, one command at a time. Type look to read this again. say Hello (or \"Hello) speaks to the room, "
		+ "and pose waves (or :waves) acts. page <name>=<message> reaches someone anywhere in the game. help <topic> "
		+ "explains any command. Out leads back to the arrival room.";

	public const string OocZone = "OOC";

	public const string IcZone = "IC";

	public const string CommonsName = "Commons";

	public const string CommonsDescription =
		"The first in-character room. Staff describe it and build the game's world out from here. Out leads back to the "
		+ "arrival room.";

	/// <summary>What a character who may not go in character yet is told at the <c>In Character</c> exit.</summary>
	public const string CommonsRefusal = "Only approved characters and staff may go in character.";

	public const string Channel = "Newbie";

	public const string ChannelDescription = "Questions and help for new players. Anyone may ask.";

	private static readonly DBRef God = new(1);

	private readonly SemaphoreSlim _applying = new(1, 1);

	public async Task<bool> AppliedAsync()
		=> (await serverData.GetExpandedServerDataAsync<StarterWorldState>())?.Applied ?? false;

	/// <summary>
	/// Makes what the game does not have yet and records the starter world as applied, even when a part failed, naming
	/// that part. Once applied, it makes nothing again.
	/// </summary>
	public async Task<Result<Success>> ApplyAsync(CancellationToken ct)
	{
		await _applying.WaitAsync(ct);
		try
		{
			if (await AppliedAsync()) return new Success();
			var failures = new List<string>();
			await ArrivalRoomAsync(failures, ct);
			await ChannelAsync(failures, ct);
			await serverData.SetExpandedServerDataAsync(new StarterWorldState { Applied = true });
			return failures.Count == 0 ? new Success() : new Error<string>(string.Join(" ", failures));
		}
		finally
		{
			_applying.Release();
		}
	}

	private async Task ArrivalRoomAsync(List<string> failures, CancellationToken ct)
	{
		var start = new DBRef((int)options.CurrentValue.Database.PlayerStart);
		if (await mediator.Send(new GetObjectNodeQuery(start), ct) is not AnySharpObject { IsRoom: true } arrival
			|| await mediator.Send(new GetObjectNodeQuery(God), ct) is not AnySharpObject god)
		{
			failures.Add("The arrival room could not be found.");
			return;
		}

		switch (await attributes.GetAttributeAsync(god, arrival, "DESCRIBE", IAttributeService.AttributeMode.Read, parent: true))
		{
			case Error<string>:
				failures.Add("The arrival room's description could not be read.");
				break;
			case SharpAttribute[] { Length: > 0 }:
				break;
			default:
				if (await attributes.SetAttributeAsync(god, arrival, "DESCRIBE", MarkupText.Plain(ArrivalDescription)) is Error<string>)
					failures.Add("The arrival room could not be described.");
				break;
		}

		if (await mediator.CreateStream(new GetExitsQuery(arrival.Object().DBRef)).AnyAsync(ct)) return;
		var arrivalRef = $"#{arrival.Object().DBRef.Number}";
		if (Made(await RunAsync("@DIG", TutorialName)) is not { } tutorial)
		{
			failures.Add("The tutorial room could not be dug.");
			return;
		}
		// Out always leads back to the arrival room, the hub; i is left alone, since it is inventory.
		var toFailed = Failed(await RunAsync("@OPEN", "Tutorial;tut", tutorial, null, arrivalRef));
		var outFailed = Failed(await RunAsync("@OPEN", "Out;o", arrivalRef, null, tutorial));
		if (toFailed || outFailed)
			failures.Add("The exits between the arrival room and the tutorial room could not all be opened.");
		if (!await SetAsync(god, tutorial, "DESCRIBE", TutorialDescription, ct))
			failures.Add("The tutorial room could not be described.");

		failures.AddRange(await ZonesAsync(arrivalRef, tutorial, ct));
	}

	/// <summary>
	/// The OOC and IC zones, each a compass map whose legend draws its rooms, and the Commons, the first IC room: its
	/// <c>In Character</c> exit from <paramref name="arrival"/> passes only a character <c>isapproved()</c> says may play,
	/// and its <c>Out</c> leads back. <paramref name="arrival"/> and <paramref name="tutorial"/> go in the OOC zone.
	/// </summary>
	/// <returns>What could not be made; empty when all of it was.</returns>
	public async Task<IReadOnlyList<string>> ZonesAsync(string arrival, string tutorial, CancellationToken ct)
	{
		var failures = new List<string>();
		if (await mediator.Send(new GetObjectNodeQuery(God), ct) is not AnySharpObject god)
		{
			failures.Add("God could not be found.");
			return failures;
		}
		if (Made(await RunAsync("@CREATE", OocZone)) is not { } ooc || Made(await RunAsync("@CREATE", IcZone)) is not { } ic)
		{
			failures.Add("The OOC and IC zones could not be made.");
			return failures;
		}
		if (Made(await RunAsync("@DIG", CommonsName)) is not { } commons)
		{
			failures.Add("The first in-character room could not be dug.");
			return failures;
		}
		var made = new (string Target, string Attribute, string Value)[]
		{
			(ooc, "MAP`KIND", "compass"), (ooc, "MAP`LEGEND`OOC`LABEL", "Out of character"), (ooc, "MAP`LEGEND`OOC`TONE", "muted"),
			(ooc, "MAP`LEGEND`OOC`ICON", "chat"),
			(ic, "MAP`KIND", "compass"), (ic, "MAP`LEGEND`IC`LABEL", "In character"), (ic, "MAP`LEGEND`IC`TONE", "primary"),
			(ic, "MAP`LEGEND`IC`ICON", "mask"),
			(arrival, "ENVIRONMENT", "ooc"), (tutorial, "ENVIRONMENT", "ooc"), (commons, "ENVIRONMENT", "ic"),
			(commons, "DESCRIBE", CommonsDescription),
		};
		var set = await made.ToAsyncEnumerable().AllAsync(async (entry, token) => await SetAsync(god, entry.Target, entry.Attribute, entry.Value, token), ct);
		var zoned = await new[] { (arrival, ooc), (tutorial, ooc), (commons, ic) }.ToAsyncEnumerable()
			.AllAsync(async (entry, token) => await ZoneAsync(entry.Item1, entry.Item2, token), ct);
		if (!set || !zoned) failures.Add("The OOC and IC zones could not all be set up.");

		if (Made(await RunAsync("@OPEN", "In Character;ic", commons, null, arrival)) is not { } way
			|| Failed(await RunAsync("@OPEN", "Out;o", arrival, null, commons)))
		{
			failures.Add("The exits between the arrival room and the Commons could not all be opened.");
			return failures;
		}
		// The approval rule is isapproved()'s, so the exit agrees with every +-verb that asks it.
		await RunAsync("@LOCK", way, "APPROVED/1");
		if (!await SetAsync(god, way, "APPROVED", "[isapproved(%#)]", ct) || !await SetAsync(god, way, "FAILURE", CommonsRefusal, ct)
			|| await ObjectAsync(way, ct) is not { } exit || !exit.Object().Locks.ContainsKey("Basic"))
			failures.Add("The way to the Commons could not be locked to approved characters.");
		return failures;
	}

	/// <summary>Puts <paramref name="target"/> in <paramref name="zone"/> with <c>@chzone</c>, and says whether it is there.</summary>
	private async Task<bool> ZoneAsync(string target, string zone, CancellationToken ct)
	{
		await RunAsync("@CHZONE", target, zone);
		return await ObjectAsync(target, ct) is { } found
			&& await found.Object().Zone.WithCancellation(ct) is AnySharpObject set
			&& $"#{set.Object().DBRef.Number}" == zone;
	}

	private async Task<AnySharpObject?> ObjectAsync(string dbref, CancellationToken ct)
		=> await mediator.Send(new GetObjectNodeQuery(new DBRef(int.Parse(dbref[1..], System.Globalization.CultureInfo.InvariantCulture))), ct)
			is AnySharpObject found ? found : null;

	/// <summary>Sets <paramref name="attribute"/> on the object <paramref name="target"/> names, as God.</summary>
	private async Task<bool> SetAsync(AnySharpObject god, string target, string attribute, string value, CancellationToken ct)
		=> await ObjectAsync(target, ct) is { } found
			&& await attributes.SetAttributeAsync(god, found, attribute, MarkupText.Plain(value)) is not Error<string>;

	private async Task ChannelAsync(List<string> failures, CancellationToken ct)
	{
		if (await mediator.Send(new GetChannelQuery(Channel), ct) is not null) return;
		if (await RunAsync("@CHANNEL", ["ADD"], Channel, "player") != ErrorMessages.Notifications.ChatChannelCreated)
		{
			failures.Add("The Newbie channel could not be made.");
			return;
		}
		if (await RunAsync("@CHANNEL", ["DESCRIBE"], Channel, ChannelDescription)
			!= string.Format(ErrorMessages.Notifications.ChatChannelDescSet, Channel))
			failures.Add("The Newbie channel could not be described.");
	}

	private Task<string?> RunAsync(string command, params string?[] arguments) => RunAsync(command, [], arguments);

	/// <summary>Runs a command as God with pre-split arguments; a null argument is left out.</summary>
	private async Task<string?> RunAsync(string command, string[] switches, params string?[] arguments)
	{
		var split = new Dictionary<string, CallState>();
		for (var i = 0; i < arguments.Length; i++)
			if (arguments[i] is { } argument) split[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new CallState(argument);
		var result = await commands.InvokeAsync(command, God, split, switches);
		return result?.Message?.ToPlainText().Trim();
	}

	/// <summary>What <c>@dig</c> and <c>@open</c> answer with: a dbref, with the object's creation time after a colon.</summary>
	[GeneratedRegex("^(#[0-9]{1,10})(:[0-9]+)?$")]
	private static partial Regex MadeDbref();

	private static string? Made(string? result)
		=> result is not null && MadeDbref().Match(result) is { Success: true } match ? match.Groups[1].Value : null;

	private static bool Failed(string? result) => result is null || result.StartsWith("#-1", StringComparison.Ordinal);
}
