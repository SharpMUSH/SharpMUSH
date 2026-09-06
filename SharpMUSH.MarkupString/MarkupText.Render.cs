using System.Buffers;
namespace MarkupString;

public sealed partial class MarkupText
{
	/// <summary>Renders this text in <paramref name="format"/>, against <see cref="MarkupRegistry.Default"/> when no registry is given.</summary>
	public string Render(MarkupFormat format, MarkupRegistry? registry = null)
	{
		var writer = new ArrayBufferWriter<char>(Length + 16);
		MarkupTextRenderer.Render(this, format, registry ?? MarkupRegistry.Default, writer);
		return new string(writer.WrittenSpan);
	}

	/// <summary>Renders this text in <paramref name="format"/> straight into <paramref name="output"/>.</summary>
	public void RenderTo(MarkupFormat format, IBufferWriter<char> output, MarkupRegistry? registry = null) =>
		MarkupTextRenderer.Render(this, format, registry ?? MarkupRegistry.Default, output);

	/// <summary>
	/// Whether this text and <paramref name="other"/> render identically in
	/// <paramref name="format"/> — the markup-aware equality, as against
	/// <see cref="Equals(MarkupText?)"/>, which compares text alone.
	/// </summary>
	public bool Equals(MarkupText other, MarkupFormat format, MarkupRegistry? registry = null)
	{
		ArgumentNullException.ThrowIfNull(other);
		if (ReferenceEquals(this, other)) return true;
		if (Runs.IsEmpty && other.Runs.IsEmpty && string.Equals(Text, other.Text, StringComparison.Ordinal)) return true;

		var resolved = registry ?? MarkupRegistry.Default;
		using var mine = new PooledCharWriter(Length + 16);
		using var theirs = new PooledCharWriter(other.Length + 16);
		MarkupTextRenderer.Render(this, format, resolved, mine);
		MarkupTextRenderer.Render(other, format, resolved, theirs);
		return mine.WrittenSpan.SequenceEqual(theirs.WrittenSpan);
	}
}
