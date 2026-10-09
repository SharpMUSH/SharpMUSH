using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using MarkupString.Layout;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// <c>@ascii</c>: the <c>ascii_translations</c> table, what a client without Unicode is sent for a character.
	/// With no argument it lists the table; <c>&lt;character&gt;=&lt;text&gt;</c> sets one entry, replacing any the
	/// character had; <c>/remove &lt;character&gt;</c> takes one out. The same table the portal's ASCII
	/// Translations page edits. Guarded the way <c>@config/set</c> is, in the command itself, so a
	/// <c>@command/restrict</c> or a clone of it never opens the table to anyone without <c>config.admin</c>.
	/// </summary>
	[SharpCommand(Name = "@ASCII", Switches = ["LIST", "REMOVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		CommandLock = "PERM^config.admin", MinArgs = 0, MaxArgs = 2, ParameterNames = ["character", "text"])]
	public async ValueTask<Option<CallState>> AsciiTranslation(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (!await executor.Can(PortalPermission.ConfigAdmin))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigCantRemakeWorld), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var character = (args.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "").Trim();

		if (switches.Contains("LIST") || (character.Length == 0 && switches.Length == 0))
		{
			await NotifyService.Notify(executor, AsciiTranslationListing((await ConfigWriter.CurrentAsync()).AsciiTranslations.Translations),
				executor);
			return CallState.Empty;
		}

		if (character.Length == 0 || (switches.Contains("REMOVE") && args.ContainsKey("1")) || (!switches.Contains("REMOVE") && !args.ContainsKey("1")))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AsciiTranslationUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		if (!AsciiTranslations.IsCharacter(character))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AsciiTranslationBadCharacterFormat), executor,
				character);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		if (switches.Contains("REMOVE"))
		{
			var removed = false;
			await ConfigWriter.UpdateAsync(current =>
			{
				var table = new Dictionary<string, string>(current.AsciiTranslations.Translations);
				removed = table.Remove(character);
				return removed ? current with { AsciiTranslations = new AsciiTranslationsOptions(table) } : current;
			});

			if (!removed)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AsciiTranslationNotFoundFormat), executor,
					character);
				return new CallState(ErrorMessages.Returns.NoMatch);
			}

			await Audit.RecordAsync(executor, AuditActions.ConfigSet, AuditTargets.Of(AuditTargetKinds.Setting, "ascii_translations"),
				$"remove {character}");
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AsciiTranslationRemovedFormat), executor, character);
			return CallState.Empty;
		}

		var text = args["1"].Message?.ToPlainText() ?? "";
		if (!AsciiTranslations.IsText(text))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AsciiTranslationBadText), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		await ConfigWriter.UpdateAsync(current =>
			current.AsciiTranslations.Translations.TryGetValue(character, out var had) && had == text
				? current
				: current with
				{
					AsciiTranslations = new AsciiTranslationsOptions(
						new Dictionary<string, string>(current.AsciiTranslations.Translations) { [character] = text })
				});
		await Audit.RecordAsync(executor, AuditActions.ConfigSet, AuditTargets.Of(AuditTargetKinds.Setting, "ascii_translations"),
			$"{character}={text}");

		if (text.Length == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AsciiTranslationDroppedFormat), executor, character);
		}
		else
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AsciiTranslationSetFormat), executor, character, text);
		}

		return CallState.Empty;
	}

	/// <summary>The table as a titled listing: each character, its code point (which an ASCII client still reads), and its text.</summary>
	private static MString AsciiTranslationListing(IReadOnlyDictionary<string, string> table)
	{
		Block body = table.Count == 0
			? new TextBlock(MarkupText.Plain("No translations: clients without Unicode get the built-in stand-ins."))
			: ServerLayout.Listing(
				[
					new TableColumn(MarkupText.Plain("Character")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Code point")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Sent as")) { Min = 10 },
				],
				table.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new[]
				{
					pair.Key,
					string.Join(' ', pair.Key.EnumerateRunes().Select(rune => $"U+{rune.Value:X4}")),
					pair.Value.Length == 0 ? "(left out)" : $"\"{pair.Value}\"",
				}));

		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain($"ASCII translations ({table.Count})"), body), 78);
	}
}
