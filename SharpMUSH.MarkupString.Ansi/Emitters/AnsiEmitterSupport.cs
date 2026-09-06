using System.Buffers;
using System.Net;
namespace MarkupString.Ansi;

/// <summary>Which tag vocabulary <see cref="AnsiEmitterSupport.EmitTagged"/> writes links in.</summary>
internal enum TagFlavour
{
	/// <summary>Pueblo: <c>&lt;A XCH_CMD&gt;</c> for commands, <c>&lt;A HREF&gt;</c> for URLs.</summary>
	Pueblo,

	/// <summary>MXP: <c>&lt;SEND&gt;</c> for commands, <c>&lt;A HREF&gt;</c> for URLs.</summary>
	Mxp
}

/// <summary>
/// The parts every set emitter in this package shares: folding a run's layers into one
/// <see cref="AnsiStyle"/>, delegating the layers it does not own, and the two link forms that are
/// spelled the same way in more than one format.
/// </summary>
internal static class AnsiEmitterSupport
{
	private const string Osc8 = "\u001b]8;;";
	private const string Bel = "\u0007";

	/// <summary>
	/// Whether this package folds <paramref name="layer"/> into the run's style for
	/// <paramref name="format"/>, rather than delegating it to its own emitter. The one place that
	/// question is answered: <see cref="Fold"/> takes the layers it says yes to and
	/// <see cref="WriteWrapped"/> takes exactly the rest, so no layer is rendered twice or dropped.
	/// </summary>
	internal static bool ClaimsStyle(IMarkup layer, MarkupFormat format, out AnsiStyle style)
	{
		if (layer is IAnsiStyleSource source) return source.TryGetAnsiStyle(format, out style);
		style = AnsiStyle.None;
		return false;
	}

	/// <summary>
	/// Folds every layer that offers a style in <paramref name="format"/> into one, outermost
	/// first, so an inner layer's settings win. Layers that offer none are left for
	/// <see cref="WriteWrapped"/>.
	/// </summary>
	internal static AnsiStyle Fold(MarkupSet? set, MarkupFormat format)
	{
		if (set is null) return AnsiStyle.None;

		var effective = AnsiStyle.None;
		for (var i = set.Count - 1; i >= 0; i--)
			if (ClaimsStyle(set[i], format, out var style))
				effective = effective.Combine(style);

		return effective;
	}

	/// <summary>
	/// Writes <paramref name="core"/> — the run as this package rendered it — wrapped by the layers
	/// this package does not own in <see cref="EmitContext.Format"/>, innermost first, each through
	/// its own emitter for that format. A layer with no emitter registered for the format wraps in
	/// nothing: its body passes through.
	/// </summary>
	internal static void WriteWrapped(
		MarkupSet set,
		ReadOnlySpan<char> core,
		in EmitContext context,
		IBufferWriter<char> output)
	{
		PooledCharWriter? front = null;
		PooledCharWriter? back = null;
		try
		{
			for (var i = 0; i < set.Count; i++)
			{
				var layer = set[i];
				if (ClaimsStyle(layer, context.Format, out _)) continue;

				var emitter = context.Registry.FindEmitter(layer.GetType(), context.Format);
				if (emitter is null) continue;

				if (front is null)
				{
					front = new PooledCharWriter(core.Length + 16);
					front.Write(core);
				}

				back ??= new PooledCharWriter(front.WrittenCount + 16);
				back.Clear();
				emitter.Emit(layer, front.WrittenSpan, context, back);
				(front, back) = (back, front);
			}

			output.Write(front is null ? core : front.WrittenSpan);
		}
		finally
		{
			front?.Dispose();
			back?.Dispose();
		}
	}

	/// <summary>
	/// Writes the body inside an OSC 8 hyperlink when the style carries a navigable URL. OSC 8 can
	/// only navigate, so a command link — and any URL with a scheme
	/// <see cref="UrlSafety.IsSafeNavigableUrl"/> rejects — is written as plain text.
	/// </summary>
	internal static void WriteHyperlinked(in AnsiStyle style, ReadOnlySpan<char> body, IBufferWriter<char> output)
	{
		if (style.LinkKind != LinkKind.Url
			|| style.LinkUrl is not { Length: > 0 } url
			|| !UrlSafety.IsSafeNavigableUrl(url))
		{
			output.Write(body);
			return;
		}

		output.Write(Osc8);
		output.Write(url);
		output.Write(Bel);
		output.Write(body);
		output.Write(Osc8);
		output.Write(Bel);
	}

	/// <summary>
	/// The shared Pueblo/MXP body: colours and attributes as SGR (these formats are not diffed
	/// across runs, so each styled run opens and closes its own state), links as the format's own
	/// tag, and everything else delegated.
	/// </summary>
	internal static void EmitTagged(
		MarkupSet set,
		ReadOnlySpan<char> body,
		in EmitContext context,
		IBufferWriter<char> output,
		TagFlavour flavour)
	{
		var style = Fold(set, context.Format);

		using var core = new PooledCharWriter(body.Length + 64);
		var styled = SgrWriter.Transition(AnsiStyle.None, style, core);
		WriteTaggedLink(style, body, core, flavour);
		if (styled) SgrWriter.Reset(core);

		WriteWrapped(set, core.WrittenSpan, context, output);
	}

	private static void WriteTaggedLink(
		in AnsiStyle style,
		ReadOnlySpan<char> body,
		IBufferWriter<char> output,
		TagFlavour flavour)
	{
		if (style.LinkUrl is not { Length: > 0 } url)
		{
			output.Write(body);
			return;
		}

		if (style.LinkKind == LinkKind.Command)
		{
			var (open, hint, close) = flavour == TagFlavour.Mxp
				? ("<SEND HREF=\"", " HINT=\"", "</SEND>")
				: ("<A XCH_CMD=\"", " XCH_HINT=\"", "</A>");

			output.Write(open);
			output.Write(WebUtility.HtmlEncode(url));
			output.Write("\"");
			if (style.LinkText is { Length: > 0 } text)
			{
				output.Write(hint);
				output.Write(WebUtility.HtmlEncode(text));
				output.Write("\"");
			}
			output.Write(">");
			output.Write(body);
			output.Write(close);
			return;
		}

		if (!UrlSafety.IsSafeNavigableUrl(url))
		{
			output.Write(body);
			return;
		}

		output.Write("<A HREF=\"");
		output.Write(WebUtility.HtmlEncode(url));
		output.Write("\">");
		output.Write(body);
		output.Write("</A>");
	}
}
