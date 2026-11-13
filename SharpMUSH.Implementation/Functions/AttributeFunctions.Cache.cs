using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>How long a <c>cache()</c> entry is fresh when no window is given: an hour, as in the PennMUSH patch.</summary>
	private const long DefaultCacheWindowSeconds = 3600;

	/// <summary>
	/// <c>cache(&lt;object&gt;, &lt;attribute&gt;, &lt;value&gt;[, &lt;window&gt;[, &lt;sliding&gt;]])</c>, after the PennMUSH patch for
	/// issue 1326: the attribute's value while it was written less than &lt;window&gt; seconds ago, else &lt;value&gt;
	/// evaluated, written to the attribute and returned. &lt;value&gt; is evaluated only on a miss. The time is the
	/// attribute's own modification time, so the attribute holds just the value. A true &lt;sliding&gt; writes the value
	/// again on a hit, which moves the time on, so an entry read often enough never expires.
	/// </summary>
	/// <remarks>
	/// The executor must control the object, checked before anything is read or evaluated; the write itself goes through
	/// the attribute service like any other set. A value the store refuses (too long, a locked attribute) is still
	/// returned, so the call answers correctly and simply misses again next time. Nothing is printed.
	/// </remarks>
	[SharpFunction(Name = "cache", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.NoParse | FunctionFlags.HasSideFX,
		ParameterNames = ["object", "attribute", "value", "window", "sliding"])]
	public async ValueTask<CallState> Cache(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentList;
		var objectArg = (await args[0].GetParsedResultAsync()).Message.ToPlainText();
		var attribute = (await args[1].GetParsedResultAsync()).Message.ToPlainText().Trim().ToUpperInvariant();

		var window = DefaultCacheWindowSeconds;
		if (args.TryGet(3, out var windowArg)
				&& !long.TryParse((await windowArg.GetParsedResultAsync()).Message.ToPlainText().Trim(), out window))
		{
			return new CallState(ErrorMessages.Returns.Integer);
		}

		if (window < 0)
		{
			return new CallState(ErrorMessages.Returns.UInteger);
		}

		var sliding = args.TryGet(4, out var slidingArg) && (await slidingArg.GetParsedResultAsync()).Message.Truthy(parser);
		if (attribute.Length == 0)
		{
			return new CallState(ErrorMessages.Returns.BadAttrName);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, objectArg, LocateFlags.All) switch
		{
			AnySharpObject target => await CacheOnAsync(parser, executor, target, attribute, window, sliding),
			_ => new CallState(ErrorMessages.Returns.NotVisible)
		};
	}

	private async ValueTask<CallState> CacheOnAsync(IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject target,
		string attribute, long window, bool sliding)
	{
		if (!await PermissionService.Controls(executor, target))
		{
			return new CallState(ErrorMessages.Returns.AttrSetPermissions);
		}

		var now = parser.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeMilliseconds();
		var stored = await AttributeService.GetAttributeAsync(executor, target, attribute,
			mode: IAttributeService.AttributeMode.Read, parent: false);
		// A bad name, or a copy the executor may not read, answers before <value> is evaluated.
		if (stored is Error<string> refused)
		{
			return new CallState(refused.Value);
		}

		// A time ahead of the clock (written before it was set back) is a miss, so it cannot stretch the window.
		if (stored is SharpAttribute[] chain && chain[^1] is { ModifiedTime: { } modified } entry
				&& window > 0 && now >= modified && (now - modified) / 1000 < window)
		{
			if (sliding)
			{
				await AttributeService.SetAttributeAsync(executor, target, attribute, entry.Value);
			}

			return new CallState(entry.Value);
		}

		var value = await parser.CurrentState.ArgumentList[2].GetParsedResultAsync();
		await AttributeService.SetAttributeAsync(executor, target, attribute, value.Message);
		return value;
	}
}
