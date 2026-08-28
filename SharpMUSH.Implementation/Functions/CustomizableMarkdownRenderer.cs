using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Implementation.Commands.WikiCommand;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.Json;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// Customizable markdown renderer that uses object attributes as templates for rendering elements
/// </summary>
public class CustomizableMarkdownRenderer : RecursiveMarkdownRenderer
{
	private readonly IMUSHCodeParser _parser;
	private readonly AnySharpObject _executor;
	private readonly AnySharpObject _templateObject;
	private readonly IAttributeService _attributeService;

	/// <summary>
	/// Template names already looked up on <see cref="_templateObject"/> and found absent.
	/// </summary>
	/// <remarks>
	/// Block templates are consulted a handful of times per document; inline ones (BOLD, LINK,
	/// INLINECODE …) are consulted once per element, so an unconfigured object would otherwise pay an
	/// attribute lookup for every bold word in the text. A renderer instance lives for exactly one
	/// <c>rendermarkdowncustom()</c> call, so the negative cache cannot go stale. Only absence is
	/// cached — a template that exists is re-evaluated every time, with that element's own arguments.
	/// </remarks>
	private readonly HashSet<string> _absentTemplates = new(StringComparer.OrdinalIgnoreCase);

	public CustomizableMarkdownRenderer(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		AnySharpObject templateObject,
		IAttributeService attributeService,
		int maxWidth = 78) : base(maxWidth, parser)
	{
		_parser = parser;
		_executor = executor;
		_templateObject = templateObject;
		_attributeService = attributeService;
	}

	/// <summary>
	/// The markdown this renderer was handed, kept so <c>RENDERMARKUP`TABLE</c> can give a template each
	/// cell's raw source. Markdig records cell positions as offsets into the string it parsed, so the
	/// string has to outlive the parse.
	/// </summary>
	private string? _markdownSource;

	public MString RenderMarkdown(string markdown)
	{
		_markdownSource = markdown;
		return RecursiveMarkdownHelper.RenderMarkdown(markdown, this);
	}

	/// <summary>
	/// Whether the object defines <c>RENDERMARKUP`{templateName}</c>, caching a negative answer.
	/// </summary>
	/// <remarks>
	/// Separate from <see cref="TryEvaluateTemplate"/> so a caller can ask <em>before</em> building the
	/// arguments. Every other template's arguments are a rendered MString it already has in hand;
	/// TABLE's is a JSON serialisation of the whole table, which no game that has not configured the
	/// template should pay for.
	/// </remarks>
	private bool HasTemplate(string templateName)
	{
		if (_absentTemplates.Contains(templateName)) return false;

		// No catch here, deliberately, and unlike TryEvaluateTemplate below. This only *looks up* an
		// attribute, and IAttributeService.GetAttributeAsync reports every expected outcome — no such
		// attribute, unusable attribute name, insufficient permission — in its return type rather than
		// by throwing. Nothing an exception could mean here is "the object has no template": it would
		// be a database or DI fault, and swallowing it on a path that runs once per template name is
		// how such a fault becomes invisible. RenderMarkdownCustom's own handler turns it into
		// "#-1 ERROR RENDERING MARKDOWN", which is the honest answer.
		var maybeAttr = _attributeService.GetAttributeAsync(
			_executor,
			_templateObject,
			$"RENDERMARKUP`{templateName}",
			mode: IAttributeService.AttributeMode.Execute,
			parent: false).GetAwaiter().GetResult();

		if (maybeAttr.IsAttribute) return true;

		_absentTemplates.Add(templateName);
		return false;
	}

	/// <summary>
	/// Try to get and evaluate a custom template attribute.
	/// </summary>
	/// <remarks>
	/// The broad catch is the documented contract, not an oversight, and it is why this differs from
	/// <see cref="HasTemplate"/>: this method also *evaluates* the template, which is arbitrary
	/// player-written softcode. A template that recurses, divides by zero or otherwise blows up must
	/// leave the rest of the document rendering rather than take the whole call down with it —
	/// "falls back gracefully to default rendering if template evaluation fails", as
	/// <c>help rendermarkdowncustom</c> puts it. A lookup has no such failure mode, so it has no catch.
	/// </remarks>
	private async Task<MString?> TryEvaluateTemplate(string templateName, Dictionary<string, CallState> args)
	{
		if (_absentTemplates.Contains(templateName)) return null;

		try
		{
			var attrName = $"RENDERMARKUP`{templateName}";
			var maybeAttr = await _attributeService.GetAttributeAsync(
				_executor,
				_templateObject,
				attrName,
				mode: IAttributeService.AttributeMode.Execute,
				parent: false);

			if (!maybeAttr.IsAttribute)
			{
				_absentTemplates.Add(templateName);
				return null;
			}

			var result = await _attributeService.EvaluateAttributeFunctionAsync(
				_parser,
				_executor,
				_templateObject,
				attrName,
				args);

			return result;
		}
		catch
		{
			return null;
		}
	}

