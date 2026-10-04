using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Common;

/// <summary>
/// The gate on PennMUSH's <c>"&lt;object&gt;/&lt;attr&gt; - Set."</c> and <c>"- Cleared."</c> reports.
/// </summary>
/// <remarks>
/// <c>do_set_atr</c> reports a write only when asked to (<c>flags &amp; 0x01</c>) and
/// <c>!AreQuiet(player, thing)</c>, and then only if the attribute it finds afterwards — through
/// <c>atr_get</c>, which walks the parent chain — is not itself <c>AF_QUIET</c>
/// (<c>src/attrib.c:2446-2451</c>). Every spelling that reaches it shares that gate:
/// <c>&amp;attr</c> (<c>command_atrset</c>, <c>src/cmds.c:1790</c>), <c>@set obj=attr:value</c>,
/// <c>attrib_set()</c> and the standard-attribute commands (<c>@desc</c>, <c>@succ</c>, ...).
/// </remarks>
public static class AttributeWriteReport
{
	public static async ValueTask<bool> IsSuppressedAsync(IAttributeService attributeService, AnySharpObject executor,
		AnySharpObject thing, string attribute)
	{
		if (await thing.Object().AreQuietAsync(executor))
		{
			return true;
		}

		var after = await attributeService.GetAttributeAsync(executor, thing, attribute,
			IAttributeService.AttributeMode.Read, parent: true);
		return after is SharpAttribute[] chain && chain.Length > 0 && chain.Last().IsQuiet();
	}
}
