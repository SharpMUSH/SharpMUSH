using System.Buffers;
namespace MarkupString;

/// <summary>
/// Turns a <see cref="MarkupText"/> into an output format: literal text encoded per
/// <see cref="MarkupFormat.Encoding"/>, each styled run handed to the registry's set emitter or,
/// failing that, wrapped by its layers' emitters innermost first.
/// </summary>
public static class MarkupTextRenderer
{
	/// <summary>C0 controls other than tab, newline and carriage return, plus DEL.</summary>
	private const string ControlCharacters = "\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u000b\u000c\u000e\u000f\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f\u007f";

	/// <summary>The five characters HTML encodes.</summary>
	private const string HtmlCharacters = "<>&\"'";

	private static readonly SearchValues<char> Controls = SearchValues.Create(ControlCharacters);

	/// <summary><see cref="Controls"/> plus the characters HTML encodes.</summary>
	private static readonly SearchValues<char> ControlsAndHtml = SearchValues.Create(ControlCharacters + HtmlCharacters);

	/// <summary>Writes <paramref name="text"/> to <paramref name="output"/> under <paramref name="encoding"/>.</summary>
	public static void EncodeText(ReadOnlySpan<char> text, TextEncoding encoding, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(output);
		switch (encoding)
		{
			case TextEncoding.StripControls:
				Strip(text, output);
				break;
			case TextEncoding.Html:
				HtmlEncode(text, output);
				break;
			default:
				output.Write(text);
				break;
		}
	}

	private static void Strip(ReadOnlySpan<char> text, IBufferWriter<char> output)
	{
		while (!text.IsEmpty)
		{
			var index = text.IndexOfAny(Controls);
			if (index < 0)
			{
				output.Write(text);
				return;
			}
			output.Write(text[..index]);
			text = text[(index + 1)..];
		}
	}

	private static void HtmlEncode(ReadOnlySpan<char> text, IBufferWriter<char> output)
	{
		while (!text.IsEmpty)
		{
			var index = text.IndexOfAny(ControlsAndHtml);
			if (index < 0)
			{
				output.Write(text);
				return;
			}
			output.Write(text[..index]);
			switch (text[index])
			{
				case '<': output.Write("&lt;"); break;
				case '>': output.Write("&gt;"); break;
				case '&': output.Write("&amp;"); break;
				case '"': output.Write("&quot;"); break;
				case '\'': output.Write("&#39;"); break;
				default: break;   // a control character: dropped
			}
			text = text[(index + 1)..];
		}
	}

	/// <summary>Renders <paramref name="text"/> in <paramref name="format"/> to <paramref name="output"/>.</summary>
	public static void Render(MarkupText text, MarkupFormat format, MarkupRegistry registry, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(format);
		ArgumentNullException.ThrowIfNull(registry);
		ArgumentNullException.ThrowIfNull(output);

		var framer = registry.FindFramer(format);
		framer?.WritePreamble(output);

		var content = text.Text.AsSpan();
		var runs = text.Runs;
		var position = 0;
		for (var i = 0; i < runs.Length; i++)
		{
			var run = runs[i];
			if (run.Start > position) EncodeText(content[position..run.Start], format.Encoding, output);
			var context = new EmitContext
			{
				Format = format,
				Registry = registry,
				Previous = i > 0 && runs[i - 1].End == run.Start ? runs[i - 1].Markups : null,
				Next = i + 1 < runs.Length && runs[i + 1].Start == run.End ? runs[i + 1].Markups : null,
				IsFirstRun = i == 0,
				IsLastRun = i == runs.Length - 1,
			};
			RenderRun(run, content.Slice(run.Start, run.Length), format, registry, context, output);
			position = run.End;
		}
		if (position < content.Length) EncodeText(content[position..], format.Encoding, output);

		framer?.WriteEpilogue(!runs.IsEmpty, output);
	}

	private static void RenderRun(
		Run run,
		ReadOnlySpan<char> body,
		MarkupFormat format,
		MarkupRegistry registry,
		in EmitContext context,
		IBufferWriter<char> output)
	{
		var front = new PooledCharWriter(body.Length + 16);
		PooledCharWriter? back = null;
		try
		{
			EncodeText(body, format.Encoding, front);

			var setEmitter = registry.FindSetEmitter(format);
			if (setEmitter is not null)
			{
				back = new PooledCharWriter(front.WrittenCount + 16);
				if (setEmitter.TryEmit(run.Markups, front.WrittenSpan, context, back))
				{
					output.Write(back.WrittenSpan);
					return;
				}
				back.Clear();
			}

			for (var i = 0; i < run.Markups.Count; i++)
			{
				var markup = run.Markups[i];
				var emitter = registry.FindEmitter(markup.GetType(), format);
				if (emitter is null) continue;
				back ??= new PooledCharWriter(front.WrittenCount + 16);
				back.Clear();
				emitter.Emit(markup, front.WrittenSpan, context, back);
				(front, back) = (back, front);
			}

			output.Write(front.WrittenSpan);
		}
		finally
		{
			front.Dispose();
			back?.Dispose();
		}
	}
}