	protected override MString RenderHeading(HeadingBlock heading)
	{
		var templateName = heading.Level switch
		{
			1 => "H1",
			2 => "H2",
			3 => "H3",
			_ => "H3"
		};

		var content = RenderInlineContent(heading.Inline);

		var args = new Dictionary<string, CallState>
		{
			{ "0", new CallState(content) }
		};

		var custom = TryEvaluateTemplate(templateName, args).GetAwaiter().GetResult();
		return custom ?? base.RenderHeading(heading);
	}

	/// <summary>
	/// Helper method to render inline content (similar to private RenderInlines in base class)
	/// </summary>
	private MString RenderInlineContent(Inline? inline)
	{
		var parts = new List<MString>();
		while (inline != null)
		{
			var rendered = Render(inline);
			if (rendered.Length > 0)
			{
				parts.Add(rendered);
			}
			inline = inline.NextSibling;
		}
		return MModule.multiple(parts);
	}

	protected override MString RenderCodeBlock(CodeBlock code)
	{
		var lines = code.Lines.Lines?
			.Where(line => line.Slice.Text != null)
			.Select(line => line.Slice.ToString())
			.ToList() ?? new List<string>();

		var codeContent = string.Join("\n", lines);
		var args = new Dictionary<string, CallState>
		{
			{ "0", new CallState(MModule.single(codeContent)) }
		};

		var custom = TryEvaluateTemplate("CODEBLOCK", args).GetAwaiter().GetResult();
		return custom ?? base.RenderCodeBlock(code);
	}

	protected override MString RenderListItem(ListItemBlock listItem, int index = 0, bool isOrdered = false)
	{
		var content = base.RenderListItem(listItem, index, isOrdered);
		var args = new Dictionary<string, CallState>
		{
			{ "0", new CallState(MModule.single(isOrdered ? "1" : "0")) },
			{ "1", new CallState(MModule.single((index + 1).ToString())) }, // Convert to 1-based index
			{ "2", new CallState(content) }
		};

		var custom = TryEvaluateTemplate("LISTITEM", args).GetAwaiter().GetResult();
		return custom ?? content;
	}

	protected override MString RenderQuote(QuoteBlock quote)
	{
		var parts = quote
			.Select(child => Render(child))
			.Where(rendered => rendered.Length > 0)
			.ToList();

		var content = MModule.multipleWithDelimiter(MModule.single("\n"), parts);
		var args = new Dictionary<string, CallState>
		{
			{ "0", new CallState(content) }
		};

		var custom = TryEvaluateTemplate("QUOTE", args).GetAwaiter().GetResult();
		return custom ?? base.RenderQuote(quote);
	}

	/// <summary>Numbers template arguments <c>%0</c>, <c>%1</c>, … in the order given.</summary>
	private static Dictionary<string, CallState> Args(params MString[] values) =>
		values
			.Select((value, index) => (index, value))
			.ToDictionary(pair => pair.index.ToString(), pair => new CallState(pair.value));

	/// <summary>A boolean template argument, in the <c>1</c>/<c>0</c> spelling softcode tests with.</summary>
	private static MString Flag(bool value) => MModule.single(value ? "1" : "0");

	private static MString Text(string? value) => MModule.single(value ?? string.Empty);

	/// <summary>
	/// Rendered content reduced to the plain text the default rendering would have used.
	/// </summary>
	/// <remarks>
	/// Every element whose built-in rendering calls <c>ToPlainText()</c> on its content hands the
	/// template the same plain text, so a template is never given markup the default would have thrown
	/// away. The alternative — an argument that carries ANSI for some inputs and not others — is worse
	/// than either choice on its own, because nothing in softcode can tell which it received.
	/// The elements that genuinely work from rendered content (headings, LISTITEM, QUOTE, CONTAINER)
	/// pass it through instead, and say so on their own members.
	/// </remarks>
	private static MString Plain(MString content) => MModule.single(content.ToPlainText());

