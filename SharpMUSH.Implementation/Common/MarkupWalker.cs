using System.Text;

namespace SharpMUSH.Implementation.Common;

/// <summary>
/// Walks a <see cref="MString"/> run by run and rebuilds it as a plain string, handing every
/// segment to a caller-supplied evaluator once per markup layer (innermost first) and once with
/// <see langword="null"/> for the stretches that carry no markup at all.
/// </summary>
/// <remarks>
/// This is the shape <c>decompose()</c>, <c>decomposeweb()</c> and <c>@decompile</c> need: they do
/// not render the markup, they re-emit it as the softcode that would recreate it, so they have to
/// see each markup object paired with the text it covers. Nothing in <see cref="MarkupText"/>
/// offers that — its renderers emit a wire format, not source — so the walk lives here, next to the
/// three functions that want it.
/// </remarks>
internal static class MarkupWalker
{
	/// <inheritdoc cref="MarkupWalker"/>
	public static string EvaluateWith(Func<IMarkup?, string, string> evaluator, MString text)
	{
		var builder = new StringBuilder(text.Length);
		var position = 0;

		foreach (var run in text.Runs)
		{
			if (run.Start > position)
			{
				builder.Append(evaluator(null, text.Text[position..run.Start]));
			}

			var segment = text.Text.Substring(run.Start, run.Length);
			foreach (var markup in run.Markups)
			{
				segment = evaluator(markup, segment);
			}

			builder.Append(segment);
			position = run.End;
		}

		if (position < text.Length)
		{
			builder.Append(evaluator(null, text.Text[position..]));
		}

		return builder.ToString();
	}
}
