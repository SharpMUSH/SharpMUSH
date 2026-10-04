using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>Renames an object: <c>@name</c> and <c>name()</c>, including a player's alias list.</summary>
public class ObjectNameService(
	IMediator mediator,
	IPermissionService permissionService,
	IValidateService validateService,
	INotifyService notifyService,
	IAttributeService attributeService,
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration)
	: IObjectNameService
{
	public async ValueTask<CallState> SetName(AnySharpObject executor, AnySharpObject obj, MString name, bool notify)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			if (notify)
			{
				await notifyService.Notify(executor, Definitions.ErrorMessages.Notifications.YouDoNotControlThatObject);
			}

			return ErrorMessages.Returns.PermissionDenied;
		}

		if (!await validateService.Valid(IValidateService.ValidationType.Name, name, obj))
		{
			if (notify)
			{
				await notifyService.NotifyLocalized(executor, nameof(Definitions.ErrorMessages.Notifications.CannotNameObjectFormat), executor, name.ToPlainText());
			}

			return ErrorMessages.Returns.PermissionDenied;
		}

		switch (obj)
		{
			case { IsThing: true } or { IsRoom: true }:
				await mediator.Send(new SetNameCommand(obj, name));
				return obj.Object().DBRef;

			case { IsPlayer: true }:
				return await SetPlayerName(executor, obj, name.ToPlainText(), notify);

			default:
				var split = name.Split(";");
				await mediator.Send(new SetNameCommand(obj, split[0]));
				if (split.Length > 1)
				{
					await attributeService.SetAttributeAsync(executor, obj, "ALIAS",
						MarkupText.Join(MarkupText.Plain(";"), split[1..]));
				}

				return obj.Object().DBRef;
		}
	}

	/// <summary>
	/// <c>do_name</c>'s player case (PennMUSH <c>src/set.c:83-100, 127-147</c>) with
	/// <c>ok_object_name</c> (<c>src/predicat.c:766</c>): <c>Name</c>, <c>Name;alias;...</c>, or
	/// <c>Name;</c> to clear the alias; a name in double quotes takes no aliases. The name and every alias
	/// must be names the player could take (<c>ok_player_name</c>), and a setter who is not a wizard may
	/// give no more than <c>max_aliases</c> names in all - counting the name itself, as Penn does. The
	/// aliases are then written to ALIAS like any other ALIAS write, which reports them.
	/// </summary>
	private async ValueTask<CallState> SetPlayerName(AnySharpObject executor, AnySharpObject player, string requested,
		bool notify)
	{
		if (await ParsePlayerName(executor, player, requested) is not { } parsed)
		{
			return await RefusePlayerName(executor, nameof(Definitions.ErrorMessages.Notifications.PlayerNameOrAliasNotAllowed),
				notify);
		}

		if (parsed.TooManyAliases)
		{
			return await RefusePlayerName(executor, nameof(Definitions.ErrorMessages.Notifications.PlayerNameTooManyAliases),
				notify);
		}

		await mediator.Send(new SetNameCommand(player, MarkupText.Plain(parsed.Name)));

		if (parsed.AliasList is { Length: 0 })
		{
			await attributeService.ClearAttributeAsync(executor, player, PlayerAliases.AttributeName,
				IAttributeService.AttributePatternMode.Exact);
		}
		else if (parsed.AliasList is { } list)
		{
			await attributeService.SetAttributeAsync(executor, player, PlayerAliases.AttributeName, MarkupText.Plain(list));
		}

		return player.Object().DBRef;
	}

	private async ValueTask<CallState> RefusePlayerName(AnySharpObject executor, string key, bool notify)
	{
		if (notify)
		{
			await notifyService.NotifyLocalized(executor, key, executor);
		}

		return ErrorMessages.Returns.BadPlayerName;
	}

	/// <summary>
	/// The name, and the alias list to write: <see langword="null"/> to leave ALIAS alone, empty to clear
	/// it. <see langword="null"/> when the name or an alias is one the player may not take, or an empty
	/// alias is followed by another.
	/// </summary>
	private async ValueTask<ParsedPlayerName?> ParsePlayerName(AnySharpObject executor, AnySharpObject player,
		string requested)
	{
		if (requested.StartsWith('"'))
		{
			var close = requested.IndexOf('"', 1);
			var quoted = close < 0 ? requested[1..] : requested[1..close];
			return await validateService.ValidPlayerName(MarkupText.Plain(quoted), executor, player)
				? new ParsedPlayerName(quoted, null, false)
				: null;
		}

		var parts = requested.Split(PlayerAliases.Delimiter);
		if (!await validateService.ValidPlayerName(MarkupText.Plain(parts[0]), executor, player))
		{
			return null;
		}

		if (parts.Length == 1)
		{
			return new ParsedPlayerName(parts[0], null, false);
		}

		var names = new List<string>();
		var empty = false;
		foreach (var part in parts[1..])
		{
			if (empty)
			{
				return null;
			}

			var entry = part.TrimStart(' ');
			if (entry.Length == 0)
			{
				empty = true;
				continue;
			}

			if (!await validateService.ValidPlayerName(MarkupText.Plain(entry), executor, player))
			{
				return null;
			}

			names.Add(entry);
		}

		var tooMany = 1 + names.Count > configuration.CurrentValue.Limit.MaxAliases && !await executor.IsWizard();
		return new ParsedPlayerName(parts[0], string.Join(PlayerAliases.Delimiter, names), tooMany);
	}

	private sealed record ParsedPlayerName(string Name, string? AliasList, bool TooManyAliases);
}
