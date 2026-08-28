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

	public MString RenderMarkdown(string markdown)
	{
		var result = RecursiveMarkdownHelper.RenderMarkdown(markdown, this);
		return result;
	}

	/// <summary>
	/// Try to get and evaluate a custom template attribute
	/// </summary>
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

	/// <summary>Runs <paramref name="templateName"/>, or <c>null</c> when the object does not define it.</summary>
	private MString? Template(string templateName, Dictionary<string, CallState> args) =>
		TryEvaluateTemplate(templateName, args).GetAwaiter().GetResult();

	protected override MString RenderBold(MString content) =>
		Template("BOLD", Args(content)) ?? base.RenderBold(content);

	protected override MString RenderItalic(MString content) =>
		Template("ITALIC", Args(content)) ?? base.RenderItalic(content);

	protected override MString RenderUnderline(MString content) =>
		Template("UNDERLINE", Args(content)) ?? base.RenderUnderline(content);

	protected override MString RenderInlineCode(CodeInline code) =>
		Template("INLINECODE", Args(Text(code.Content))) ?? base.RenderInlineCode(code);

	/// <summary>
	/// <c>RENDERMARKUP`LINK</c>: <c>%0</c> link text, <c>%1</c> URL or command, <c>%2</c> whether the
	/// source already marked it a command, <c>%3</c> the link title/hint.
	/// </summary>
	/// <remarks>
	/// <c>%2</c> is what makes the template usable: a help-topic shortcut (<c>[topic]</c>) already
	/// carries a command in <c>%1</c> rather than a URL, and a template that wrapped it in an
	/// <c>xch_cmd</c> and a plain <c>https:</c> link in an <c>href</c> has to be able to tell them apart.
	/// Images are not links and keep going to <c>IMAGE</c>.
	/// </remarks>
	protected override MString RenderLink(LinkInline link, MString content)
	{
		if (link.IsImage) return base.RenderLink(link, content);

		var url = link.Url ?? string.Empty;
		// The text the default rendering would show, so a template never has to reproduce the
		// "empty link text falls back to the URL" rule for itself.
		var text = string.IsNullOrWhiteSpace(content.ToPlainText()) ? MModule.single(url) : content;
		var isCommand = link.GetData(HelpTopicInlineParser.CommandDataKey) is true;

		return Template("LINK", Args(text, Text(url), Flag(isCommand), Text(link.Title)))
			?? base.RenderLink(link, content);
	}

	/// <summary>
	/// <c>RENDERMARKUP`IMAGE</c>: <c>%0</c> alt text, <c>%1</c> image URL.
	/// </summary>
	protected override MString RenderImage(LinkInline link, MString content) =>
		Template("IMAGE", Args(content, Text(link.Url))) ?? base.RenderImage(link, content);

	/// <summary>
	/// <c>RENDERMARKUP`WIKILINK</c>: <c>%0</c> display text, <c>%1</c> the <c>@wiki</c> page reference,
	/// <c>%2</c> the target page's title.
	/// </summary>
	/// <remarks>
	/// <c>%1</c> is the fully qualified <c>namespace:category:slug</c> form, so a template can build
	/// <c>@wiki %1</c> and get a working command — which is what <c>@wiki</c> itself renders a wiki link
	/// as. Falling through to <c>base</c> rather than to a fixed rendering keeps whatever default the
	/// surface has: neutral prose here, the command link under <c>@wiki</c>.
	/// </remarks>
	protected override MString RenderWikiLink(WikiLinkInline wikiLink)
	{
		var display = wikiLink.DisplayText ?? wikiLink.Title;
		var reference = WikiCommandHelper.ReferenceForWikiLink(wikiLink);

		return Template("WIKILINK", Args(Text(display), Text(reference), Text(wikiLink.Title)))
			?? base.RenderWikiLink(wikiLink);
	}

	/// <summary><c>RENDERMARKUP`AUTOLINK</c>: <c>%0</c> the URL, which is also the text shown.</summary>
	protected override MString RenderAutolink(AutolinkInline autolink) =>
		Template("AUTOLINK", Args(Text(autolink.Url))) ?? base.RenderAutolink(autolink);

	/// <summary><c>RENDERMARKUP`TASKLIST</c>: <c>%0</c> is 1 when the box is ticked.</summary>
	protected override MString RenderTaskList(TaskList task) =>
		Template("TASKLIST", Args(Flag(task.Checked))) ?? base.RenderTaskList(task);

	/// <summary>
	/// <c>RENDERMARKUP`TABLE</c>: <c>%0</c> is the whole table, already laid out in columns.
	/// </summary>
	/// <remarks>
	/// The table is handed over rendered rather than as its cells: column widths are computed across
	/// every row at once against the render width, so there is no per-cell hook that could produce an
	/// aligned table. A template can therefore frame, indent or colour a table, not re-lay it out.
	/// </remarks>
	protected override MString RenderTable(Table table)
	{
		var rendered = base.RenderTable(table);
		return Template("TABLE", Args(rendered)) ?? rendered;
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
