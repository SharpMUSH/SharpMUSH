using SharpMUSH.Database;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// The zone functions TinyMUX and RhostMUSH have: a zone's members by type (<c>zchildren()</c>, <c>zrooms()</c> and the
/// rest), <c>lzone()</c>, <c>zlcon()</c>, <c>zsearch()</c>, <c>zonecmd()</c>, and the <c>zfun*()</c> family, which runs an
/// attribute of one of the caller's zones as the matching <c>u*()</c> function would.
/// </summary>
/// <remarks>A zone's members are the objects in it, first zone or further one, in dbref order.</remarks>
public partial class Functions
{
	/// <summary>How many entries a page of <c>lzone()</c> holds.</summary>
	private const int ZoneListPage = 400;

	[SharpFunction(Name = "zchildren", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["zone"])]
	public ValueTask<CallState> ZoneChildren(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ZoneMembersOfTypeAsync(parser, type: null);

	[SharpFunction(Name = "zrooms", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["zone"])]
	public ValueTask<CallState> ZoneRooms(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ZoneMembersOfTypeAsync(parser, DatabaseConstants.TypeRoom);

	[SharpFunction(Name = "zexits", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["zone"])]
	public ValueTask<CallState> ZoneExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ZoneMembersOfTypeAsync(parser, DatabaseConstants.TypeExit);

	[SharpFunction(Name = "zthings", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["zone"])]
	public ValueTask<CallState> ZoneThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ZoneMembersOfTypeAsync(parser, DatabaseConstants.TypeThing);

	/// <summary>
	/// <c>inzone(&lt;zone&gt;)</c>: the rooms in the zone, as zrooms(). <c>inzone(&lt;zone&gt;, &lt;object&gt;)</c>: 1 when the
	/// object is in the zone, else 0.
	/// </summary>
	[SharpFunction(Name = "inzone", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["zone", "object"])]
	public async ValueTask<CallState> InZone(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!parser.CurrentState.Arguments.ContainsKey("1"))
		{
			return await ZoneMembersOfTypeAsync(parser, DatabaseConstants.TypeRoom);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await LocateService.Locate(parser, executor, executor, ZoneArg(parser, "0"), LocateFlags.All) is not AnySharpObject zone
			|| await LocateService.Locate(parser, executor, executor, ZoneArg(parser, "1"), LocateFlags.All) is not AnySharpObject member
			|| !await PermissionService.CanExamine(executor, zone)
			|| !await PermissionService.CanExamine(executor, member))
		{
			return "0";
		}

		return (await Mediator.ZonesOfAsync(member)).Any(held => held.Object().DBRef.Number == zone.Object().DBRef.Number)
			? "1"
			: "0";
	}

	/// <summary>
	/// <c>lzone(&lt;object&gt;[, &lt;page&gt;[, &lt;objid&gt;]])</c>: the objects in the zone when the object has any, else the
	/// zones it is in.
	/// </summary>
	[SharpFunction(Name = "lzone", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object", "page", "objid"])]
	public async ValueTask<CallState> ListZone(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await LocateService.Locate(parser, executor, executor, ZoneArg(parser, "0"), LocateFlags.All) is not AnySharpObject target
			|| !await PermissionService.CanExamine(executor, target))
		{
			return ErrorMessages.Returns.Nothing;
		}

		var members = await ZoneMemberRefsAsync(target);
		var entries = members.Count > 0
			? members
			: [.. (await Mediator.ZonesOfAsync(target)).Select(zone => zone.Object().DBRef)];
		var page = ZoneArg(parser, "1").Trim();
		var objids = ZoneArgIsTrue(parser, "2");

		if (page.StartsWith('l'))
		{
			return $"{entries.Count / ZoneListPage + 1} {entries.Count}";
		}

		var shown = PageOf(entries, page);
		return string.Join(' ', shown.Select(entry => objids ? entry.ToString() : $"#{entry.Number}"));
	}

	/// <summary>
	/// <c>&lt;page&gt;</c> from 1, a page of <see cref="ZoneListPage"/>; <c>~&lt;first&gt;-&lt;count&gt;</c>, that many from
	/// the first'th; anything else, all of them.
	/// </summary>
	private static IEnumerable<DBRef> PageOf(IReadOnlyList<DBRef> entries, string page)
	{
		if (page.StartsWith('~') && page.IndexOf('-') is > 1 and var dash)
		{
			return int.TryParse(page[1..dash], out var first) && int.TryParse(page[(dash + 1)..], out var count)
				? entries.Skip(Math.Max(0, first - 1)).Take(Math.Max(0, count))
				: [];
		}

		return int.TryParse(page, out var number) && number > 0
			? entries.Skip((int)Math.Min(int.MaxValue, (long)(number - 1) * ZoneListPage)).Take(ZoneListPage)
			: entries;
	}

	/// <summary>
	/// <c>zlcon(&lt;zone&gt;[, &lt;type&gt;[, &lt;separator&gt;]])</c>: lcon() of each object in the zone that has contents,
	/// as one list. <c>&lt;type&gt;</c> is lcon()'s.
	/// </summary>
	[SharpFunction(Name = "zlcon", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["zone", "type", "separator"])]
	public async ValueTask<CallState> ZoneContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await LocateService.Locate(parser, executor, executor, ZoneArg(parser, "0"), LocateFlags.All) is not AnySharpObject zone)
		{
			return ErrorMessages.Returns.InvalidZone;
		}

		var type = ZoneArg(parser, "1");
		var contents = new List<string>();
		foreach (var member in await ZoneMemberRefsAsync(zone))
		{
			var arguments = new Dictionary<string, CallState> { ["0"] = new($"#{member.Number}") };
			if (type.Length > 0) arguments["1"] = new(type);
			var listed = await parser.With(state => state with { Arguments = arguments }, WalkContentsWithFilter);
			var text = listed.Message?.ToPlainText() ?? string.Empty;
			if (text.Length == 0 || text.StartsWith("#-1")) continue;
			contents.Add(text);
		}

		var separator = parser.CurrentState.Arguments.ContainsKey("2") ? ZoneArg(parser, "2") : " ";
		return string.Join(separator, contents
			.SelectMany(list => list.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			.Select(entry => DBRef.TryParse(entry, out var dbref) && dbref is { } parsed ? $"#{parsed.Number}" : entry));
	}

	/// <summary>
	/// <c>zsearch(&lt;zone&gt; [&lt;class&gt;=&lt;restriction&gt;][, &lt;low&gt;[, &lt;high&gt;]])</c>: search() over the objects
	/// in the zone, which takes the place of search()'s player.
	/// </summary>
	[SharpFunction(Name = "zsearch", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular,
		ParameterNames = ["zone class=restriction", "low", "high"])]
	public ValueTask<CallState> ZoneSearch(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> ZoneSearchAsync(parser, attribute, objids: false);

	[SharpFunction(Name = "zsearchobjid", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular,
		ParameterNames = ["zone class=restriction", "low", "high"])]
	public ValueTask<CallState> ZoneSearchObjid(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> ZoneSearchAsync(parser, attribute, objids: true);

	/// <summary><c>searchobjid()</c>: search() giving objids.</summary>
	[SharpFunction(Name = "searchobjid", MinArgs = 0, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular,
		ParameterNames = ["player", "class=restriction..."])]
	public async ValueTask<CallState> SearchObjid(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> await RewriteSearchAsync(await SearchAnyAsync(parser, attribute), objids: true, going: true);

	/// <summary><c>searchng()</c>: search() leaving out objects being destroyed.</summary>
	[SharpFunction(Name = "searchng", MinArgs = 0, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular,
		ParameterNames = ["player", "class=restriction..."])]
	public async ValueTask<CallState> SearchNotGoing(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> await RewriteSearchAsync(await SearchAnyAsync(parser, attribute), objids: false, going: false);

	/// <summary><c>searchngobjid()</c>: searchng() giving objids.</summary>
	[SharpFunction(Name = "searchngobjid", MinArgs = 0, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular,
		ParameterNames = ["player", "class=restriction..."])]
	public async ValueTask<CallState> SearchNotGoingObjid(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> await RewriteSearchAsync(await SearchAnyAsync(parser, attribute), objids: true, going: false);

	/// <summary>search() with no argument searching as an empty one does: everything the caller may search.</summary>
	private async ValueTask<CallState> SearchAnyAsync(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> parser.CurrentState.Arguments.Count == 0
			? await parser.With(state => state with { Arguments = new Dictionary<string, CallState> { ["0"] = new(string.Empty) } },
				p => ListSearch(p, attribute))
			: await ListSearch(parser, attribute);

	private async ValueTask<CallState> ZoneSearchAsync(IMUSHCodeParser parser, SharpFunctionAttribute attribute, bool objids)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var first = ZoneArg(parser, "0").Trim();
		var equals = first.IndexOf('=');
		var before = equals < 0 ? first : first[..equals].TrimEnd();
		var space = equals < 0 ? -1 : before.LastIndexOf(' ');
		var zoneName = equals < 0 ? first : space < 0 ? string.Empty : before[..space].Trim();
		var spec = equals < 0 ? string.Empty : first[(space + 1)..].Trim();
		if (zoneName.Length == 0
			|| await LocateService.Locate(parser, executor, executor, zoneName, LocateFlags.All) is not AnySharpObject zone)
		{
			return ErrorMessages.Returns.InvalidZone;
		}

		var members = (await ZoneMemberRefsAsync(zone)).Select(member => member.Number).ToHashSet();
		// With a class, search()'s one-argument form, which reads the range from arguments 1 and 2; without one, every
		// object the caller may search, the range as lsearch()'s MINDB and MAXDB.
		var arguments = spec.Length > 0
			? new Dictionary<string, CallState>(parser.CurrentState.Arguments) { ["0"] = new(spec) }
			: RangeOnly(parser);
		var found = await parser.With(state => state with { Arguments = arguments },
			p => spec.Length > 0 ? TinySearchAsync(p, executor) : ListSearchInternal(p, attribute, useRegex: false));

		var text = found.Message?.ToPlainText() ?? string.Empty;
		if (text.StartsWith("#-1")) return found;

		var kept = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Where(entry => DBRef.TryParse(entry, out var dbref) && dbref is { } parsed && members.Contains(parsed.Number));
		return await RewriteSearchAsync(new CallState(string.Join(' ', kept)), objids, going: true);
	}

	/// <summary>lsearch() arguments for everything the caller may search, within zsearch()'s low and high dbrefs.</summary>
	private static Dictionary<string, CallState> RangeOnly(IMUSHCodeParser parser)
	{
		var arguments = new Dictionary<string, CallState> { ["0"] = new(string.Empty) };
		foreach (var (key, name) in new[] { ("1", "MINDB"), ("2", "MAXDB") })
		{
			if (ZoneArg(parser, key).Trim() is { Length: > 0 } bound)
			{
				arguments[arguments.Count.ToString()] = new(name);
				arguments[arguments.Count.ToString()] = new(bound);
			}
		}

		return arguments;
	}

	/// <summary>
	/// A search's result with each dbref written as an objid when <paramref name="objids"/>, and objects being destroyed
	/// left out unless <paramref name="going"/>. An error passes through unchanged.
	/// </summary>
	private async ValueTask<CallState> RewriteSearchAsync(CallState result, bool objids, bool going)
	{
		var text = result.Message?.ToPlainText() ?? string.Empty;
		if ((!objids && going) || text.StartsWith("#-1")) return result;

		var written = new List<string>();
		foreach (var entry in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (!DBRef.TryParse(entry, out var dbref) || dbref is not { } parsed
				|| await Mediator.Send(new GetObjectNodeQuery(parsed)) is not AnySharpObject found)
			{
				written.Add(entry);
				continue;
			}

			if (!going && await found.HasFlag("GOING")) continue;
			written.Add(objids ? found.Object().DBRef.ToString() : entry);
		}

		return new CallState(string.Join(' ', written)) { HadErrors = result.HadErrors };
	}

	/// <summary>
	/// <c>zonecmd(&lt;action&gt;, &lt;object&gt;[, &lt;zone&gt;])</c>: <c>@chzone</c> as a function. <c>add</c> and
	/// <c>delete</c> put the object in a zone or take it out of one, <c>replace</c> takes <c>&lt;old&gt;/&lt;new&gt;</c> and
	/// puts the new zone last, <c>purge</c> takes it out of every zone, and <c>list</c> gives the zones it is in.
	/// </summary>
	[SharpFunction(Name = "zonecmd", MinArgs = 2, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged | FunctionFlags.StripAnsi,
		ParameterNames = ["action", "object", "zone"])]
	public async ValueTask<CallState> ZoneCommand(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var action = ZoneArg(parser, "0").Trim().ToLowerInvariant();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await LocateService.Locate(parser, executor, executor, ZoneArg(parser, "1"), LocateFlags.All) is not AnySharpObject target)
		{
			return ErrorMessages.Returns.NoMatch;
		}

		// The zones the object is in, whether or not it is a zone with objects of its own.
		if (action.StartsWith('l'))
		{
			return await PermissionService.CanExamine(executor, target)
				? string.Join(' ', PageOf([.. (await Mediator.ZonesOfAsync(target)).Select(zone => zone.Object().DBRef)],
					ZoneArg(parser, "2").Trim()).Select(zone => $"#{zone.Number}"))
				: ErrorMessages.Returns.Nothing;
		}

		var zoneName = ZoneArg(parser, "2").Trim();
		return action switch
		{
			_ when action.StartsWith('p') && (await Mediator.ZonesOfAsync(target)).Count == 0 => CallState.Empty,
			_ when action.StartsWith('p') => Done(await ChangeZoneForFunctionAsync(parser, executor, target,
				new AnyOptionalSharpObject(new None()))),
			_ when zoneName.Length == 0 => ErrorMessages.Returns.InvalidZone,
			_ when action.StartsWith('a') => await WithZoneAsync(parser, executor, zoneName,
				async zone => Done(await AddZoneForFunctionAsync(parser, executor, target, zone))),
			_ when action.StartsWith('d') => await WithZoneAsync(parser, executor, zoneName,
				async zone => Done(await ZoneHelpers.RemoveZoneAsync(Mediator, NotifyService, PermissionService, executor,
					target, zone, noisy: false))),
			_ when action.StartsWith('r') && zoneName.IndexOf('/') is > 0 and var slash => await WithZoneAsync(parser,
				executor, zoneName[..slash], async old => await WithZoneAsync(parser, executor, zoneName[(slash + 1)..],
					async replacement => await ReplaceZoneAsync(parser, executor, target, old, replacement))),
			_ => ErrorMessages.Returns.InvalidArguments
		};

		static CallState Done(Result<Success> result) => result switch
		{
			Success => CallState.Empty,
			Error<string> error => error.Value
		};
	}

	private async ValueTask<CallState> ReplaceZoneAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject target, AnySharpObject old, AnySharpObject replacement)
	{
		if (!(await Mediator.ZonesOfAsync(target)).Any(held => held.Object().DBRef.Equals(old.Object().DBRef)))
		{
			return ErrorMessages.Returns.NothingToDo;
		}

		return await AddZoneForFunctionAsync(parser, executor, target, replacement) switch
		{
			Error<string> error => error.Value,
			_ => await ZoneHelpers.RemoveZoneAsync(Mediator, NotifyService, PermissionService, executor, target, old,
				noisy: false) switch
			{
				Error<string> error => error.Value,
				_ => CallState.Empty
			}
		};
	}

	private async ValueTask<CallState> WithZoneAsync(IMUSHCodeParser parser, AnySharpObject executor, string name,
		Func<AnySharpObject, ValueTask<CallState>> then)
		=> await LocateService.Locate(parser, executor, executor, name.Trim(), LocateFlags.All) is AnySharpObject zone
			? await then(zone)
			: ErrorMessages.Returns.InvalidZone;

	private ValueTask<Result<Success>> AddZoneForFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject target, AnySharpObject zone)
		=> ZoneHelpers.AddZoneAsync(parser, Mediator, RelationshipCycles, NotifyService, PermissionService, LockService,
			DidItService, ObjectRelationshipService, FlagAndPowerService, Configuration, ConnectionService, executor, target,
			zone, preserve: false, noisy: false);

	private ValueTask<Result<Success>> ChangeZoneForFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject target, AnyOptionalSharpObject zone)
		=> ZoneHelpers.ChangeZoneAsync(parser, Mediator, RelationshipCycles, NotifyService, PermissionService, LockService,
			DidItService, ObjectRelationshipService, FlagAndPowerService, Configuration, ConnectionService, executor, target,
			zone, preserve: false, noisy: false);

	/// <summary><c>zfun2()</c>: zfun() run as the caller, as u2() is.</summary>
	[SharpFunction(Name = "zfun2", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular,
		ParameterNames = ["object/attribute", "arguments..."])]
	public ValueTask<CallState> ZoneFunctionAsCaller(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnZoneAsync(parser, evaluated: true, p => UserAttributeAsCaller(p, attribute));

	[SharpFunction(Name = "zfunlocal", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular | FunctionFlags.Localize,
		ParameterNames = ["object/attribute", "arguments..."])]
	public ValueTask<CallState> ZoneFunctionLocalized(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnZoneAsync(parser, evaluated: true, p => UserAttributeLocalized(p, attribute));

	[SharpFunction(Name = "zfun2local", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular | FunctionFlags.Localize,
		ParameterNames = ["object/attribute", "arguments..."])]
	public ValueTask<CallState> ZoneFunctionAsCallerLocalized(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnZoneAsync(parser, evaluated: true, p => UserAttributeAsCallerLocalized(p, attribute));

	[SharpFunction(Name = "zfundefault", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse,
		ParameterNames = ["object/attribute", "default", "arguments..."])]
	public ValueTask<CallState> ZoneFunctionDefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> OnZoneAsync(parser, evaluated: false, p => UserAttributeDefaultAsync(p, asCaller: false));

	[SharpFunction(Name = "zfun2default", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse,
		ParameterNames = ["object/attribute", "default", "arguments..."])]
	public ValueTask<CallState> ZoneFunctionAsCallerDefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> OnZoneAsync(parser, evaluated: false, p => UserAttributeDefaultAsync(p, asCaller: true));

	[SharpFunction(Name = "zfunldefault", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse | FunctionFlags.Localize,
		ParameterNames = ["object/attribute", "default", "arguments..."])]
	public ValueTask<CallState> ZoneFunctionLocalizedDefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> OnZoneAsync(parser, evaluated: false, p => UserAttributeDefaultAsync(p, asCaller: false));

	[SharpFunction(Name = "zfun2ldefault", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse | FunctionFlags.Localize,
		ParameterNames = ["object/attribute", "default", "arguments..."])]
	public ValueTask<CallState> ZoneFunctionAsCallerLocalizedDefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> OnZoneAsync(parser, evaluated: false, p => UserAttributeDefaultAsync(p, asCaller: true));

	/// <summary><c>zfuneval()</c>: ueval() of a zone's attribute, <c>[&lt;zone&gt;/]&lt;attribute&gt;/&lt;level&gt;</c>.</summary>
	[SharpFunction(Name = "zfuneval", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular,
		ParameterNames = ["object/attribute/role", "arguments..."])]
	public ValueTask<CallState> ZoneFunctionEval(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnZoneAsync(parser, evaluated: true, p => UEval(p, attribute), withLevel: true);

	/// <summary>
	/// Runs <paramref name="call"/> with its first argument naming the zone's attribute: <c>&lt;zone&gt;/&lt;attribute&gt;</c>,
	/// when the zone is one the caller is in, or <c>&lt;attribute&gt;</c> on the caller's first zone. A first argument
	/// that is still unevaluated (<paramref name="evaluated"/> false) is evaluated first, once.
	/// </summary>
	private async ValueTask<CallState> OnZoneAsync(IMUSHCodeParser parser, bool evaluated,
		Func<IMUSHCodeParser, ValueTask<CallState>> call, bool withLevel = false)
	{
		var first = parser.CurrentState.Arguments["0"].Message;
		var selector = (evaluated ? first : (await ParseRawArgument(parser, first))?.Message)?.ToPlainText() ?? string.Empty;
		var level = string.Empty;
		if (withLevel)
		{
			var cut = selector.LastIndexOf('/');
			if (cut < 0) return ErrorMessages.Returns.NoLevelSpecified;
			level = selector[cut..];
			selector = selector[..cut];
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var zones = await Mediator.ZonesOfAsync(executor);
		var slash = selector.IndexOf('/');
		AnySharpObject? zone;
		string attributeName;
		if (slash < 0)
		{
			zone = zones.Count > 0 ? zones[0] : null;
			attributeName = selector;
		}
		else
		{
			zone = await LocateService.Locate(parser, executor, executor, selector[..slash], LocateFlags.All) is AnySharpObject named
				&& zones.Any(held => held.Object().DBRef.Equals(named.Object().DBRef))
					? named
					: null;
			attributeName = selector[(slash + 1)..];
		}

		if (zone is null)
		{
			return ErrorMessages.Returns.InvalidZone;
		}

		var arguments = new Dictionary<string, CallState>(parser.CurrentState.Arguments)
		{
			["0"] = new($"#{zone.Object().DBRef.Number}/{attributeName}{level}")
		};
		return await parser.With(state => state with { Arguments = arguments }, call);
	}

	/// <summary>
	/// The objects in the zone named by the first argument, of <paramref name="type"/> or every type, as TinyMUX lists
	/// them: the caller must control the zone or be a wizard or royalty.
	/// </summary>
	private async ValueTask<CallState> ZoneMembersOfTypeAsync(IMUSHCodeParser parser, string? type)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var found = await LocateService.Locate(parser, executor, executor, ZoneArg(parser, "0"), LocateFlags.All);
		if (found is not AnySharpObject zone)
		{
			return ErrorMessages.Returns.NoMatch;
		}

		if (!await executor.IsPriv() && !await PermissionService.Controls(executor, zone))
		{
			return ErrorMessages.Returns.PermissionDenied;
		}

		var members = await Mediator.CreateStream(new GetObjectsByZoneQuery(zone.Object().DBRef))
			.Where(member => type is null || member.Type == type)
			.Select(member => $"#{member.DBRef.Number}")
			.ToListAsync();
		return string.Join(' ', members);
	}

	private async ValueTask<IReadOnlyList<DBRef>> ZoneMemberRefsAsync(AnySharpObject zone)
		=> await Mediator.CreateStream(new GetObjectsByZoneQuery(zone.Object().DBRef))
			.Select(member => member.DBRef)
			.ToListAsync();

	private static string ZoneArg(IMUSHCodeParser parser, string key)
		=> parser.CurrentState.Arguments.TryGetValue(key, out var value) ? value.Message?.ToPlainText() ?? string.Empty : string.Empty;

	private static bool ZoneArgIsTrue(IMUSHCodeParser parser, string key)
		=> int.TryParse(ZoneArg(parser, key).Trim(), out var value) && value != 0;
}
