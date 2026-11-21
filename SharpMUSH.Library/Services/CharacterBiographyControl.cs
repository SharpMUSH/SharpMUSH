using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="ICharacterBiographyControl"/>
public sealed class CharacterBiographyControl(
	Lazy<IMediator> mediator,
	Lazy<IPermissionService> permissions,
	Lazy<IAccountService> accounts) : ICharacterBiographyControl
{
	public async ValueTask<bool> MayWriteAsync(string? writerDbref, string title, string slug, CancellationToken ct = default)
	{
		if (!DBRef.TryParse(writerDbref, out var writerRef) || writerRef is not { } reference
				|| await mediator.Value.Send(new GetObjectNodeQuery(reference), ct) is not AnySharpObject writer)
			return false;

		foreach (var character in await CharactersAsync(title, slug, ct))
		{
			if (await SameAccountAsync(writer, character, ct) || await permissions.Value.Controls(writer, new AnySharpObject(character)))
				return true;
		}

		return false;
	}

	/// <summary>
	/// The players whose name slugifies to <paramref name="slug"/>. A slug writes a space as <c>_</c>, so the name
	/// is looked up as titled, as slugged and with its underscores read as spaces.
	/// </summary>
	private async ValueTask<List<SharpPlayer>> CharactersAsync(string title, string slug, CancellationToken ct)
	{
		var found = new List<SharpPlayer>();
		foreach (var name in new[] { title.Trim(), slug, slug.Replace('_', ' ') }.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			await foreach (var player in mediator.Value.CreateStream(new GetPlayerQuery(name)).WithCancellation(ct))
			{
				if (string.Equals(WikiHelpers.Slugify(player.Object.Name), slug, StringComparison.OrdinalIgnoreCase)
						&& found.All(p => p.Object.DBRef != player.Object.DBRef))
					found.Add(player);
			}
		}

		return found;
	}

	private async ValueTask<bool> SameAccountAsync(AnySharpObject writer, SharpPlayer character, CancellationToken ct)
		=> writer.IsPlayer
			&& await accounts.Value.GetAccountForCharacterAsync(writer.Object().DBRef, ct) is { Id: { Length: > 0 } mine }
			&& await accounts.Value.GetAccountForCharacterAsync(character.Object.DBRef, ct) is { Id: { Length: > 0 } theirs }
			&& string.Equals(mine, theirs, StringComparison.Ordinal);
}
