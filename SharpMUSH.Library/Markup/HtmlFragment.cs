using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MarkupString;
using MarkupString.Html;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// An HTML fragment as markup: text nodes become the text, each element an <see cref="HtmlMarkup"/>
/// layer over what it encloses. The result is an ordinary <see cref="MarkupText"/>, so it stores,
/// slices and renders like any other — a Pueblo, MXP or portal client gets the tags, ANSI gets the
/// handful it can fold into styling, everything else gets the words.
/// </summary>
/// <remarks>
/// The fragment is read the way a browser reads it (AngleSharp's HTML5 parser, in body context), so
/// what is malformed is repaired rather than refused: an unclosed tag closes at the end, a stray
/// closing tag is dropped, a bare <c>&lt;</c> is text. Two things the span model cannot say are
/// dropped rather than invented: an element enclosing nothing has nothing to cover, and a void
/// element has no content by definition — except <c>&lt;br&gt;</c>, whose plain reading is the line
/// break it means. The one refusal left is the caller's: an element <paramref name="element"/> will
/// not write.
/// </remarks>
public static class HtmlFragment
{
	private static readonly HtmlParser Parser = new();

	/// <summary>
	/// Parses <paramref name="html"/>. <paramref name="element"/> turns each element's lower-cased
	/// name and attributes into its layer — or <see langword="null"/> to refuse the element, which
	/// refuses the fragment with <see cref="ErrorMessages.Returns.PermissionDenied"/>.
	/// </summary>
	public static Result<MString> Parse(string html, Func<string, IReadOnlyList<HtmlAttribute>, HtmlMarkup?> element)
	{
		using var document = Parser.ParseDocument(string.Empty);
		return Content(Parser.ParseFragment(html, document.Body!), element);
	}

	private static Result<MString> Content(IEnumerable<INode> nodes, Func<string, IReadOnlyList<HtmlAttribute>, HtmlMarkup?> element)
	{
		var pieces = new List<MString>();
		foreach (var node in nodes)
		{
			switch (node)
			{
				case IText text:
					pieces.Add(MarkupText.Plain(text.Data));
					break;
				case IElement child:
					switch (Element(child, element))
					{
						case Error<string> error: return error;
						case MString piece: pieces.Add(piece); break;
					}
					break;
			}
		}

		return MarkupText.Concat(pieces);
	}

	/// <summary>One element as the layer over its content.</summary>
	private static Result<MString> Element(IElement node, Func<string, IReadOnlyList<HtmlAttribute>, HtmlMarkup?> element)
	{
		// AngleSharp already gives a void element no children; only <br> has a plain reading.
		if (node.LocalName == "br") return MarkupText.Plain("\n");

		var attributes = node.Attributes.Select(attribute => new HtmlAttribute(attribute.Name, attribute.Value)).ToList();
		if (element(node.LocalName, attributes) is not { } markup)
		{
			return new Error<string>(ErrorMessages.Returns.PermissionDenied);
		}

		return Content(node.ChildNodes, element) switch
		{
			MString content => content.Length == 0 ? MarkupText.Empty : MarkupText.Wrap(markup, content),
			Error<string> error => error,
		};
	}
}
