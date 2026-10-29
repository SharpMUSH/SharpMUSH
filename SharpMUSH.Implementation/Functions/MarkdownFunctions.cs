using Markdig.Syntax;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Implementation.Commands.WikiCommand;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
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
	public async ValueTask<CallState> RenderMarkdown(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		var markdown = "";
		if (args.TryGetValue("0", out var markdownArg))
		{
			markdown = markdownArg.Message.ToPlainText();
		}

		var width = 78;
		if (args.TryGetValue("1", out var widthArg))
		{
			var widthStr = widthArg.Message.ToPlainText();
			if (!int.TryParse(widthStr, out width) || width < 10 || width > 1000)
			{
				return new CallState(ErrorMessages.Returns.InvalidWidth);
			}
		}

		try
		{
			var document = RecursiveMarkdownHelper.Parse(markdown);
			var renderer = new RecursiveMarkdownRenderer(width, parser)
			{
				ImageAllowed = await MarkdownImagePolicy(executor),
				LiveBlocks = await MarkdownLiveBlocks(parser, executor, document, width),
			};
			return new CallState(renderer.Render(document));
		}
		catch (Exception ex)
		{
			return new CallState(string.Format(ErrorMessages.Returns.MarkdownRenderErrorFormat, ex.Message));
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
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var markdown = "";
		if (args.TryGetValue("0", out var markdownArg))
		{
			markdown = markdownArg.Message.ToPlainText();
		}

		var templateObjRef = args["1"].Message.ToPlainText();

		var width = 78;
		if (args.TryGetValue("2", out var widthArg))
		{
			var widthStr = widthArg.Message.ToPlainText();
			if (!int.TryParse(widthStr, out width) || width < 10 || width > 1000)
			{
				return new CallState(ErrorMessages.Returns.InvalidWidth);
			}
		}

		var imageAllowed = await MarkdownImagePolicy(executor);
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, templateObjRef, LocateFlags.All,
			async templateObj =>
			{
				try
				{
					var document = RecursiveMarkdownHelper.Parse(markdown);
					var liveBlocks = await MarkdownLiveBlocks(parser, executor, document, width);
					var customRenderer = new CustomizableMarkdownRenderer(
						parser, executor, templateObj, AttributeService, width)
					{ ImageAllowed = imageAllowed, LiveBlocks = liveBlocks };
					var result = customRenderer.RenderMarkdown(markdown, document);
					return new CallState(result);
				}
				catch (Exception ex)
				{
					return new CallState(string.Format(ErrorMessages.Returns.MarkdownRenderErrorFormat, ex.Message));
				}
			});
	}

	/// <summary>
	/// Which of a markdown image's addresses render as a picture: the ones <c>image()</c> would show for
	/// <paramref name="executor"/>. Without Send_Image the markdown's images stay their placeholders, so
	/// <c>rendermarkdown()</c> is no way around what <c>image()</c> asks.
	/// </summary>
	/// <summary>
	/// What the wiki's live listing blocks in <paramref name="document"/> show <paramref name="executor"/>: the pages its
	/// softcode may reach, as <c>wikicategory()</c>, <c>wikilist()</c>, <c>wikirecent()</c> and <c>wikiquery()</c> return
	/// them, under their titles in its locale. The engine draws the listing; softcode never reads a page's text through it.
	/// </summary>
	private async ValueTask<IReadOnlyDictionary<string, MString>> MarkdownLiveBlocks(IMUSHCodeParser parser, AnySharpObject executor,
		MarkdownDocument document, int width)
		=> await LiveBlocksWiki.AnswerAsync(parser.HouseLook(), parser.ServiceProvider.GetRequiredService<IWikiService>(),
			parser.ServiceProvider.GetRequiredService<IWikiLocalizationService>(), document,
			await WikiCommandHelper.SoftcodeVisibilityAsync(parser, executor), width,
			await WikiCommandHelper.ResolveExecutorLocaleAsync(parser, executor), forceSource: false);

	private async ValueTask<Func<string, bool>?> MarkdownImagePolicy(AnySharpObject executor)
		=> await CanSendImage(executor) ? ImageAllowed : null;
}