	/// <summary>Runs <paramref name="templateName"/>, or <c>null</c> when the object does not define it.</summary>
	private MString? Template(string templateName, Dictionary<string, CallState> args) =>
		TryEvaluateTemplate(templateName, args).GetAwaiter().GetResult();

	/// <summary>
	/// <c>RENDERMARKUP`BOLD</c>: <c>%0</c> the emphasised text, plain.
	/// </summary>
	/// <remarks>
	/// Plain because <see cref="RecursiveMarkdownRenderer.RenderBold"/> is: it styles
	/// <c>content.ToPlainText()</c>, so nested markup is already discarded by the default rendering and
	/// a template that received it would be the only thing in the pipeline that could see it.
	/// </remarks>
	protected override MString RenderBold(MString content) =>
		Template("BOLD", Args(Plain(content))) ?? base.RenderBold(content);

	/// <inheritdoc cref="RenderBold"/>
	protected override MString RenderItalic(MString content) =>
		Template("ITALIC", Args(Plain(content))) ?? base.RenderItalic(content);

	/// <inheritdoc cref="RenderBold"/>
	protected override MString RenderUnderline(MString content) =>
		Template("UNDERLINE", Args(Plain(content))) ?? base.RenderUnderline(content);

	/// <summary><c>RENDERMARKUP`INLINECODE</c>: <c>%0</c> the code text, which is plain by construction.</summary>
	protected override MString RenderInlineCode(CodeInline code) =>
		Template("INLINECODE", Args(Text(code.Content))) ?? base.RenderInlineCode(code);

	/// <summary>
	/// <c>RENDERMARKUP`LINK</c>: <c>%0</c> link text (plain), <c>%1</c> URL or command, <c>%2</c> whether
	/// the source already marked it a command, <c>%3</c> the link title/hint.
	/// </summary>
	/// <remarks>
	/// <c>%2</c> is what makes the template usable: a help-topic shortcut (<c>[topic]</c>) already
	/// carries a command in <c>%1</c> rather than a URL, and a template that wrapped it in an
	/// <c>xch_cmd</c> and a plain <c>https:</c> link in an <c>href</c> has to be able to tell them apart.
	/// Images are not links and keep going to <c>IMAGE</c>.
	/// <para>
	/// A link with no URL is not a link: the default rendering emits the content unchanged rather than
	/// any link markup, so the template is not consulted at all. Otherwise an object with a LINK template
	/// would decorate something the default would have left as prose.
	/// </para>
	/// </remarks>
	protected override MString RenderLink(LinkInline link, MString content)
	{
		// Images are dispatched by the base implementation, which routes them to RenderImage.
		if (link.IsImage) return base.RenderLink(link, content);

		var url = link.Url ?? string.Empty;
		if (string.IsNullOrWhiteSpace(url)) return base.RenderLink(link, content);

		// The text the default rendering would show — trimmed, plain, and falling back to the URL when
		// the link has no text — so a template never has to reproduce that rule for itself.
		var contentText = content.ToPlainText().Trim();
		var text = string.IsNullOrWhiteSpace(contentText) ? url : contentText;
		var isCommand = link.GetData(HelpTopicInlineParser.CommandDataKey) is true;

		return Template("LINK", Args(Text(text), Text(url), Flag(isCommand), Text(link.Title)))
			?? base.RenderLink(link, content);
	}

	/// <summary>
	/// <c>RENDERMARKUP`IMAGE</c>: <c>%0</c> the alt text, plain and trimmed, <c>%1</c> the image URL.
	/// </summary>
	/// <remarks>
	/// Plain because <see cref="RecursiveMarkdownRenderer.RenderImage"/> is: the built-in placeholder is
	/// built from <c>content.ToPlainText().Trim()</c>. Alt text can hold inline markup
	/// (<c>![a **bold** logo](…)</c>), and handing that over rendered would leak ANSI into a softcode
	/// argument that the helpfile calls "the alt text".
	/// </remarks>
	protected override MString RenderImage(LinkInline link, MString content) =>
		Template("IMAGE", Args(Text(content.ToPlainText().Trim()), Text(link.Url)))
			?? base.RenderImage(link, content);

