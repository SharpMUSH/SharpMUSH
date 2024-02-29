using OneOf;
using System.Collections.Immutable;

namespace AntlrCSharp.Implementation.Markup
{
	/// <summary>
	/// An AnsiString is a string carrying markup, but instead of using plain ANSI sequences, marks them as 'spans'
	/// This allows us to insert, append, etc without risking ansi-bleed.
	/// </summary>
	public record AnsiString : MarkupSpan<AnsiMarkup>
	{
		public AnsiString(string text) : base(text) { }

		public AnsiString(AnsiMarkup markup, string text) : base(markup, text) { }

		public AnsiString(AnsiMarkup markup, MarkupSpan<AnsiMarkup> span) : base(markup, span) { }

		public AnsiString(AnsiMarkup markup, IImmutableList<OneOf<string, MarkupSpan<AnsiMarkup>>> spans) : base(markup, spans) { }

		protected AnsiString(MarkupSpan<AnsiMarkup> original) : base(original) { }
	}
}
