using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
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

	public CustomizableMarkdownRenderer(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		AnySharpObject templateObject,
		IAttributeService attributeService,
		int maxWidth = 78) : base(maxWidth)
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
				return null; // No custom template, use default
			}

			// Evaluate the attribute with the provided arguments
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
			// If template evaluation fails, fall back to default rendering
			return null;
		}
	}

	protected override MString RenderHeading(HeadingBlock heading)
	{
		// Extract level for template selection
		var templateName = heading.Level switch
		{
			1 => "H1",
			2 => "H2",
			3 => "H3",
			_ => "H3"
		};

		// Render the heading content (inline elements) without default formatting
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
}