	/// <summary>
	/// <c>RENDERMARKUP`WIKILINK</c>: <c>%0</c> display text, <c>%1</c> the <c>@wiki</c> page reference,
	/// <c>%2</c> the target page's title. All three are plain by construction.
	/// </summary>
	/// <remarks>
	/// <c>%1</c> is the fully qualified <c>namespace:category:slug</c> form, so a template can build
	/// <c>@wiki %1</c> and get a working command — which is what <c>@wiki</c> itself renders a wiki link
	/// as. Falling through to <c>base</c> rather than to a fixed rendering keeps whatever default the
	/// surface has: neutral prose here, the command link under <c>@wiki</c>.
	/// <para>
	/// A link with no display text renders as nothing by default, so the template is not consulted for
	/// one.
	/// </para>
	/// </remarks>
	protected override MString RenderWikiLink(WikiLinkInline wikiLink)
	{
		var display = wikiLink.DisplayText ?? wikiLink.Title;
		if (string.IsNullOrWhiteSpace(display)) return base.RenderWikiLink(wikiLink);

		var reference = WikiCommandHelper.ReferenceForWikiLink(wikiLink);

		return Template("WIKILINK", Args(Text(display), Text(reference), Text(wikiLink.Title)))
			?? base.RenderWikiLink(wikiLink);
	}

	/// <summary>
	/// <c>RENDERMARKUP`AUTOLINK</c>: <c>%0</c> the URL, which is also the text shown.
	/// </summary>
	/// <remarks>
	/// An autolink with no URL renders as nothing by default. The template is not consulted for one,
	/// or an object with an AUTOLINK template would emit output where every other renderer emits
	/// nothing at all.
	/// </remarks>
	protected override MString RenderAutolink(AutolinkInline autolink) =>
		string.IsNullOrEmpty(autolink.Url)
			? base.RenderAutolink(autolink)
			: Template("AUTOLINK", Args(Text(autolink.Url))) ?? base.RenderAutolink(autolink);

	/// <summary><c>RENDERMARKUP`TASKLIST</c>: <c>%0</c> is 1 when the box is ticked.</summary>
	/// <remarks>A task-list marker is a checked flag and nothing else, so there is no state in which the
	/// default renders nothing and no content that could carry markup.</remarks>
	protected override MString RenderTaskList(TaskList task) =>
		Template("TASKLIST", Args(Flag(task.Checked))) ?? base.RenderTaskList(task);

	/// <summary>
	/// <c>RENDERMARKUP`TABLE</c>: <c>%0</c> is the table described as one JSON object, so a template can
	/// lay the table out itself:
	/// <c>{"width":78,"align":["&lt;","&gt;"],"widths":[36,36],"head":["A","B"],
	/// "rows":[["1","2"],["3","4"]]}</c>.
	/// </summary>
	/// <remarks>
	/// Cells carry their markdown <em>source</em>, not rendered text: a cell reading <c>**loud**</c>
	/// arrives with its asterisks, and a template calls <c>rendermarkdown()</c> on it when it wants
	/// formatting. That keeps the decision with the game, and it is why JSON is safe here — the payload
	/// is authored article source, not rendered output with ANSI in it. <c>\|</c> escapes are left
	/// exactly as written, because <c>rendermarkdown()</c> is what resolves them.
	/// <c>widths</c> is the one thing a template could not work out for itself: source length is not
	/// rendered length, and only this side has both the cell and the rendering rules. Without it every
	/// template would divide the total by the column count and produce uniform columns — worse than the
	/// default layout it is replacing.
	/// <para>
	/// One object rather than parallel arguments because it nests: <c>json_map()</c> walks
	/// <c>rows</c> and then each row, so a per-row and a per-cell helper attribute compose instead of
	/// threading five arguments through every call by hand.
	/// </para>
	/// <para>
	/// <c>width</c> is the width <em>this render</em> was asked for, which is not necessarily the
	/// reader's <c>width(%#)</c>: a caller may have passed a fixed width for an export, or be rendering
	/// for an object that is not connected at all. A template computing its own budget would lay the
	/// table out to a different width than the prose around it, so the renderer's own value is carried
	/// here rather than left to be guessed at.
	/// </para>
	/// <para>
	/// The template is looked up before any of this is built, so a game with no TABLE template pays
	/// nothing per table.
	/// </para>
	/// </remarks>
	protected override MString RenderTable(Table table)
	{
		// Both conditions are required and both are cheap: no template means no payload, and no retained
		// source means no way to quote a cell. The second cannot happen through rendermarkdowncustom(),
		// which always enters via RenderMarkdown(string).
		if (_markdownSource is null || !HasTemplate("TABLE")) return base.RenderTable(table);

		// Cells are collected twice on purpose. The payload carries source, but the column widths can
		// only be measured from the rendering: "**index**" is nine characters of source and five on
		// screen, so a template measuring what it is given would be wrong by the length of the markup.
		var rows = table
			.OfType<TableRow>()
			.Select(row =>
			{
				var cells = row.OfType<TableCell>().ToArray();
				return (
					row.IsHeader,
					Source: cells.Select(CellSource).ToArray(),
					Rendered: (IReadOnlyList<MString>)cells.Select(RenderTableCell).ToArray());
			})
			.ToList();

		if (rows.Count == 0) return base.RenderTable(table);

		var columns = rows.Max(row => row.Source.Length);

		// No column count of its own: align and widths both carry exactly one entry per column, and a
		// third field saying the same number is a third thing that can disagree with the other two.
		// A template that wants it reads json_query(json_query(%0,get,widths),size).
		var payload = new
		{
			width = MaxWidth,
			// align()'s own justification characters, not letters: every template feeds these straight
			// into an align()/lalign() width spec, so translating here once keeps the same switch() out
			// of every template. Nothing wants them as l/c/r.
			align = Enumerable.Range(0, columns).Select(column =>
				table.ColumnDefinitions.Count > column
					? table.ColumnDefinitions[column].Alignment switch
					{
						TableColumnAlign.Center => "-",
						TableColumnAlign.Right => ">",
						_ => "<"
					}
					: "<").ToArray(),
			// The same widths the built-in layout would use, computed from the rendered cells.
			widths = ComputeColumnWidths(
				rows.Select(row => row.Rendered).ToList(), columns, TableContentWidth(columns)),
			head = rows.FirstOrDefault(row => row.IsHeader).Source ?? [],
			rows = rows.Where(row => !row.IsHeader).Select(row => row.Source).ToArray()
		};

		// Serialised with the options json() uses, so a template meets one JSON dialect whichever
		// function produced the document it is reading.
		var custom = Template("TABLE", Args(MModule.single(
			JsonSerializer.Serialize(payload, JsonHelpers.RelaxedJsonOptions))));

		return custom ?? base.RenderTable(table);
	}

