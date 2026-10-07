using System.Text.Json;
using MarkupString;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>The attribute a player's <c>@theme</c> is kept in.</summary>
	public const string ThemeAttribute = "THEME";

	/// <summary>
	/// <c>@theme[/light|/dark] &lt;player&gt;=[&lt;theme&gt;]</c> — the theme every layout is drawn in for
	/// that player, over the game's own and under one a layout names itself. <c>/light</c> and <c>/dark</c>
	/// make it for that background. Empty clears it.
	/// </summary>
	[SharpCommand(Name = "@THEME", Switches = ["LIGHT", "DARK"], Behavior = CB.Default | CB.EqSplit | CB.RSNoParse,
		MinArgs = 1, MaxArgs = 2, ParameterNames = ["player", "theme"])]
	public async ValueTask<Option<CallState>> Theme(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;
		var spec = args.TryGetValue("1", out var given) ? given.Message!.ToPlainText().Trim() : string.Empty;
		var mode = switches.Contains("LIGHT") ? "light" : switches.Contains("DARK") ? "dark" : null;

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, args["0"].Message!.ToPlainText(), LocateFlags.All,
			async target =>
			{
				if (!target.IsPlayer)
				{
					await NotifyService.Notify(executor, ErrorMessages.Returns.NotAPlayer, executor);
					return new CallState(ErrorMessages.Returns.NotAPlayer);
				}
				if (!await PermissionService.Controls(executor, target))
				{
					return await NotifyService.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				if (spec.Length == 0)
				{
					await AttributeService.SetAttributeAsync(executor, target, ThemeAttribute, MarkupText.Empty);
					await PublishThemeAsync(target.Object().DBRef, null);
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ThemeCleared), executor);
					return CallState.Empty;
				}

				var stored = mode is null ? spec : InMode(spec, mode);
				if (LayoutThemes.Read(stored) is Error<string> error)
				{
					await NotifyService.Notify(executor, error.Value, executor);
					return new CallState(error.Value);
				}

				await AttributeService.SetAttributeAsync(executor, target, ThemeAttribute, MarkupText.Plain(stored));
				await PublishThemeAsync(target.Object().DBRef, stored);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ThemeSet), executor);
				return CallState.Empty;
			});
	}

	/// <summary><paramref name="spec"/> for a <paramref name="mode"/> background: <c>{"preset":&lt;spec&gt;,"mode":"light"}</c>.</summary>
	private static string InMode(string spec, string mode)
	{
		string value;
		try
		{
			using var document = JsonDocument.Parse(spec);
			value = document.RootElement.GetRawText();
		}
		catch (JsonException)
		{
			value = JsonSerializer.Serialize(spec);
		}
		return $"{{\"preset\":{value},\"mode\":\"{mode}\"}}";
	}

	/// <summary>Tells each of <paramref name="player"/>'s connections its theme, null for none.</summary>
	private async ValueTask PublishThemeAsync(DBRef player, string? theme)
	{
		await foreach (var connection in ConnectionService.Get(player))
			await MessageBus.Publish(new UpdateThemeMessage(connection.Handle, theme));
	}

	/// <summary>A player's <c>@theme</c>, or null when it has none.</summary>
	private async ValueTask<string?> ThemeOf(AnySharpObject player) =>
		await AttributeService.GetAttributeAsync(player, player, ThemeAttribute, IAttributeService.AttributeMode.Read, parent: false)
			is SharpAttribute[] { Length: > 0 } attributes && attributes[^1].Value.ToPlainText().Trim() is { Length: > 0 } theme
			? theme
			: null;
}
