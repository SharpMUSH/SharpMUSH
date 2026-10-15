using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

/// <summary>
/// What an attribute index hit's <see cref="IndexedAttributeFacts"/> already say about whether one viewer may read the
/// copy that put it there: the cheap half of
/// <see cref="Interfaces.IPermissionService.CanViewAttribute(AnySharpObject, AnySharpObject, AttributeViewMemo, SharpAttribute[])"/>,
/// so a query that finds thousands of copies a mortal may not read turns most of them away without loading them.
/// </summary>
/// <remarks>
/// It follows <c>CanViewAttributeAsync</c> step by step and answers <see cref="IndexedAttributeVerdict.Check"/>
/// whenever the facts cannot settle a step, so it never answers differently from the full test:
/// <list type="number">
/// <item>An <c>internal</c> copy is hidden from everyone (and never indexed).</item>
/// <item>A <c>mortal_dark</c> level hides the copy from all but wizards and God.</item>
/// <item>A viewer that may examine the object reads it. A wizard (God included) may examine everything, and so may
/// any <c>See_All</c> viewer; otherwise examining needs control or the object's <c>VISUAL</c> flag and examine lock.
/// Control is possible only through the routes <c>Controls</c> takes: the viewer is the object, holds
/// <c>control.all</c>, has the object's owner (<c>Owns</c>), or reaches it through its zone, its owner's
/// <c>SHARED</c> zone lock or its own control lock. A hit with any of those, or with <c>VISUAL</c>, is checked.</item>
/// <item>Otherwise the copy is read only when every level is <c>visual</c>; a <c>nearby</c> level makes that depend
/// on where the viewer is, which is checked.</item>
/// </list>
/// </remarks>
public sealed class IndexedAttributeVisibility
{
	private readonly int _viewer;
	private readonly int _viewerOwner;
	private readonly bool _wizard;
	private readonly bool _seeAll;
	private readonly bool _controlsAll;

	private IndexedAttributeVisibility(int viewer, int viewerOwner, bool wizard, bool seeAll, bool controlsAll)
	{
		_viewer = viewer;
		_viewerOwner = viewerOwner;
		_wizard = wizard;
		_seeAll = seeAll;
		_controlsAll = controlsAll;
	}

	/// <summary>Reads what the verdicts need of <paramref name="viewer"/>, once per query.</summary>
	public static async ValueTask<IndexedAttributeVisibility> ForAsync(AnySharpObject viewer, CancellationToken cancellationToken)
	{
		var flags = await viewer.ReadFlagsAsync(cancellationToken);
		var owner = await viewer.Object().Owner.WithCancellation(cancellationToken);
		return new IndexedAttributeVisibility(viewer.Object().Key, owner.Object.Key, await viewer.IsWizard(cancellationToken),
			await viewer.IsSee_All(flags), flags.ControlsAll);
	}

	/// <summary>Whether every hit is visible to this viewer, so a query need not read any facts.</summary>
	public bool SeesEverything => _wizard;

	/// <summary>Whether <paramref name="hit"/>'s copy is visible, hidden, or needs the full test.</summary>
	public IndexedAttributeVerdict Judge(IndexedAttributeHit hit)
	{
		var facts = hit.Facts;
		if (facts.HasFlag(IndexedAttributeFacts.Internal))
		{
			return IndexedAttributeVerdict.Hidden;
		}

		if (_wizard)
		{
			return IndexedAttributeVerdict.Visible;
		}

		if (facts.HasFlag(IndexedAttributeFacts.MortalDark))
		{
			return IndexedAttributeVerdict.Hidden;
		}

		if (_seeAll || hit.Number == _viewer)
		{
			return IndexedAttributeVerdict.Visible;
		}

		var mayControl = _controlsAll
			|| hit.Owner == _viewerOwner
			|| hit.Owner < 0
			|| facts.HasFlag(IndexedAttributeFacts.HasControlLock)
			|| (!facts.HasFlag(IndexedAttributeFacts.IsPlayer)
				&& (facts.HasFlag(IndexedAttributeFacts.HasZone) || facts.HasFlag(IndexedAttributeFacts.OwnerShared)));
		if (mayControl || facts.HasFlag(IndexedAttributeFacts.ObjectVisual))
		{
			// Examining may be allowed; when it is not, an all-visual copy is still read without nearby.
			return facts.HasFlag(IndexedAttributeFacts.AllVisual) && !facts.HasFlag(IndexedAttributeFacts.AnyNearby)
				? IndexedAttributeVerdict.Visible
				: IndexedAttributeVerdict.Check;
		}

		if (!facts.HasFlag(IndexedAttributeFacts.AllVisual))
		{
			return IndexedAttributeVerdict.Hidden;
		}

		return facts.HasFlag(IndexedAttributeFacts.AnyNearby) ? IndexedAttributeVerdict.Check : IndexedAttributeVerdict.Visible;
	}
}

/// <summary>What <see cref="IndexedAttributeVisibility.Judge"/> decides about one hit.</summary>
public enum IndexedAttributeVerdict
{
	/// <summary>The viewer reads the copy.</summary>
	Visible,

	/// <summary>The viewer does not.</summary>
	Hidden,

	/// <summary>The facts cannot tell: load the object and the copy, and ask the permission service.</summary>
	Check
}
