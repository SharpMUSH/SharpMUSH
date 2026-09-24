using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using static SharpMUSH.Library.Services.Interfaces.LocateFlags;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// PennMUSH's html(), tag() and endtag() write one end of a tag each. Markup here is a span over the
	/// text it covers, which has no half-open form, and a tag written into the text as characters is
	/// escaped like any other &lt; — so all three point at <c>tagwrap()</c>.
	/// </summary>
	[SharpFunction(Name = "html", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly, ParameterNames = ["tag", "text..."])]
	public ValueTask<CallState> HTML(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(ErrorMessages.Returns.UseTagwrapInstead);

	[SharpFunction(Name = "tag", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["tagname", "content", "attributes"])]
	public ValueTask<CallState> Tag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(ErrorMessages.Returns.UseTagwrapInstead);

	[SharpFunction(Name = "endtag", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["tagname"])]
	public ValueTask<CallState> EndTag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(ErrorMessages.Returns.UseTagwrapInstead);

	/// <summary>
	/// <c>tagwrap(&lt;name&gt;[, &lt;parameters&gt;], &lt;string&gt;)</c> — the wrapped STRING is the last
	/// argument, with the optional tag parameters in the middle (<c>help tagwrap</c>, and PennMUSH).
	///
	/// <para>The tag is markup on the string, not text in it, as PennMUSH's is (<c>safe_tag_wrap</c>,
	/// <c>src/markup.c</c>): a Pueblo, MXP or portal client receives it as a tag, every other client the
	/// string alone, and <c>strlen()</c> counts only the string. Written into the text instead, the
	/// renderer escapes it like any other &lt; and every client shows it literally.</para>
	///
	/// <para><see cref="TagwrapPolicy"/> is the gate: without Send_OOB, only PennMUSH's tags and only
	/// parameters a browser cannot be made to run.</para>
	/// </summary>
	[SharpFunction(Name = "tagwrap", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["tag", "parameters", "content"])]
	public async ValueTask<CallState> TagWrap(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var tagName = args["0"].Message!.ToPlainText().Trim();
		var hasParameters = args.Count > 2;
		var content = args[hasParameters ? "2" : "1"].Message!;
		var parameters = hasParameters ? args["1"].Message!.ToPlainText().Trim() : string.Empty;

		if (!HtmlMarkup.IsValidTagName(tagName))
		{
			return new CallState(ErrorMessages.Returns.InvalidTagName);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await CanSendOob(executor))
		{
			return new CallState(MarkupText.Wrap(
				HtmlMarkup.Create(tagName, parameters.Length == 0 ? null : parameters), content));
		}

		return TagwrapPolicy.Wrap(tagName, parameters) is { } markup
			? new CallState(MarkupText.Wrap(markup, content))
			: new CallState(ErrorMessages.Returns.PermissionDenied);
	}

	/// <summary>
	/// <c>cmdlink(&lt;string&gt;, &lt;command&gt;[, &lt;hint&gt;])</c> — SharpMUSH's own: &lt;string&gt;
	/// as a link that runs &lt;command&gt; when clicked. Pueblo and MXP write a command link differently
	/// (<c>&lt;A XCH_CMD&gt;</c> against <c>&lt;SEND HREF&gt;</c>), so softcode cannot write one with
	/// <c>tagwrap()</c> that serves both; this names the link and each client's renderer writes it in
	/// that client's dialect, or as the bare string for a client with none. The hint, the command when
	/// not given, is the tooltip. Gated as PennMUSH gates <c>XCH_CMD</c>: a wizard or Send_OOB.
	/// </summary>
	[SharpFunction(Name = "cmdlink", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "command", "hint"])]
	public async ValueTask<CallState> CmdLink(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var text = args["0"].Message!;
		var command = args["1"].Message!.ToPlainText();
		var hint = args.TryGetValue("2", out var hintArg) ? hintArg.Message!.ToPlainText() : string.Empty;

		if (!await CanSendOob(await parser.CurrentState.KnownExecutorObject(Mediator)))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (string.IsNullOrWhiteSpace(command))
		{
			return new CallState(text);
		}

		// A client sends the command as it stands, and a line break in it would send a second line.
		if (command.Any(char.IsControl))
		{
			return new CallState(ErrorMessages.Returns.InvalidArgument);
		}

		return new CallState(MarkupText.Wrap(
			Ansi.Create(linkUrl: command, linkKind: LinkKind.Command, linkText: hint.Length > 0 ? hint : command),
			text));
	}

	/// <summary>PennMUSH's <c>Can_Send_OOB</c> (<c>hdrs/mushdb.h</c>).</summary>
	private static async ValueTask<bool> CanSendOob(AnySharpObject executor)
		=> await executor.IsWizard() || await executor.HasPower("Send_OOB");

	/// <summary>
	/// <c>wshtml(&lt;html&gt;)</c> — an HTML fragment as markup (<see cref="HtmlFragment"/>): text nodes
	/// become the text, each element a layer over what it encloses. It sends nothing; the value is for
	/// whatever emits it, and every client reads it the way it reads <c>tagwrap()</c> output. Each element
	/// is held to the same gate: anything well-formed with Send_OOB, <see cref="TagwrapPolicy"/> without.
	///
	/// <para>What is malformed is repaired the way a browser repairs it, not refused.</para>
	///
	/// <para>PennMUSH's <c>wshtml(&lt;html&gt;, &lt;default&gt;)</c> carries a second, plain-text reading
	/// because its buffer holds raw bytes and cannot degrade a tag on its own. Markup can, so the second
	/// argument has no counterpart: the fragment's own text is what a client without HTML sees.</para>
	/// </summary>
	[SharpFunction(Name = "wshtml", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["html"])]
	public async ValueTask<CallState> WsHtml(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var html = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var privileged = await CanSendOob(await parser.CurrentState.KnownExecutorObject(Mediator));

		return HtmlFragment.Parse(html, privileged ? AnyElement : TagwrapPolicy.Wrap) switch
		{
			MString markup => new CallState(markup),
			Error<string> error => new CallState(error.Value),
		};
	}

	/// <summary>
	/// The Send_OOB reading of an element: any tag, every attribute, re-encoded the way
	/// <see cref="TagwrapPolicy.Wrap(string, IReadOnlyList{HtmlAttribute})"/> re-encodes the ones it keeps.
	/// </summary>
	private static HtmlMarkup AnyElement(string tagName, IReadOnlyList<HtmlAttribute> attributes)
		=> HtmlMarkup.Tag(tagName, [.. attributes]);

	[SharpFunction(Name = "WEBSOCKET_HTML", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular,
		ParameterNames = ["html", "player"])]
	public async ValueTask<CallState> WebSocketHTML(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var htmlContent = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (parser.CurrentState.Arguments.TryGetValue("1", out var targetArg)
				&& await LocateService.LocateAndNotifyIfInvalidWithCallState(
					parser,
					executor,
					executor,
					targetArg.Message!.ToPlainText(),
					PlayersPreference | AbsoluteMatch) is Error<CallState> error)
		{
			return error.Value;
		}

		// TODO: Actual websocket/out-of-band HTML communication is planned for future release.
		// 
		// Full implementation requirements:
		// 1. Add websocket support to ConnectionService
		// 2. Implement HTML rendering capability detection
		// 3. Add HTML sanitization to prevent security issues
		// 4. Support rich HTML features for web-based MUSH clients
		//
		// When implemented, this will send HTML through OOB channel
		// Placeholder - returns empty string as OOB data doesn't display in-band
		return CallState.Empty;
	}

	/// <summary>
	/// <c>sound(&lt;file&gt;[, &lt;volume&gt;[, &lt;repeats&gt;]])</c> — plays a sound effect. The file is
	/// one the client resolves against the game's own sound directory, or an absolute address.
	///
	/// <para>It is one object, and every client is told about it in its own way: MXP gets
	/// <c>&lt;SOUND&gt;</c>, Pueblo <c>&lt;img xch_sound&gt;</c>, the portal an <c>&lt;audio&gt;</c> the page
	/// decides whether to play, and a terminal nothing at all. Nothing is left in the plain text either,
	/// so a listen pattern never sees it.</para>
	///
	/// <para><paramref name="volume"/> is 0 to 100, and <c>repeats</c> is how many times to play it or
	/// <c>-1</c> for until it is stopped. Gated as <c>tagwrap()</c> is: a sound makes a client fetch and
	/// play a file.</para>
	/// </summary>
	[SharpFunction(Name = "sound", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["file", "volume", "repeats"])]
	public ValueTask<CallState> Sound(IMUSHCodeParser parser, SharpFunctionAttribute _2) => PlayAsync(parser, music: false);

	/// <summary>
	/// <c>music(&lt;file&gt;[, &lt;volume&gt;[, &lt;repeats&gt;]])</c> — as <c>sound()</c>, for background
	/// music: one piece plays at a time, and MXP has a channel of its own for it.
	/// </summary>
	[SharpFunction(Name = "music", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["file", "volume", "repeats"])]
	public ValueTask<CallState> Music(IMUSHCodeParser parser, SharpFunctionAttribute _2) => PlayAsync(parser, music: true);

	private async ValueTask<CallState> PlayAsync(IMUSHCodeParser parser, bool music)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var file = args["0"].Message!.ToPlainText().Trim();

		if (!await CanSendOob(await parser.CurrentState.KnownExecutorObject(Mediator)))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (file.Length == 0 || file.Any(char.IsControl))
		{
			return new CallState(ErrorMessages.Returns.InvalidArgument);
		}

		int? volume = null;
		if (args.TryGetValue("1", out var volumeArg) && volumeArg.Message!.ToPlainText().Trim() is { Length: > 0 } volumeText)
		{
			if (!int.TryParse(volumeText, out var parsed) || parsed is < 0 or > 100)
			{
				return new CallState(ErrorMessages.Returns.OutOfRange);
			}

			volume = parsed;
		}

		int? repeats = null;
		if (args.TryGetValue("2", out var repeatsArg) && repeatsArg.Message!.ToPlainText().Trim() is { Length: > 0 } repeatsText)
		{
			if (!int.TryParse(repeatsText, out var parsed) || parsed == 0 || parsed < MarkupString.SoundMarkup.Forever)
			{
				return new CallState(ErrorMessages.Returns.OutOfRange);
			}

			repeats = parsed;
		}

		return new CallState(music
			? MarkupText.Music(file, volume, repeats)
			: MarkupText.Sound(file, volume, repeats));
	}

	/// <summary>
	/// <c>stopsound([&lt;channel&gt;])</c> — silences what is playing: <c>effects</c>, <c>music</c>, or
	/// both when no channel is named.
	/// </summary>
	[SharpFunction(Name = "stopsound", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["channel"])]
	public async ValueTask<CallState> StopSound(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!await CanSendOob(await parser.CurrentState.KnownExecutorObject(Mediator)))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var args = parser.CurrentState.ArgumentsOrdered;
		var channel = args.TryGetValue("0", out var arg) ? arg.Message!.ToPlainText().Trim() : string.Empty;

		return channel.ToLowerInvariant() switch
		{
			"" => new CallState(MarkupText.StopSound()),
			"effects" => new CallState(MarkupText.StopSound(SoundChannel.Effects)),
			"music" => new CallState(MarkupText.StopSound(SoundChannel.Music)),
			_ => new CallState(ErrorMessages.Returns.InvalidArgument),
		};
	}

	/// <summary>
	/// <c>image(&lt;address&gt;[, &lt;description&gt;[, &lt;width&gt;[, &lt;height&gt;]]])</c> — a picture,
	/// standing in its description for a client that shows none, or the address when no description is
	/// given. MXP gets <c>&lt;IMAGE&gt;</c>, Pueblo and the portal <c>&lt;img&gt;</c>, and a terminal the
	/// words — which is why the description is worth writing.
	///
	/// <para>Put it inside <c>cmdlink()</c> to make it a picture that runs a command.</para>
	/// </summary>
	[SharpFunction(Name = "image", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["address", "description", "width", "height"])]
	public async ValueTask<CallState> Image(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!await CanSendOob(await parser.CurrentState.KnownExecutorObject(Mediator)))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var args = parser.CurrentState.ArgumentsOrdered;
		var address = args["0"].Message!.ToPlainText().Trim();
		if (address.Length == 0 || address.Any(char.IsControl))
		{
			return new CallState(ErrorMessages.Returns.InvalidArgument);
		}

		var description = args.TryGetValue("1", out var descriptionArg) ? descriptionArg.Message!.ToPlainText() : null;

		if (!TryPixels(args, "2", out var width) || !TryPixels(args, "3", out var height))
		{
			return new CallState(ErrorMessages.Returns.OutOfRange);
		}

		return new CallState(MarkupText.Image(address, description, width, height));
	}

	private static bool TryPixels(IReadOnlyDictionary<string, CallState> args, string key, out int? pixels)
	{
		pixels = null;
		if (!args.TryGetValue(key, out var arg) || arg.Message!.ToPlainText().Trim() is not { Length: > 0 } text) return true;
		if (!int.TryParse(text, out var parsed) || parsed <= 0) return false;

		pixels = parsed;
		return true;
	}

	/// <summary>
	/// <c>pane(&lt;text&gt;, &lt;name&gt;[, &lt;title&gt;])</c> — sends the text to a pane of its own,
	/// opening it if the client has none by that name. A client with no panes shows the text where it
	/// is, which is why the text travels inside the markup rather than after it.
	/// </summary>
	[SharpFunction(Name = "pane", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["text", "name", "title"])]
	public async ValueTask<CallState> Pane(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!await CanSendOob(await parser.CurrentState.KnownExecutorObject(Mediator)))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var args = parser.CurrentState.ArgumentsOrdered;
		var text = args["0"].Message!;
		var name = args["1"].Message!.ToPlainText().Trim();
		var title = args.TryGetValue("2", out var titleArg) ? titleArg.Message!.ToPlainText().Trim() : null;

		if (name.Length == 0 || name.Any(char.IsControl))
		{
			return new CallState(ErrorMessages.Returns.InvalidArgument);
		}

		return new CallState(MarkupText.Pane(text, name, string.IsNullOrEmpty(title) ? null : title));
	}

	/// <summary>
	/// <c>preformat(&lt;text&gt;)</c> — says the text is laid out by its own spacing: a table, a map, a
	/// listing. A client reading the stream as HTML collapses runs of spaces and ignores every column
	/// width unless it is told, so anything drawn with <c>align()</c> or spaces of your own wants this
	/// around it. <c>align()</c>, <c>lalign()</c> and <c>table()</c> already say it for themselves.
	///
	/// <para>No permission gate: it changes how the text is laid out, not what the client fetches.</para>
	/// </summary>
	[SharpFunction(Name = "preformat", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["text"])]
	public ValueTask<CallState> Preformat(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(new CallState(MarkupText.Preformatted(parser.CurrentState.Arguments["0"].Message!)));

	/// <summary>
	/// <c>clearscreen()</c> — clears what the player has been shown: <c>ESC[H ESC[2J</c> for a terminal,
	/// <c>&lt;xch_page clear&gt;</c> for Pueblo, and an element the portal acts on. MXP has no such
	/// instruction and is sent nothing.
	/// </summary>
	[SharpFunction(Name = "clearscreen", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public async ValueTask<CallState> ClearScreen(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await CanSendOob(await parser.CurrentState.KnownExecutorObject(Mediator))
			? new CallState(MarkupText.ClearScreen())
			: new CallState(ErrorMessages.Returns.PermissionDenied);

	/// <summary>
	/// <c>prefetch(&lt;address&gt;)</c> — asks the client to fetch something now that it will want soon.
	/// Pueblo and the portal act on it; every other client is sent nothing.
	/// </summary>
	[SharpFunction(Name = "prefetch", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["address"])]
	public async ValueTask<CallState> Prefetch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!await CanSendOob(await parser.CurrentState.KnownExecutorObject(Mediator)))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var address = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Trim();

		return address.Length == 0 || address.Any(char.IsControl)
			? new CallState(ErrorMessages.Returns.InvalidArgument)
			: new CallState(MarkupText.Prefetch(address));
	}

	/// <summary>
	/// <c>expirelinks([&lt;group&gt;])</c> — makes links already on the player's screen stop working:
	/// those in a named group, or every one. MXP acts on it; the portal is told, and a client with
	/// neither leaves its old links working.
	/// </summary>
	[SharpFunction(Name = "expirelinks", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["group"])]
	public async ValueTask<CallState> ExpireLinks(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!await CanSendOob(await parser.CurrentState.KnownExecutorObject(Mediator)))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var args = parser.CurrentState.ArgumentsOrdered;
		var group = args.TryGetValue("0", out var arg) ? arg.Message!.ToPlainText().Trim() : string.Empty;

		return new CallState(MarkupText.ExpireLinks(group.Length == 0 ? null : group));
	}
}