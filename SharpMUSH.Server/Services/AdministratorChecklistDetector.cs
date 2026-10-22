using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// The administrator checklist steps the game itself shows are done, so the checklist can tick them: an
/// introduction is saved, the administrator's account has a wizard character other than #1, a channel
/// exists, <c>@backup/list</c> has a copy, and the room new players arrive in has a description. Only the
/// steps asked about are looked at.
/// </summary>
public sealed class AdministratorChecklistDetector(IMediator mediator, IOptionsWrapper<SharpMUSHOptions> options,
	IWorldBackupService backups, IAttributeService attributes, IAccountService accounts)
{
	public async Task<IReadOnlyList<string>> DetectAsync(string accountId, IReadOnlyCollection<string> steps, CancellationToken ct)
	{
		var done = new List<string>();
		foreach (var step in steps)
			if (await DoneAsync(accountId, step, ct)) done.Add(step);
		return done;
	}

	private ValueTask<bool> DoneAsync(string accountId, string step, CancellationToken ct) => step switch
	{
		"identity" => ValueTask.FromResult(!string.IsNullOrWhiteSpace(options.CurrentValue.Onboarding.Introduction)),
		"staff-character" => StaffWizardAsync(accountId, ct),
		"channels" => mediator.CreateStream(new GetChannelListQuery()).AnyAsync(ct),
		"backups" => ValueTask.FromResult(backups.List().Count > 0),
		"start-room" => StartRoomDescribedAsync(ct),
		_ => ValueTask.FromResult(false)
	};

	/// <summary>
	/// A wizard on the administrator's own account that is not God. Seeded objects such as the package manager
	/// are wizards too, but belong to no account.
	/// </summary>
	private async ValueTask<bool> StaffWizardAsync(string accountId, CancellationToken ct)
		=> await (await accounts.GetCharactersAsync(accountId, ct)).ToAsyncEnumerable()
			.AnyAsync(async (player, token) => new AnySharpObject(player) is var character
				&& !character.IsGod() && await character.IsWizard(token), ct);

	private async ValueTask<bool> StartRoomDescribedAsync(CancellationToken ct)
	{
		if (await mediator.Send(new GetObjectNodeQuery(new DBRef((int)options.CurrentValue.Database.PlayerStart)), ct)
			is not AnySharpObject room) return false;
		return await attributes.GetAttributeAsync(room, room, "DESCRIBE", IAttributeService.AttributeMode.Read, parent: false)
			is SharpAttribute[] { Length: > 0 } chain && !string.IsNullOrWhiteSpace(chain[^1].Value.ToPlainText());
	}
}
