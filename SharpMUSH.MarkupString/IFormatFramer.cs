using System.Buffers;
namespace MarkupString;

/// <summary>
/// Writes whatever a format needs around a whole rendered text — an ANSI reset after any styled
/// run, say. Registered per format; at most one framer per format.
/// </summary>
public interface IFormatFramer
{
	/// <summary>The format this frames.</summary>
	MarkupFormat Format { get; }

	/// <summary>Written before any of the text.</summary>
	void WritePreamble(IBufferWriter<char> output);

	/// <summary>Written after all of the text. <paramref name="anyRunEmitted"/> tells whether the text carried any styled run.</summary>
	void WriteEpilogue(bool anyRunEmitted, IBufferWriter<char> output);
}