	/// <summary>
	/// One cell's markdown source, trimmed of the padding spaces the table syntax puts around it.
	/// </summary>
	/// <remarks>
	/// Markdig pads a row shorter than the table's column count with cells that carry no source span at
	/// all (<c>Start 0</c>, <c>End -1</c>). Those answer as empty strings, so a short row arrives padded
	/// to <c>columns</c> rather than as a shorter array — trimming them back off would be a guess about
	/// which trailing empties the author wrote and which the parser added.
	/// The bounds are re-checked against the source rather than trusted, because a wrong span here would
	/// be an <see cref="ArgumentOutOfRangeException"/> in the middle of a render.
	/// </remarks>
	private string CellSource(TableCell cell)
	{
		var span = cell.Span;
		if (_markdownSource is null || span.Start < 0 || span.Length <= 0) return string.Empty;

		var end = Math.Min(span.End, _markdownSource.Length - 1);
		return end < span.Start ? string.Empty : _markdownSource[span.Start..(end + 1)].Trim();
	}

	/// <summary>
	/// <c>RENDERMARKUP`CONTAINER</c>: <c>%0</c> directive name, <c>%1</c> its arguments, <c>%2</c> the
	/// container's rendered contents.
	/// </summary>
	/// <remarks>
	/// This is the hook a game needs to make the wiki's live-listing directives (<c>::: category lore</c>)
	/// mean something in-game — the built-in rendering can only say "see the web portal", but softcode
	/// holding <c>%0</c> and <c>%1</c> can call <c>wikilist()</c> and print the listing for real.
	/// </remarks>
	protected override MString RenderCustomContainer(CustomContainer container)
	{
		// Same normalisation the base renderer performs: depending on trivia tracking Markdig puts the
		// fence line in Info alone or splits it across Info/Arguments.
		var fenceLine = $"{container.Info} {container.Arguments}".Trim();
		var tokens = fenceLine.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		var name = tokens.Length > 0 ? tokens[0] : string.Empty;
		var arguments = tokens.Length > 1 ? tokens[1] : string.Empty;

		var contents = MModule.multipleWithDelimiter(MModule.single("\n"),
			container.Select(Render).Where(rendered => rendered.Length > 0).ToList());

		return Template("CONTAINER", Args(Text(name), Text(arguments), contents))
			?? base.RenderCustomContainer(container);
	}
}
