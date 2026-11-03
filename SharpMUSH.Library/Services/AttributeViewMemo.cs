using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

/// <summary>
/// The per-object half of <see cref="Interfaces.IPermissionService.CanViewAttribute(AnySharpObject, AnySharpObject, LazySharpAttribute[])"/>,
/// remembered for one listing. Whether the viewer may examine the object, could look at it, or is
/// privileged does not depend on the attribute, so a listing of thousands of attributes on one object
/// asks each once rather than once per attribute (<c>lattr</c>, <c>nattr</c>, <c>xattr</c> and the
/// other pattern reads, and <c>examine</c>). The attribute's own flags are still tested every time.
/// </summary>
/// <remarks>
/// Per listing, never shared: it holds the answers of one read, not a cache a write would have to
/// invalidate. Not thread-safe; a listing is one asynchronous flow.
/// </remarks>
public sealed class AttributeViewMemo
{
	private Dictionary<DBRef, bool>? _privileged;
	private Dictionary<(DBRef Viewer, DBRef Target), bool>? _examine;
	private Dictionary<(DBRef Viewer, DBRef Target), bool>? _look;

	/// <summary>Whether <paramref name="viewer"/> is privileged, asked once per <paramref name="memo"/>; without one, asked now.</summary>
	internal static ValueTask<bool> PrivilegedAsync(AttributeViewMemo? memo, AnySharpObject viewer,
		Func<AnySharpObject, ValueTask<bool>> compute)
		=> memo is null ? compute(viewer) : RememberAsync(memo._privileged ??= [], viewer.Object().DBRef, viewer, compute);

	/// <summary>Whether <paramref name="viewer"/> may examine <paramref name="target"/>, asked once per <paramref name="memo"/>.</summary>
	internal static ValueTask<bool> CanExamineAsync(AttributeViewMemo? memo, AnySharpObject viewer, AnySharpObject target,
		Func<AnySharpObject, AnySharpObject, ValueTask<bool>> compute)
		=> memo is null ? compute(viewer, target) : RememberAsync(memo._examine ??= [], viewer, target, compute);

	/// <summary>Whether <paramref name="viewer"/> could look at <paramref name="target"/>, asked once per <paramref name="memo"/>.</summary>
	internal static ValueTask<bool> CanLookAtAsync(AttributeViewMemo? memo, AnySharpObject viewer, AnySharpObject target,
		Func<AnySharpObject, AnySharpObject, ValueTask<bool>> compute)
		=> memo is null ? compute(viewer, target) : RememberAsync(memo._look ??= [], viewer, target, compute);

	private static async ValueTask<bool> RememberAsync(Dictionary<DBRef, bool> answers, DBRef key, AnySharpObject viewer,
		Func<AnySharpObject, ValueTask<bool>> compute)
	{
		if (answers.TryGetValue(key, out var known))
		{
			return known;
		}

		var answer = await compute(viewer);
		answers[key] = answer;
		return answer;
	}

	private static async ValueTask<bool> RememberAsync(Dictionary<(DBRef Viewer, DBRef Target), bool> answers,
		AnySharpObject viewer, AnySharpObject target, Func<AnySharpObject, AnySharpObject, ValueTask<bool>> compute)
	{
		var key = (viewer.Object().DBRef, target.Object().DBRef);
		if (answers.TryGetValue(key, out var known))
		{
			return known;
		}

		var answer = await compute(viewer, target);
		answers[key] = answer;
		return answer;
	}
}
