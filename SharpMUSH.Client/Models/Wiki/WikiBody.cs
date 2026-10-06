using System.Net;
using System.Text.RegularExpressions;

namespace SharpMUSH.Client.Models.Wiki;

/// <summary>Rendered HTML the body shows as it is.</summary>
public sealed record WikiBodyHtml(string Html);

/// <summary>A live listing (<c>::: recent 5</c> and the others) the body hydrates with a component.</summary>
public sealed record WikiBodyDirective(string Directive, string Arg);

/// <summary>
/// A <c>div</c> that holds a live listing somewhere inside it (a <c>::: center</c>, a <c>flex</c> or one of its
/// items), drawn as a real element so the listing renders inside it rather than after it.
/// </summary>
public sealed record WikiBodyElement(IReadOnlyDictionary<string, object> Attributes, IReadOnlyList<WikiBodyNode> Children);

/// <summary>One piece of a wiki page's body.</summary>
public union WikiBodyNode(WikiBodyHtml, WikiBodyDirective, WikiBodyElement);

/// <summary>
/// Cuts a page's rendered HTML into the pieces the portal draws: HTML, and the live listings it puts components
/// in place of. A listing can sit inside container blocks, so every <c>div</c> around one becomes an element
/// whose children are cut the same way; anything that holds no listing stays one piece of HTML. Cutting the
/// HTML flat at each listing would leave each wrapper's opening tag in one piece and its closing tag in
/// another, and the browser would close the wrapper before the listing.
/// </summary>
public static partial class WikiBody
{
	[GeneratedRegex("<div class=\"wiki-directive\" data-directive=\"([a-z]+)\" data-arg=\"([^\"]*)\"></div>|<div\\b([^>]*)>|</div>",
		RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
	private static partial Regex Token();

	[GeneratedRegex("([a-zA-Z_:][-a-zA-Z0-9_:.]*)\\s*=\\s*\"([^\"]*)\"", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
	private static partial Regex Attribute();

	/// <summary>A <c>div</c> still open while the HTML is read.</summary>
	private sealed class Frame(int start, string attributes)
	{
		public int Start { get; } = start;
		public string Attributes { get; } = attributes;
		public List<WikiBodyNode> Children { get; } = [];
		public bool HoldsDirective { get; set; }
	}

	/// <summary>The pieces of <paramref name="html"/>, in order.</summary>
	public static IReadOnlyList<WikiBodyNode> Split(string html)
	{
		var root = new List<WikiBodyNode>();
		var open = new Stack<Frame>();
		var position = 0;

		List<WikiBodyNode> Current() => open.Count > 0 ? open.Peek().Children : root;

		foreach (Match token in Token().Matches(html))
		{
			if (token.Index > position)
			{
				AddHtml(Current(), html[position..token.Index]);
			}

			position = token.Index + token.Length;

			if (token.Groups[1].Success)
			{
				Current().Add(new WikiBodyDirective(token.Groups[1].Value, WebUtility.HtmlDecode(token.Groups[2].Value)));
				foreach (var frame in open)
				{
					frame.HoldsDirective = true;
				}
			}
			else if (token.Groups[3].Success)
			{
				open.Push(new Frame(token.Index, token.Groups[3].Value));
			}
			else if (open.Count > 0)
			{
				Close(open.Pop(), open.Count > 0 ? open.Peek().Children : root, html[..position]);
			}
			else
			{
				AddHtml(root, token.Value);
			}
		}

		if (position < html.Length)
		{
			AddHtml(Current(), html[position..]);
		}

		// Unclosed divs (Markdig closes its own, so only broken input) close at the end.
		while (open.Count > 0)
		{
			Close(open.Pop(), open.Count > 0 ? open.Peek().Children : root, html);
		}

		return root;
	}

	/// <summary>
	/// Ends <paramref name="frame"/>: as one piece of its source HTML when nothing in it is a listing,
	/// otherwise as an element holding its pieces.
	/// </summary>
	private static void Close(Frame frame, List<WikiBodyNode> parent, string htmlToHere)
	{
		if (!frame.HoldsDirective)
		{
			AddHtml(parent, htmlToHere[frame.Start..]);
			return;
		}

		var attributes = Attribute().Matches(frame.Attributes)
			.GroupBy(match => match.Groups[1].Value, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(group => group.Key, group => (object)WebUtility.HtmlDecode(group.First().Groups[2].Value),
				StringComparer.OrdinalIgnoreCase);
		parent.Add(new WikiBodyElement(attributes, frame.Children));
	}

	/// <summary>Adds HTML to <paramref name="nodes"/>, joining it to HTML that already ends the list.</summary>
	private static void AddHtml(List<WikiBodyNode> nodes, string html)
	{
		if (html.Length == 0) return;
		if (nodes is [.., WikiBodyHtml last])
		{
			nodes[^1] = new WikiBodyHtml(last.Html + html);
			return;
		}

		nodes.Add(new WikiBodyHtml(html));
	}
}
