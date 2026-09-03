using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Guest-character administration, acting as the authenticated session's character.
///
/// <para>A game ships with <c>Net.Guests</c> on and no guest characters, because the seeded database
/// defines the <c>Guest</c> POWER but gives it to nobody. The portal offers "Play" to anonymous
/// visitors on that setting alone, so every visitor of a fresh game reaches <c>/play</c>, watches it
/// run <c>connect guest</c>, and is told there are no guest characters available. Stocking them was
/// possible only from a MU* client (<c>@pcreate</c> then <c>@power &lt;name&gt;=Guest</c>) — a
/// terminal round-trip for the one thing an operator has to do before anyone can look at their game.
/// </para>
///
/// <para>Work goes through the engine's own commands via <see cref="IEngineCommandInvoker"/>, never
/// straight to the database: <c>@PCREATE</c> validates the name, applies the starting quota and
/// fires <c>PLAYER`CREATE</c>; <c>@NUKE</c> runs the whole destruction path including its refusal to
/// destroy a connected player. Arguments are passed structurally, so a name is never spliced into a
/// command line.</para>
///
/// Routes:
///   GET    api/admin/guests          — roster, plus why guest login may still be refused
///   POST   api/admin/guests          — create one  { name }
///   DELETE api/admin/guests/{dbref}  — destroy one
/// </summary>
[ApiController]
[Route("api/admin/guests")]
[Authorize]
public class AdminGuestsController(
	IMediator mediator,
	IEngineCommandInvoker commandInvoker,
	IConnectionService connectionService,
	IOptionsWrapper<SharpMUSHOptions> configuration,
	IPasswordService passwordService) : ControllerBase
{
	/// <param name="InUse">
	/// Whether someone is connected as this guest right now. Reported because it is the one reason a
	/// present, correctly-powered guest still cannot be handed out — and because <c>@NUKE</c> refuses
	/// to destroy a connected player, so a delete button on this row would fail.
	/// </param>
	public record GuestRow(int DbrefNumber, long CreationTime, string Name, bool InUse);

	/// <param name="GuestLoginsEnabled"><c>Net.Guests</c>. Off means no roster size helps.</param>
	/// <param name="MaxGuests"><c>Limit.MaxGuests</c>; -1 for no cap.</param>
	/// <param name="NextFreeName">A <c>GuestN</c> name not currently taken, to pre-fill the form.</param>
	public record GuestListResponse(
		bool GuestLoginsEnabled,
		int MaxGuests,
		string NextFreeName,
		IReadOnlyList<GuestRow> Guests);

	public record CreateGuestRequest(string? Name);

	/// <summary>Prefix the suggested names use, matching the convention `connect guest` documents.</summary>
	private const string GuestNamePrefix = "Guest";

	/// <summary>
	/// Ceiling on the search for a free <c>GuestN</c>. Far past any plausible roster; it exists so a
	/// database in a strange state cannot spin this endpoint forever.
	/// </summary>
	private const int MaxSuggestionScan = 1000;

	[HttpGet]
	public async Task<IActionResult> List(CancellationToken ct)
	{
		if (await RequireWizardAsync(ct) is { } failure) return failure;

		var net = configuration.CurrentValue.Net;
		var limits = configuration.CurrentValue.Limit;

		var rows = new List<GuestRow>();
		await foreach (var guest in GuestCharacters.AllAsync(mediator).WithCancellation(ct))
		{
			rows.Add(new GuestRow(
				guest.Object.Key,
				guest.Object.CreationTime,
				guest.Object.Name,
				await IsInUseAsync(new DBRef(guest.Object.Key, guest.Object.CreationTime), ct)));
		}

		return Ok(new GuestListResponse(
			net.Guests,
			(int)limits.MaxGuests,
			await NextFreeGuestNameAsync(ct),
			rows));
	}

	[HttpPost]
	public async Task<IActionResult> Create([FromBody] CreateGuestRequest request, CancellationToken ct)
	{
		if (await ResolveExecutorAsync(ct) is not { } executor) return Unauthorized();
		if (!await executor.IsWizard())
			return StatusCode(StatusCodes.Status403Forbidden,
				new ApiErrorDto("Only a wizard may create guest characters."));

		var name = string.IsNullOrWhiteSpace(request.Name)
			? await NextFreeGuestNameAsync(ct)
			: request.Name.Trim();

		// Checked here as well as in @PCREATE so the panel can answer with the reason. The command
		// reports its own refusals by notifying the executing character, which is a terminal this
		// caller is not reading — without this, a duplicate name would surface as a bare failure.
		if (await mediator.CreateStream(new GetPlayerQuery(name)).AnyAsync(ct))
			return Conflict(new ApiErrorDto($"'{name}' is already taken."));

		// A guest never authenticates by password — HandleGuestLogin selects the character directly —
		// but @PCREATE requires one, so it gets a random one that is never shown or stored elsewhere.
		var created = await commandInvoker.InvokeAsync("@PCREATE", executor.Object().DBRef,
			new Dictionary<string, CallState>
			{
				["0"] = new(name),
				["1"] = new(passwordService.GenerateRandomPassword())
			});

		// @PCREATE answers with the new dbref on success and an empty CallState on refusal.
		if (created?.Message?.ToPlainText() is not { Length: > 0 } dbrefText
			|| !DBRef.TryParse(dbrefText, out var parsed) || parsed is not { } dbref)
		{
			return StatusCode(StatusCodes.Status500InternalServerError,
				new ApiErrorDto($"The game refused to create '{name}'."));
		}

		await commandInvoker.InvokeAsync("@POWER", executor.Object().DBRef,
			new Dictionary<string, CallState>
			{
				["0"] = new(dbref.ToString()),
				["1"] = new(GuestCharacters.GuestPower)
			});

		// @POWER reports success and failure the same way (an empty CallState plus a notification),
		// so the grant is confirmed by reading it back. A player created without the power is worse
		// than no player at all: it looks like a guest in every listing and `connect guest` ignores it.
		var node = await mediator.Send(new GetObjectNodeQuery(dbref), ct);
		if (node.IsNone || !await GuestCharacters.IsGuestAsync(node.AsPlayer))
		{
			return StatusCode(StatusCodes.Status500InternalServerError,
				new ApiErrorDto($"'{name}' was created but the {GuestCharacters.GuestPower} power did not take."));
		}

		// The creation time comes off the node rather than the parsed dbref. @PCREATE does return an
		// objid today, so `dbref.CreationMilliseconds ?? 0` produced the right answer — but the
		// sentinel is a silently wrong one, and a 0 here would have the panel build `#N:0` for a guest
		// that does exist. The node is already loaded and is the authoritative value, which is also
		// what List reports.
		var player = node.AsPlayer;
		return Ok(new GuestRow(player.Object.Key, player.Object.CreationTime, name,
			await IsInUseAsync(dbref, ct)));
	}

	[HttpDelete("{dbref:int}")]
	public async Task<IActionResult> Delete(int dbref, CancellationToken ct)
	{
		if (await ResolveExecutorAsync(ct) is not { } executor) return Unauthorized();
		if (!await executor.IsWizard())
			return StatusCode(StatusCodes.Status403Forbidden,
				new ApiErrorDto("Only a wizard may remove guest characters."));

		var node = await mediator.Send(new GetObjectNodeQuery(new DBRef(dbref)), ct);
		if (node.IsNone || !node.Known.IsPlayer) return NotFound();

		var player = node.AsPlayer;
		// Scoped to guests deliberately. This endpoint exists to manage the guest roster; letting it
		// delete arbitrary players would make it a second, less-guarded @nuke reachable over HTTP.
		if (!await GuestCharacters.IsGuestAsync(player))
			return StatusCode(StatusCodes.Status403Forbidden,
				new ApiErrorDto($"#{dbref} is not a guest character."));

		var target = new DBRef(player.Object.Key, player.Object.CreationTime);

		// PennMUSH destroys in two passes (src/destroy.c): the first marks the object GOING, and only
		// a second run against a GOING object calls free_object(). One pass would leave the guest in
		// the database still carrying its power, so `connect guest` would go on handing it out while
		// the panel claimed it was gone.
		if (await NukeOnceAsync(executor, target) is { } refusal)
			return StatusCode(StatusCodes.Status403Forbidden, new ApiErrorDto(refusal));
		if (await NukeOnceAsync(executor, target) is { } secondRefusal)
			return StatusCode(StatusCodes.Status403Forbidden, new ApiErrorDto(secondRefusal));

		return NoContent();
	}

	/// <summary>Runs <c>@NUKE</c> once; returns the refusal message, or null when it was accepted.</summary>
	private async Task<string?> NukeOnceAsync(AnySharpObject executor, DBRef target)
	{
		var result = await commandInvoker.InvokeAsync("@NUKE", executor.Object().DBRef,
			new Dictionary<string, CallState> { ["0"] = new(target.ToString()) });

		var message = result?.Message?.ToPlainText();
		return message is not null && message.StartsWith("#-1", StringComparison.Ordinal) ? message : null;
	}

	/// <summary>
	/// The lowest <c>GuestN</c> no player currently holds. Suggested rather than imposed: an operator
	/// who wants themed guest names types their own.
	/// </summary>
	private async Task<string> NextFreeGuestNameAsync(CancellationToken ct)
	{
		for (var n = 1; n <= MaxSuggestionScan; n++)
		{
			var candidate = $"{GuestNamePrefix}{n}";
			if (!await mediator.CreateStream(new GetPlayerQuery(candidate)).AnyAsync(ct))
				return candidate;
		}

		return $"{GuestNamePrefix}{Guid.NewGuid():N}"[..12];
	}

	private async Task<bool> IsInUseAsync(DBRef guest, CancellationToken ct)
		=> await connectionService.Get(guest)
			.AnyAsync(c => c.State == IConnectionService.ConnectionState.LoggedIn, ct);

	/// <summary>
	/// The character this request acts as — the <c>character_dbref</c> claim, same rule as
	/// <c>ObjectsController</c> and <c>MailController</c>.
	/// </summary>
	private async Task<AnySharpObject?> ResolveExecutorAsync(CancellationToken ct)
	{
		if (User.GetActingCharacter() is not { } character) return null;

		var result = await mediator.Send(new GetObjectNodeQuery(character), ct);
		return result.IsNone ? null : result.Known;
	}

	/// <summary>
	/// Reading the roster is wizard-only too: it names every character the game will hand to an
	/// anonymous visitor, which is not something an ordinary player has any business enumerating.
	/// </summary>
	private async Task<IActionResult?> RequireWizardAsync(CancellationToken ct)
	{
		if (await ResolveExecutorAsync(ct) is not { } executor) return Unauthorized();

		return await executor.IsWizard()
			? null
			: StatusCode(StatusCodes.Status403Forbidden,
				new ApiErrorDto("Only a wizard may view the guest roster."));
	}
}
