using System.Collections.Immutable;
using System.Text.Json;
using MarkupString;
using MarkupString.Layout;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>The attribute a player's <c>@theme</c> is kept in.</summary>
	public const string ThemeAttribute = "THEME";

	/// <summary>The switches that change the game's themes rather than a player's.</summary>
	private static readonly string[] ThemeAdminSwitches = ["ADD", "REMOVE", "DISABLE", "ENABLE"];

	/// <summary>
	/// <c>@theme[/light|/dark] &lt;object&gt;=[&lt;theme&gt;]</c> — the theme every layout is drawn in for a
	/// player, over the game's own and under one a layout names itself. Set on a parent or the player
	/// ancestor, it is inherited. <c>/light</c> and <c>/dark</c> make it for that background. Empty clears it.
	/// <c>/refresh [&lt;player&gt;]</c> works a player's theme out again. <c>/list</c> shows the game's themes;
	/// <c>/add</c>, <c>/remove</c>, <c>/disable</c> and <c>/enable</c> change them (<c>layout.admin</c>).
	/// </summary>
	[SharpCommand(Name = "@THEME", Switches = ["LIGHT", "DARK", "REFRESH", "LIST", "ADD", "REMOVE", "DISABLE", "ENABLE"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.RSBrace, MinArgs = 0, MaxArgs = 2, ParameterNames = ["object", "theme"])]
	public async ValueTask<Option<CallState>> Theme(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;
		var left = args.TryGetValue("0", out var first) ? first.Message!.ToPlainText().Trim() : string.Empty;
		var spec = args.TryGetValue("1", out var given) ? given.Message!.ToPlainText().Trim() : string.Empty;

		if (switches.Contains("LIST")) return await ThemeListAsync(executor);
		if (switches.FirstOrDefault(ThemeAdminSwitches.Contains) is { } change)
			return await ThemeChangeAsync(parser, executor, change, left, spec);
		if (switches.Contains("REFRESH")) return await ThemeRefreshAsync(parser, executor, left);
		if (left.Length == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ThemeUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArgument);
		}

		var mode = switches.Contains("LIGHT") ? "light" : switches.Contains("DARK") ? "dark" : null;
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, left, LocateFlags.All,
			async target =>
			{
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
					// Removed rather than set empty, so the theme a parent or the ancestor gives shows through.
					await AttributeService.ClearAttributeAsync(executor, target, ThemeAttribute, IAttributeService.AttributePatternMode.Exact);
					await RefreshThemesAfterAsync(parser, target);
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ThemeCleared), executor);
					return CallState.Empty;
				}

				// A mode is added to the theme the text works out to; braces keep the JSON whole when it is evaluated.
				var stored = mode is null ? spec : $"{{{InMode(await WorkedOutAsync(parser, target, spec), mode)}}}";
				if (ReadTheme(await WorkedOutAsync(parser, target, stored)) is Error<string> error)
				{
					await NotifyService.Notify(executor, error.Value, executor);
					return new CallState(error.Value);
				}

				await AttributeService.SetAttributeAsync(executor, target, ThemeAttribute, MarkupText.Plain(stored));
				await RefreshThemesAfterAsync(parser, target);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ThemeSet), executor);
				return CallState.Empty;
			});
	}

	/// <summary><c>@theme/list</c>: every theme the game knows, whether it is offered, and an added one's JSON.</summary>
	private async ValueTask<Option<CallState>> ThemeListAsync(AnySharpObject executor)
	{
		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Theme")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Kind")) { Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("Status")) { Wrap = false },
			],
			LayoutThemeService.List().Select(theme => new[]
			{
				theme.Name,
				theme.BuiltIn ? "built-in" : "added",
				theme.Disabled ? "disabled" : "offered",
			}));
		var output = ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Layout themes"), table), 78);
		await NotifyService.Notify(executor, output, executor);
		return new CallState(string.Join(' ', LayoutThemeService.Names));
	}

	/// <summary><c>@theme/add</c>, <c>/remove</c>, <c>/disable</c> and <c>/enable</c>: the game's own themes.</summary>
	private async ValueTask<Option<CallState>> ThemeChangeAsync(IMUSHCodeParser parser, AnySharpObject executor,
		string change, string name, string definition)
	{
		if (!await executor.Can(PortalPermission.LayoutAdmin))
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.PermissionDenied,
				shouldNotify: true);
		}

		if (name.Length == 0 || (change == "ADD" && definition.Length == 0))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ThemeAdminUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArgument);
		}

		var outcome = change switch
		{
			// Worked out as the executor, as a THEME is: braces keep JSON whole, and code can make the theme.
			"ADD" => await LayoutThemeService.AddAsync(name, await WorkedOutAsync(parser, executor, definition)) switch
			{
				LayoutThemeEntry added => (FoundResult<string>)added.Name,
				Error<string> error => error,
			},
			"REMOVE" => Named(await LayoutThemeService.RemoveAsync(name), name),
			_ => Named(await LayoutThemeService.SetDisabledAsync(name, change == "DISABLE"), name),
		};

		switch (outcome)
		{
			case string theme:
				await NotifyService.NotifyLocalized(executor, change switch
				{
					"ADD" => nameof(ErrorMessages.Notifications.ThemeAddedFormat),
					"REMOVE" => nameof(ErrorMessages.Notifications.ThemeRemovedFormat),
					"DISABLE" => nameof(ErrorMessages.Notifications.ThemeDisabledFormat),
					_ => nameof(ErrorMessages.Notifications.ThemeEnabledFormat),
				}, executor, theme);
				// Whoever reads in a theme that changed, went or came back sees it now.
				await RefreshConnectedThemesAsync(parser);
				return new CallState(theme);
			case NotFound:
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ThemeNotFoundFormat), executor, name.ToLowerInvariant());
				return new CallState(LayoutThemes.Unknown);
			case Error<string> error:
				await NotifyService.Notify(executor, error.Value, executor);
				return new CallState(error.Value);
		}

		return CallState.Empty;
	}

	private static FoundResult<string> Named(FoundResult<Success> result, string name) => result switch
	{
		Success => name.Trim().ToLowerInvariant(),
		NotFound notFound => notFound,
		Error<string> error => error,
	};

	/// <summary><c>@theme/refresh [&lt;player&gt;]</c>: works the player's theme out again, sends it to their connections, and says what it is.</summary>
	private async ValueTask<Option<CallState>> ThemeRefreshAsync(IMUSHCodeParser parser, AnySharpObject executor, string who) =>
		await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, who.Length == 0 ? "me" : who, LocateFlags.All,
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

				var theme = await RefreshThemeAsync(parser, target);
				if (theme is Error<string> error)
				{
					await NotifyService.Notify(executor, error.Value, executor);
					return new CallState(error.Value);
				}

				if (theme is string spec)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ThemeInUseFormat), executor, spec);
					return new CallState(spec);
				}
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ThemeNoneInUse), executor);
				return CallState.Empty;
			});

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

	/// <summary>What THEME text <paramref name="text"/> works out to, evaluated with <paramref name="player"/> as <c>%#</c> and <c>%!</c>.</summary>
	private static async ValueTask<string> WorkedOutAsync(IMUSHCodeParser parser, AnySharpObject player, string text)
	{
		text = text.Trim();
		var dbref = player.Object().DBRef;
		var result = await parser.With(state => state with { Executor = dbref, Enactor = dbref, Caller = dbref },
			async evaluating => await evaluating.FunctionParse(MarkupText.Plain(text)));
		return result?.Message?.ToPlainText().Trim() ?? string.Empty;
	}

	/// <summary><paramref name="spec"/> with the game's added themes written out, if it reads; why not, if not.</summary>
	private Result<string> ReadTheme(string spec) => LayoutThemeService.Resolve(spec) switch
	{
		string resolved => LayoutThemes.Read(resolved) switch
		{
			ThemePalette => resolved,
			Error<string> error => error,
		},
		Error<string> error => error,
	};

	/// <summary>
	/// The theme <paramref name="player"/> reads in: their THEME, or the nearest parent's or their type ancestor's,
	/// worked out as them, with the game's added themes written out. <see cref="NotFound"/> when none is set or it
	/// works out to nothing; the error when it does not read.
	/// </summary>
	private async ValueTask<FoundResult<string>> PlayerThemeAsync(IMUSHCodeParser parser, AnySharpObject player)
	{
		// Read as God: a look format inherits whoever may read it, and so does this.
		var reader = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1))) is AnySharpObject god ? god : player;
		if (await AttributeService.GetAttributeAsync(reader, player, ThemeAttribute, IAttributeService.AttributeMode.Read, parent: true)
				is not SharpAttribute[] { Length: > 0 } attributes
			|| attributes[^1].Value.ToPlainText().Trim() is not { Length: > 0 } text)
		{
			return new NotFound();
		}

		var worked = await WorkedOutAsync(parser, player, text);
		if (worked.Length == 0) return new NotFound();
		return ReadTheme(worked) switch
		{
			string spec => spec,
			Error<string> error => error,
		};
	}

	/// <summary>Works <paramref name="player"/>'s theme out and sends it to each of their connections, none when it does not read.</summary>
	private async ValueTask<FoundResult<string>> RefreshThemeAsync(IMUSHCodeParser parser, AnySharpObject player)
	{
		var theme = await PlayerThemeAsync(parser, player);
		await foreach (var connection in ConnectionService.Get(player.Object().DBRef))
			await MessageBus.Publish(new UpdateThemeMessage(connection.Handle, theme is string spec ? spec : null));
		return theme;
	}

	/// <summary>
	/// After <paramref name="changed"/>'s THEME changed: that player's theme, or for any other object (a parent, the
	/// ancestor) every connected player's, since any of them may inherit it.
	/// </summary>
	private async ValueTask RefreshThemesAfterAsync(IMUSHCodeParser parser, AnySharpObject changed)
	{
		if (changed.IsPlayer)
		{
			await RefreshThemeAsync(parser, changed);
			return;
		}
		await RefreshConnectedThemesAsync(parser);
	}

	/// <summary>Every connected player's theme, worked out again; a player whose theme stopped reading is told why.</summary>
	private async ValueTask RefreshConnectedThemesAsync(IMUSHCodeParser parser)
	{
		var players = await ConnectionService.GetAll()
			.Where(connection => connection.Ref is not null)
			.Select(connection => connection.Ref!.Value)
			.Distinct()
			.ToArrayAsync();
		foreach (var dbref in players)
		{
			if (await Mediator.Send(new GetObjectNodeQuery(dbref)) is not AnySharpObject player) continue;
			if (await RefreshThemeAsync(parser, player) is Error<string> error)
				await NotifyService.NotifyLocalized(player, nameof(ErrorMessages.Notifications.ThemeUnreadableFormat), null, error.Value);
		}
	}
}
