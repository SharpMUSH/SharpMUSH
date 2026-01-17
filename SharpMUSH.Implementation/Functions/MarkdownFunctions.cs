using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using static MarkupString.MarkupImplementation;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// RENDERMARKDOWN(markdown[, width])
	/// Renders CommonMark/Markdown text into SharpMUSH MarkupString with ANSI formatting
	/// </summary>
	/// <param name="parser">The MUSH code parser</param>
	/// <param name="_2">Function attribute metadata</param>
	/// <returns>Rendered markdown as MarkupString</returns>
	[SharpFunction(Name = "rendermarkdown", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["text"])]
	public ValueTask<CallState> RenderMarkdown(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		
		// Get markdown text, default to empty
		var markdown = "";
		if (args.TryGetValue("0", out var markdownArg))
		{
			markdown = markdownArg.Message!.ToPlainText();
		}
		
		// Get width parameter, default to 78
		var width = 78;
		if (args.TryGetValue("1", out var widthArg))
		{
			var widthStr = widthArg.Message!.ToPlainText();
			if (!int.TryParse(widthStr, out width) || width < 10 || width > 1000)
			{
				return ValueTask.FromResult(new CallState("#-1 INVALID WIDTH (must be 10-1000)"));
			}
		}
		
		try
		{
			var result = RecursiveMarkdownHelper.RenderMarkdown(markdown, width);
			return ValueTask.FromResult(new CallState(result));
		}
		catch (Exception ex)
		{
			return ValueTask.FromResult(new CallState($"#-1 ERROR RENDERING MARKDOWN: {ex.Message}"));
		}
	}
	
	/// <summary>
	/// RENDERMARKDOWNCUSTOM(markdown, object[, width])
	/// Renders CommonMark/Markdown text using custom attribute templates on the specified object
	/// </summary>
	/// <param name="parser">The MUSH code parser</param>
	/// <param name="_2">Function attribute metadata</param>
	/// <returns>Rendered markdown as MarkupString with custom templates applied</returns>
	[SharpFunction(Name = "rendermarkdowncustom", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["text"])]
	public async ValueTask<CallState> RenderMarkdownCustom(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		
		// Get markdown text
		var markdown = "";
		if (args.TryGetValue("0", out var markdownArg))
		{
			markdown = markdownArg.Message!.ToPlainText();
		}
		
		// Get template object
		var templateObjRef = args["1"].Message!.ToPlainText();
		
		// Get width parameter, default to 78
		var width = 78;
		if (args.TryGetValue("2", out var widthArg))
		{
			var widthStr = widthArg.Message!.ToPlainText();
			if (!int.TryParse(widthStr, out width) || width < 10 || width > 1000)
			{
				return new CallState("#-1 INVALID WIDTH (must be 10-1000)");
			}
		}
		
		// Locate the template object
		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, templateObjRef, LocateFlags.All,
			async templateObj =>
			{
				try
				{
					// Create custom renderer with template object
					var customRenderer = new CustomizableMarkdownRenderer(
						parser, executor, templateObj, _attributeService!, width);
					var result = customRenderer.RenderMarkdown(markdown);
					return new CallState(result);
				}
				catch (Exception ex)
				{
					return new CallState($"#-1 ERROR RENDERING MARKDOWN: {ex.Message}");
				}
			});
	}
}
