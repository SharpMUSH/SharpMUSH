using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// PennMUSH's <c>dbwalk</c> (src/fundb.c:687) and the <c>first_visible</c> filter it walks through
/// (src/predicat.c:292) — the single implementation behind <c>lcon</c>, <c>lexits</c>, <c>lplayers</c>,
/// <c>lthings</c>, their <c>lv*</c>/<c>x*</c>/<c>n*</c> forms, and <c>con</c>/<c>exit</c>/<c>next</c>.
/// </summary>
/// <remarks>
/// These were 27 separate bodies here, each listing a container's contents with no permission gate and
/// no visibility filter, so any object an executor could name gave up its complete contents including
/// DARK ones (issue #833). PennMUSH unified them into one function in 2001 for the same reason they had
/// drifted apart here — the comment above <c>dbwalk</c> records that <c>next()</c>, <c>con()</c> and
/// <c>exit()</c> had ended up with different permissions from <c>lcon()</c> and <c>lexits()</c> — and
/// the gate itself predates that, added by Amberyl because "mortals could get the contents of rooms
/// they didn't control, thus ... they could build a scanner to locate anything they wanted".
/// </remarks>
public partial class Functions
{
	/// <summary>PennMUSH's <c>TYPE_</c> mask, as <c>dbwalk</c> takes it.</summary>
	[Flags]
	private enum WalkType
	{
		Thing = 1,
		Player = 2,
		Exit = 4,
		Contents = Thing | Player
	}

	/// <summary><c>dbwalk</c>'s <c>listening</c> argument: <c>lcon(x,puppet)</c> and <c>lcon(x,listen)</c>.</summary>
	private enum WalkListening
	{
		None = 0,
		Puppet = 1,
		Listen = 2
	}

	/// <param name="Types">Which object types to keep.</param>
	/// <param name="SkipDark">Penn's <c>vis</c> — the <c>lv*</c> forms' extra filter, on top of the
	/// visibility filter every form gets.</param>
	/// <param name="Start">1-based window start, or 0 for "no window" (<c>x*</c> forms only).</param>
	/// <param name="Count">Window length, or 0 for "no window".</param>
	private readonly record struct WalkSpec(
		WalkType Types,
		bool SkipDark = false,
		int Start = 0,
		int Count = 0,
		WalkListening Listening = WalkListening.None);

	/// <summary>
	/// The gate: <c>Can_Examine(executor, loc) || Location(executor) == loc || enactor == loc</c>, with
	/// <c>validloc</c> ahead of it. Every form answers <c>#-1</c> when this fails, which is the whole
	/// reason it exists — see the class remarks.
	/// </summary>
	private static async ValueTask<bool> WalkGate(AnySharpObject executor, AnySharpObject enactor,
		AnySharpObject loc, WalkType types)
	{
		// validloc: a room for exits, anything that is not an exit for contents.
		var validLoc = types.HasFlag(WalkType.Exit) ? loc.IsRoom : !loc.IsExit;
		if (!validLoc) return false;

		if (await PermissionService!.CanExamine(executor, loc)) return true;

		var executorLocation = await executor.Where();
		if (executorLocation.Object().DBRef == loc.Object().DBRef) return true;

		return enactor.Object().DBRef == loc.Object().DBRef;
	}

