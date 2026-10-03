using DotNext.Threading;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Extensions;

public static class LazySharpAttributeExtensions
{
	/// <summary>
	/// The attribute's value, read for one test and then released. A content scan (<c>grep</c>,
	/// <c>wildgrep</c>, <c>regrep</c>, <c>@grep</c>) holds the whole matched set of attributes while it
	/// streams through them, so a body loaded through <see cref="LazySharpAttribute.Value"/> would stay
	/// alive with its attribute until the scan ends — every body the scan read, all at once. Resetting the
	/// lazy value after the read keeps one body alive at a time. A later reader of the same attribute
	/// reads its value again; a value source that cannot be reset simply keeps it.
	/// </summary>
	public static async ValueTask<MString> ReadValueOnceAsync(this LazySharpAttribute attribute, CancellationToken cancellationToken)
	{
		var value = await attribute.Value.WithCancellation(cancellationToken);
		attribute.Value.Reset();
		return value;
	}

	/// <inheritdoc cref="SharpAttributeExtensions.SyntaxParseType(SharpAttribute)"/>
	public static ParseType? SyntaxParseType(this LazySharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name.Equals("cmdsyntax", StringComparison.OrdinalIgnoreCase)) ? ParseType.CommandList
			: attribute.Flags.Any(x => x.Name.Equals("funsyntax", StringComparison.OrdinalIgnoreCase)) ? ParseType.Function
			: null;
}
