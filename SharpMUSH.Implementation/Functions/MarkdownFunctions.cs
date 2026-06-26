using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

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
	public static ValueTask<CallState> RenderMarkdown(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		var markdown = "";
		if (args.TryGetValue("0", out var markdownArg))
		{
			markdown = markdownArg.Message!.ToPlainText();
		}

		var width = 78;
		if (args.TryGetValue("1", out var widthArg))
		{
			var widthStr = widthArg.Message!.ToPlainText();
			if (!int.TryParse(widthStr, out width) || width < 10 || width > 1000)
			{
				return ValueTask.FromResult(new CallState(ErrorMessages.Returns.InvalidWidth));
			}
		}

		try
		{
			var result = RecursiveMarkdownHelper.RenderMarkdown(markdown, width, parser);
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
	public static async ValueTask<CallState> RenderMarkdownCustom(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var markdown = "";
		if (args.TryGetValue("0", out var markdownArg))
		{
			markdown = markdownArg.Message!.ToPlainText();
		}

		var templateObjRef = args["1"].Message!.ToPlainText();

		var width = 78;
		if (args.TryGetValue("2", out var widthArg))
		{
			var widthStr = widthArg.Message!.ToPlainText();
			if (!int.TryParse(widthStr, out width) || width < 10 || width > 1000)
			{
				return new CallState(ErrorMessages.Returns.InvalidWidth);
			}
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, templateObjRef, LocateFlags.All,
			async templateObj =>
			{
				try
				{
					var customRenderer = new CustomizableMarkdownRenderer(
						parser, executor, templateObj, AttributeService!, width);
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