	/// <summary>
	/// PennMUSH's <c>first_visible</c> (src/predicat.c:292), as a per-candidate predicate.
	/// </summary>
	/// <remarks>
	/// Penn's version is a loop with an <c>lck</c> latch, because it is called to skip forward over a
	/// contents list; the latch only avoids re-testing the location half while scanning past consecutive
	/// hidden objects, and <c>DOLIST_VISIBLE</c> calls it afresh (latch reset) for each item, so per
	/// candidate the two forms agree.
	/// <para>
	/// Penn documents its own bug here and keeps it, so this keeps it too: the <c>controls</c> escape
	/// means a DARK object <em>is</em> listed to someone who owns it. "The behavior is left as is because
	/// so many functions in fundb.c rely on the incorrect behavior to return expected values."
	/// </para>
	/// <para>
	/// <c>ldark</c> is <c>Opaque</c> for a player and <c>Dark</c> — not <c>DarkLegal</c> — for anything
	/// else, and the location half is asked about the container, not the candidate.
	/// </para>
	/// </remarks>
	private static async ValueTask<bool> FirstVisible(AnySharpObject executor, AnySharpObject loc,
		AnySharpObject thing, bool locIsDark)
	{
		if (!await PermissionService!.CanInteract(executor, thing, IPermissionService.InteractType.See))
		{
			return false;
		}

		var hidden = await thing.IsDarkLegal() || (locIsDark && !await thing.IsLight());
		if (!hidden) return true;

		return await executor.IsSee_All()
					 || loc.Object().DBRef == executor.Object().DBRef
					 || await PermissionService.Controls(executor, loc)
					 || await PermissionService.Controls(executor, thing);
	}

	/// <summary>
	/// Walks <paramref name="loc"/>'s contents as <c>dbwalk</c> does.
	/// </summary>
	/// <returns>
	/// The matching contents in order, or <c>null</c> when the gate refused — which every caller renders
	/// as <c>#-1</c>, exactly as <c>dbwalk</c>'s <c>else</c> branch does.
	/// </returns>
	private static async ValueTask<List<AnySharpContent>?> DbWalk(AnySharpObject executor,
		AnySharpObject enactor, AnySharpObject loc, WalkSpec spec)
	{
		if (!await WalkGate(executor, enactor, loc, spec.Types)) return null;

		// ldark = IsPlayer(loc) ? Opaque(loc) : Dark(loc)
		var locIsDark = loc.IsPlayer ? await loc.IsOpaque() : await loc.IsDark();
		var locIsLight = await loc.IsLight();
		var privWho = await executor.IsPriv() || await executor.HasPower("Who");

		var matched = new List<AnySharpContent>();
		var seen = 0;

		await foreach (var item in loc.AsContainer.Content(Mediator!))
		{
			var thing = item.WithRoomOption();

			if (!TypeMatches(item, spec.Types)) continue;
			if (!await FirstVisible(executor, loc, thing, locIsDark)) continue;

			// The lv* forms' extra pass: hide anything dark outright, and disconnected players.
			if (spec.SkipDark)
			{
				if (await thing.IsDark() && !await thing.IsLight() && !locIsLight) continue;
				if (spec.Types == WalkType.Player && !await ConnectionService!.IsConnected(thing)) continue;
			}

			switch (spec.Listening)
			{
				case WalkListening.Puppet when !await thing.IsPuppet():
					continue;
				case WalkListening.Listen when !(
					(await thing.IsHearer(ConnectionService!, AttributeService!) || await thing.IsListener())
					&& (privWho || !await thing.IsDark())):
					continue;
			}

			seen++;

			// Penn counts every match but only writes the requested window; the n* forms report the
			// full count, so the window must not narrow what we counted.
			if (spec.Count < 1 || (seen >= spec.Start && seen < spec.Start + spec.Count))
			{
				matched.Add(item);
			}
		}

		return matched;
	}

	private static bool TypeMatches(AnySharpContent item, WalkType types)
		=> (item.IsExit && types.HasFlag(WalkType.Exit))
			 || (item.IsPlayer && types.HasFlag(WalkType.Player))
			 || (item.IsThing && types.HasFlag(WalkType.Thing));

