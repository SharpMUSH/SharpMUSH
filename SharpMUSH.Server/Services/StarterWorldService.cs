using System.Text.RegularExpressions;
using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
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
/// in (<c>player_start</c>) gets a description and an exit, <c>Out</c>, to a second room with an exit back, and a
/// Newbie channel opens for questions. Every part goes through the game's own commands as God, and each is left out
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
		+ "@channel/on Newbie to ask other players a question. Out leads further into the game.";

	public const string OutsideName = "Outside";

	public const string OutsideDescription =
		"The first place beyond the arrival room, waiting for this game's staff to name and describe it. In leads back.";

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

		if (await attributes.GetAttributeAsync(arrival, arrival, "DESCRIBE", IAttributeService.AttributeMode.Read, parent: false)
			is not SharpAttribute[] { Length: > 0 }
			&& await attributes.SetAttributeAsync(god, arrival, "DESCRIBE", MarkupText.Plain(ArrivalDescription)) is Error<string>)
			failures.Add("The arrival room could not be described.");

		if (await mediator.CreateStream(new GetExitsQuery(arrival.Object().DBRef)).AnyAsync(ct)) return;
		var arrivalRef = $"#{arrival.Object().DBRef.Number}";
		if (Made(await RunAsync("@DIG", OutsideName)) is not { } outside)
		{
			failures.Add("The room beyond the arrival room could not be dug.");
			return;
		}
		if (Failed(await RunAsync("@OPEN", "Out;o", outside, null, arrivalRef))
			|| Failed(await RunAsync("@OPEN", "In;i", arrivalRef, null, outside)))
			failures.Add("The exits between the arrival room and Outside could not all be opened.");
		if (await mediator.Send(new GetObjectNodeQuery(new DBRef(int.Parse(outside[1..], System.Globalization.CultureInfo.InvariantCulture))), ct) is AnySharpObject made
			&& await attributes.SetAttributeAsync(god, made, "DESCRIBE", MarkupText.Plain(OutsideDescription)) is Error<string>)
			failures.Add("Outside could not be described.");
	}

	private async Task ChannelAsync(List<string> failures, CancellationToken ct)
	{
		if (await mediator.Send(new GetChannelQuery(Channel), ct) is not null) return;
		if (await RunAsync("@CHANNEL", ["ADD"], Channel, "player") != ErrorMessages.Notifications.ChatChannelCreated)
		{
			failures.Add("The Newbie channel could not be made.");
			return;
		}
		await RunAsync("@CHANNEL", ["DESCRIBE"], Channel, ChannelDescription);
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