	/// <summary>
	/// Resolves argument 0 the way <c>match_thing</c> does — noisily, so the executor is told why —
	/// and hands the walk its location. A name that does not resolve answers a bare <c>#-1</c>:
	/// <c>dbwalk</c>'s <c>!GoodObject(loc)</c> branch writes exactly that, the reason having already
	/// gone to the executor.
	/// </summary>
	private static async ValueTask<(AnySharpObject Executor, AnySharpObject Enactor, AnySharpObject? Loc)>
		WalkTarget(IMUSHCodeParser parser, string name)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);
		var located = await LocateService!.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, name, LocateFlags.All);

		return (executor, enactor, located.IsError ? null : located.AsSharpObject);
	}

	private static string Arg(IMUSHCodeParser parser, string index)
		=> parser.CurrentState.Arguments[index].Message!.ToPlainText();

	private static string Render(IEnumerable<AnySharpContent> contents)
		=> string.Join(" ", contents.Select(x => x.Object().DBRef.ToString()));

	/// <summary>The <c>l*</c> and <c>lv*</c> forms: the whole matching list.</summary>
	private static async ValueTask<CallState> WalkList(IMUSHCodeParser parser, WalkType types, bool skipDark,
		WalkListening listening = WalkListening.None)
	{
		var (executor, enactor, loc) = await WalkTarget(parser, Arg(parser, "0"));
		if (loc is null) return new CallState(ErrorMessages.Returns.Nothing);

		var walked = await DbWalk(executor, enactor, loc, new WalkSpec(types, skipDark, Listening: listening));
		return new CallState(walked is null ? ErrorMessages.Returns.Nothing : Render(walked));
	}

	/// <summary>The <c>n*</c> and <c>nv*</c> forms: how many matched, ignoring any window.</summary>
	private static async ValueTask<CallState> WalkCount(IMUSHCodeParser parser, WalkType types, bool skipDark)
	{
		var (executor, enactor, loc) = await WalkTarget(parser, Arg(parser, "0"));
		if (loc is null) return new CallState(ErrorMessages.Returns.Nothing);

		var count = await DbWalkCount(executor, enactor, loc, new WalkSpec(types, skipDark));
		return new CallState(count?.ToString() ?? ErrorMessages.Returns.Nothing);
	}

	/// <summary>
	/// The <c>x*</c> and <c>xv*</c> forms: a 1-based window of the matching list. Penn rejects a
	/// non-integer with <c>#-1 ARGUMENT MUST BE INTEGER</c> and a start or count below 1 with
	/// <c>#-1 ARGUMENT OUT OF RANGE</c>, both before it matches anything.
	/// </summary>
	private static async ValueTask<CallState> WalkWindow(IMUSHCodeParser parser, WalkType types, bool skipDark)
	{
		if (!int.TryParse(Arg(parser, "1"), out var start) || !int.TryParse(Arg(parser, "2"), out var count))
		{
			return new CallState(ErrorMessages.Returns.Integer);
		}

		if (start < 1 || count < 1)
		{
			return new CallState(ErrorMessages.Returns.ArgRange);
		}

		var (executor, enactor, loc) = await WalkTarget(parser, Arg(parser, "0"));
		if (loc is null) return new CallState(ErrorMessages.Returns.Nothing);

		var walked = await DbWalk(executor, enactor, loc, new WalkSpec(types, skipDark, start, count));
		return new CallState(walked is null ? ErrorMessages.Returns.Nothing : Render(walked));
	}

	/// <summary>
	/// <c>con()</c> and <c>exit()</c>: the first match, or <c>#-1</c>. Penn runs the identical walk and
	/// prints <c>safe_dbref</c> of its result, which is <c>#-1</c> for <c>NOTHING</c> — so a refused
	/// gate and an empty container are the same answer here, as they are there.
	/// </summary>
	private static async ValueTask<CallState> WalkFirst(IMUSHCodeParser parser, WalkType types)
	{
		var (executor, enactor, loc) = await WalkTarget(parser, Arg(parser, "0"));
		if (loc is null) return new CallState(ErrorMessages.Returns.Nothing);

		var walked = await DbWalk(executor, enactor, loc, new WalkSpec(types));
		return new CallState(walked is { Count: > 0 }
			? walked[0].Object().DBRef.ToString()
			: ErrorMessages.Returns.Nothing);
	}

	/// <summary>
	/// <c>lcon()</c>'s optional second argument, which selects the type and the listening filter
	/// (<c>fun_dbwalker</c>, src/fundb.c:779). Each keyword may be abbreviated — Penn matches with
	/// <c>string_prefixe</c> — and anything else is <c>#-1</c>.
	/// </summary>
	/// <remarks>
	/// The argument was declared (<c>MaxArgs = 2</c>) and then ignored, so <c>lcon(here,players)</c>
	/// listed everything.
	/// </remarks>
	private static async ValueTask<CallState> WalkContentsWithFilter(IMUSHCodeParser parser)
	{
		if (!parser.CurrentState.Arguments.ContainsKey("1"))
		{
			return await WalkList(parser, WalkType.Contents, skipDark: false);
		}

		var keyword = Arg(parser, "1");
		if (string.IsNullOrEmpty(keyword)) return new CallState(ErrorMessages.Returns.Nothing);

		static bool Is(string keyword, string full)
			=> full.StartsWith(keyword, StringComparison.OrdinalIgnoreCase);

		return keyword switch
		{
			_ when Is(keyword, "player") => await WalkList(parser, WalkType.Player, skipDark: false),
			_ when Is(keyword, "object") || Is(keyword, "thing")
				=> await WalkList(parser, WalkType.Thing, skipDark: false),
			_ when Is(keyword, "connect") => await WalkList(parser, WalkType.Player, skipDark: true),
			_ when Is(keyword, "puppet")
				=> await WalkList(parser, WalkType.Thing, skipDark: false, WalkListening.Puppet),
			_ when Is(keyword, "listen")
				=> await WalkList(parser, WalkType.Contents, skipDark: false, WalkListening.Listen),
			_ => new CallState(ErrorMessages.Returns.Nothing)
		};
	}

	/// <summary>
	/// <c>next()</c>: the first match after <paramref name="parser"/>'s argument in the same walk, which
	/// is <c>dbwalk</c>'s <c>after</c> parameter. An exit walks its source room's exits; a thing or a
	/// player walks its location's contents; a room has no next.
	/// </summary>
	/// <remarks>
	/// The gate applies to the <em>location</em>, so <c>next()</c> on something in a room the executor
	/// can neither examine nor stand in answers <c>#-1</c> — which is the drift Penn's 2001 unification
	/// was fixing, and which had reappeared here.
	/// <para>
	/// An argument that is not itself visible in that walk has no successor and answers <c>#-1</c>:
	/// Penn only starts filling <c>result</c> once it has passed <c>after</c> in the visible list.
	/// </para>
	/// </remarks>
	private static async ValueTask<CallState> WalkNext(IMUSHCodeParser parser)
	{
		var (executor, enactor, it) = await WalkTarget(parser, Arg(parser, "0"));
		if (it is null || it.IsRoom) return new CallState(ErrorMessages.Returns.Nothing);

		var types = it.IsExit ? WalkType.Exit : WalkType.Contents;
		var loc = (await it.Where()).WithExitOption();

		var walked = await DbWalk(executor, enactor, loc, new WalkSpec(types));
		if (walked is null) return new CallState(ErrorMessages.Returns.Nothing);

		var index = walked.FindIndex(x => x.Object().DBRef == it.Object().DBRef);
		return new CallState(index >= 0 && index + 1 < walked.Count
			? walked[index + 1].Object().DBRef.ToString()
			: ErrorMessages.Returns.Nothing);
	}

	/// <summary>
	/// The whole count of matches, which is what the <c>n*</c> forms report — <c>dbwalk</c>'s
	/// <c>retcount</c>, not the size of any window.
	/// </summary>
	private static async ValueTask<int?> DbWalkCount(AnySharpObject executor, AnySharpObject enactor,
		AnySharpObject loc, WalkSpec spec)
		=> (await DbWalk(executor, enactor, loc, spec with { Start = 0, Count = 0 }))?.Count;
}
