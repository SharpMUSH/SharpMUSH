using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@FIND", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 0, MaxArgs = 3, ParameterNames = ["name", "flags"])]
	public async ValueTask<Option<CallState>> Find(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		string? searchName = null;
		if (args.Count > 0 && args.ContainsKey("0"))
		{
			searchName = args["0"].Message?.ToPlainText();
		}

		int? beginDbref = null;
		int? endDbref = null;

		if (args.Count >= 2 && args.ContainsKey("1"))
		{
			var beginStr = args["1"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(beginStr) && int.TryParse(beginStr, out var begin))
			{
				beginDbref = begin;
			}
		}

		if (args.Count >= 3 && args.ContainsKey("2"))
		{
			var endStr = args["2"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(endStr) && int.TryParse(endStr, out var end))
			{
				endDbref = end;
			}
		}

		var matchCount = 0;

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FindSearchingFormat), executor, searchName != null ? string.Format(ErrorMessages.Notifications.FindSearchMatchingFormat, searchName) : "");

		var filter = new ObjectSearchFilter
		{
			NamePattern = searchName,
			MinDbRef = beginDbref,
			MaxDbRef = endDbref
		};

		var controlledResults = await Mediator.CreateStream(new GetFilteredObjectsQuery(filter))
			.Where(async (obj, ct) =>
			{
				return await Mediator.Send(new GetObjectNodeQuery(obj.DBRef), ct) is AnySharpObject objNode
					&& await PermissionService.Controls(executor, objNode);
			})
			.ToListAsync();

		matchCount = controlledResults.Count;

		if (beginDbref.HasValue || endDbref.HasValue)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FindRangeFormat), executor, beginDbref ?? 0, endDbref?.ToString() ?? "end");
		}

		foreach (var obj in controlledResults)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FindObjectResultFormat), executor, obj.Key, obj.Name);
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FindFoundMatchingFormat), executor, matchCount);

		return new CallState(matchCount.ToString());
	}

	[SharpCommand(Name = "@SCAN", Switches = ["ROOM", "SELF", "ZONE", "GLOBALS"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 1, MaxArgs = 0, ParameterNames = ["object", "code"])]
	public async ValueTask<Option<CallState>> Scan(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.Any()
			? parser.CurrentState.Switches.ToArray()
			: ["ROOM", "SELF", "ZONE", "GLOBALS"];

		var perceive = await ObserveRealityAsync(parser, executor);
		List<string> runningOutput = [];

		async Task<bool> CanScan(AnySharpObject obj)
		{
			var controls = await PermissionService.Controls(executor, obj);
			if (controls) return true;

			var isVisual = await obj.HasFlag("VISUAL");
			return isVisual;
		}

		async ValueTask ReportMatches(IAsyncEnumerable<AnySharpObject> candidates)
		{
			var matched = await CommandDiscoveryService.MatchUserDefinedCommand(parser,
				candidates.Where((item, ct) => perceive(item.Object().DBRef, ct)), arg0);
			if (!matched.TryGetValue(out var matches))
			{
				return;
			}

			foreach (var (i, (obj, attr, _)) in matches.Index())
			{
				if (!await CanScan(obj))
				{
					continue;
				}

				runningOutput.Add($"#{obj.Object().DBRef.Number}/{attr.LongName}");
				await NotifyService.Notify(executor,
					$"{obj.Object().Name}\t[{i}: #{obj.Object().DBRef.Number}/{attr.LongName}]", executor);
			}
		}

		static IAsyncEnumerable<AnySharpObject> Just(AnySharpObject obj) => new[] { obj }.ToAsyncEnumerable();

		var here = executor.IsContent ? await executor.AsContent.Location() : null;

		// Both zones are wanted by the ZONE branch and by the master room's already-scanned guard, and
		// resolving one costs a fetch, so only pay for them when a branch that reads them will run.
		var needsZones = switches.Contains("ZONE") || switches.Contains("GLOBALS");
		var hereZone = !needsZones || here is null
			? null
			: await here.Object().Zone.WithCancellation(CancellationToken.None) is AnySharpObject locationZone
				? locationZone
				: null;
		var personalZone = !needsZones
			? null
			: await executor.Object().Zone.WithCancellation(CancellationToken.None) is AnySharpObject ownZone
				? ownZone
				: null;

		var scannedNeighbors = false;

		if (here is not null && switches.Contains("ROOM"))
		{
			// Penn splits this into two flags and @scan with no switches sets both: CHECK_NEIGHBORS for
			// the contents of the location, CHECK_HERE for the location object itself
			// (src/game.c:1892-1911). Only the first was implemented, so a $-command living on the room
			// - the ordinary place to put one - was never reported.
			await ReportMatches(here.Content(Mediator).Select(x => x.WithRoomOption()));
			await ReportMatches(Just(here.WithExitOption()));
			scannedNeighbors = true;
		}

		if (switches.Contains("SELF"))
		{
			// CHECK_INVENTORY, then CHECK_SELF (src/game.c:1911-1927). The self check is not gated on
			// being a container: Penn scans the executor whether or not it can hold anything, and this
			// whole branch used to be skipped for an executor that could not.
			if (executor.IsContainer)
			{
				await ReportMatches(executor.AsContainer.Content(Mediator).Select(x => x.WithRoomOption()));
			}

			// An executor standing in the room is already in its contents, so the neighbours pass above
			// has reported it. do_scan lets that duplicate through because it prints the two passes under
			// separate headings; this returns one flat list, so follow scan_list instead, which drops
			// CHECK_SELF the moment CHECK_NEIGHBORS is set for exactly this reason (src/game.c:1763-1764).
			if (!scannedNeighbors)
			{
				await ReportMatches(Just(executor));
			}
		}

		if (switches.Contains("ZONE"))
		{
			// A zone that is a room is a Zone Master Room and its CONTENTS carry the commands; a zone
			// that is anything else carries them itself (src/game.c:1931-1978). Scanning the contents in
			// both cases - which is what this did - looks in the wrong place for every non-room zone,
			// and the executor's own zone was not consulted at all.
			if (hereZone is not null)
			{
				await ScanZone(hereZone);
			}

			if (personalZone is not null
					&& (hereZone is null || personalZone.Object().DBRef != hereZone.Object().DBRef))
			{
				await ScanZone(personalZone);
			}
		}

		if (switches.Contains("GLOBALS"))
		{
			var masterRoom = new DBRef(Convert.ToInt32(Configuration.CurrentValue.Database.MasterRoom));

			// Penn's own guard, verbatim: skip when the executor stands in the master room, or the master
			// room is either zone (src/game.c:1984). Note it tests only those three dbrefs - it does NOT
			// ask whether the ROOM or ZONE branch actually ran, so `@scan/globals` from inside the master
			// room reports nothing in PennMUSH too. Kept as-is for parity rather than "improved".
			var alreadyScanned = here?.Object().DBRef == masterRoom
				|| hereZone?.Object().DBRef == masterRoom
				|| personalZone?.Object().DBRef == masterRoom;

			if (!alreadyScanned)
			{
				await ReportMatches(Mediator.CreateStream(new GetContentsQuery(masterRoom))
					?.Select(x => x.WithRoomOption()) ?? AsyncEnumerable.Empty<AnySharpObject>());
			}
		}

		return new CallState(string.Join(" ", runningOutput));

		async ValueTask ScanZone(AnySharpObject zone)
		{
			if (zone.IsRoom)
			{
				// Penn guards both zone blocks with the same expression - Location(player) != Zone(player)
				// (src/game.c:1937, 1971) - which compares the location to the PERSONAL zone even while
				// scanning the location's zone. Reads like a slip, but it is what Penn does, and it is
				// materially different from comparing against the zone being scanned: with no personal
				// zone set, Zone(player) is NOTHING and the location's Zone Master Room is always scanned.
				if (here?.Object().DBRef != personalZone?.Object().DBRef)
				{
					await ReportMatches(Mediator.CreateStream(new GetContentsQuery(zone.Object().DBRef))
						?.Select(x => x.WithRoomOption()) ?? AsyncEnumerable.Empty<AnySharpObject>());
				}

				return;
			}

			await ReportMatches(Just(zone));
		}
	}

	[SharpCommand(Name = "@SEARCH", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse,
		MinArgs = 0, MaxArgs = int.MaxValue, ParameterNames = ["player", "class=restriction..."])]
	public async ValueTask<Option<CallState>> Search(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		var (playerText, pairs) = ParseSearchCommandArgs(args);

		DBRef? ownerFilter;
		if (playerText == null)
		{
			// PennMUSH: no <player> given defaults to ANY_OWNER for wizards (See_All/Search_All), else the executor's own objects.
			ownerFilter = await executor.IsWizard() ? null : executor.Object().DBRef;
		}
		else if (playerText.Equals("all", StringComparison.OrdinalIgnoreCase))
		{
			ownerFilter = null;
		}
		else if (playerText.Equals("me", StringComparison.OrdinalIgnoreCase))
		{
			ownerFilter = executor.Object().DBRef;
		}
		else
		{
			var maybeOwner = await LocateService.Locate(parser, executor, executor, playerText, LocateFlags.All);
			if (maybeOwner is not AnySharpObject owner)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchUnknownOwner), executor);
				return new CallState(ErrorMessages.Returns.NotFound);
			}

			ownerFilter = owner.Object().DBRef;
		}

		var search = await SearchSpecEngine.ExecuteResultAsync(
			parser, Mediator, LocateService, AttributeService, BooleanExpressionParser, PermissionService,
			executor, ownerFilter, pairs, useRegex: false);
		var matches = search.Matches;

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchAdvancedHeader), executor);

		if (playerText != null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchPlayerFilterFormat), executor, playerText);
		}

		if (pairs.Count > 0)
		{
			var criteria = string.Join(", ", pairs.Select(p => $"{p.ClassType.ToUpperInvariant()}={p.Restriction}"));
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchCriteriaFormat), executor, criteria);
		}

		if (matches.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchNothingFound), executor);
			return new CallState("0") { HadErrors = search.HadErrors };
		}

		foreach (var obj in matches)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchObjectEntryFormat), executor, obj.Key, obj.Name, obj.Type);
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchObjectsFoundFormat), executor, matches.Count);

		return new CallState(matches.Count.ToString()) { HadErrors = search.HadErrors };
	}

	/// <summary>
	/// Parses @search's own syntax, per PennMUSH's <c>do_search</c>:
	/// <c>@search [&lt;player&gt;] [&lt;class1&gt;=&lt;restriction1&gt;[,&lt;class2&gt;=&lt;restriction2&gt;...]]</c>.
	/// <para>The command's <c>CB.EqSplit | CB.RSArgs</c> behavior only splits the RAW text on the FIRST
	/// top-level '=' (giving <c>args["0"]</c> the whole left side verbatim) and then comma-splits
	/// everything right of it (<c>args["1"]</c>, <c>args["2"]</c>, ...). Unlike lsearch(), whose
	/// positional function args are already one token per class/restriction, @search's own player name
	/// and its first search class are both crammed into that same left-hand chunk (e.g. "all type" for
	/// <c>@search all type=PLAYER</c>), and every restriction after the first carries its own class via
	/// an embedded '=' inside its comma chunk (e.g. "flags=W" in "...,flags=W"). This re-splits that
	/// left chunk on its first whitespace run, then walks the remaining chunks for their own '='.</para>
	/// </summary>
	private static (string? Player, List<SearchSpecEngine.SearchPair> Pairs) ParseSearchCommandArgs(
		IReadOnlyDictionary<string, CallState> args)
	{
		var lhs = args.TryGetValue("0", out var arg0) ? arg0.Message?.ToPlainText() ?? "" : "";

		var rhsChunks = new List<string>();
		for (var i = 1; args.TryGetValue(i.ToString(), out var chunk); i++)
		{
			rhsChunks.Add(chunk.Message?.ToPlainText() ?? "");
		}

		string? player;
		string? leadingClass = null;

		if (lhs.Length == 0)
		{
			player = null;
		}
		else if (lhs[0] == '"')
		{
			var closeIndex = lhs.IndexOf('"', 1);
			if (closeIndex >= 0)
			{
				player = lhs[1..closeIndex];
				var remainder = lhs[(closeIndex + 1)..].TrimStart();
				leadingClass = remainder.Length > 0 ? remainder : null;
			}
			else
			{
				player = lhs.TrimStart('"');
			}
		}
		else
		{
			var spaceIndex = lhs.IndexOf(' ');
			if (spaceIndex < 0)
			{
				// A single bare token: it's the leading class if there's a restriction waiting for it
				// on the right of the '=' (e.g. "type=room"); otherwise it's a plain player/owner filter
				// (e.g. "@search SomePlayer").
				if (rhsChunks.Count > 0)
				{
					leadingClass = lhs;
					player = null;
				}
				else
				{
					player = lhs;
				}
			}
			else
			{
				player = lhs[..spaceIndex];
				var remainder = lhs[(spaceIndex + 1)..].TrimStart();
				leadingClass = remainder.Length > 0 ? remainder : null;
			}
		}

		var pairs = new List<SearchSpecEngine.SearchPair>();
		var chunkIndex = 0;

		if (leadingClass != null && chunkIndex < rhsChunks.Count)
		{
			pairs.Add(new SearchSpecEngine.SearchPair(leadingClass, rhsChunks[chunkIndex]));
			chunkIndex++;
		}

		for (; chunkIndex < rhsChunks.Count; chunkIndex++)
		{
			var chunk = rhsChunks[chunkIndex];
			var eqIndex = chunk.IndexOf('=');
			if (eqIndex > 0)
			{
				pairs.Add(new SearchSpecEngine.SearchPair(chunk[..eqIndex], chunk[(eqIndex + 1)..]));
			}
		}

		return (player, pairs);
	}

	[SharpCommand(Name = "@WHEREIS", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 1, ParameterNames = ["name"])]
	public async ValueTask<Option<CallState>> WhereIs(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WhereIsMustSpecifyPlayer), executor);
			return new CallState(ErrorMessages.Returns.NoPlayerSpecified);
		}

		var targetName = args["0"].Message!.ToPlainText();

		var maybeTarget = await LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			targetName,
			LocateFlags.All);

		if (maybeTarget is not AnySharpObject target)
		{
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		if (target is not SharpPlayer targetPlayer)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WhereIsCanOnlyLocatePlayers), executor);
			return new CallState(ErrorMessages.Returns.NotAPlayer);
		}

		var targetObject = target.Object();

		var isUnfindable = await targetObject.Flags.Value
			.AnyAsync(f => f.Symbol == "U" || f.Name.Equals("UNFINDABLE", StringComparison.OrdinalIgnoreCase));

		if (isUnfindable)
		{
			await NotifyService.Notify(target,
				$"{executor.Object().Name} tried to locate you, but was unable to.", executor);
			await NotifyService.Notify(executor,
				$"{targetObject.Name} is UNFINDABLE.", executor);
			return new CallState(ErrorMessages.Returns.Unfindable);
		}

		var targetLocation = await target.AsContent.Location();
		var locationName = targetLocation.Object().Name;

		await NotifyService.Notify(target,
			$"{executor.Object().Name} has just located your position.", executor);

		await NotifyService.Notify(executor,
			$"{targetObject.Name} is in {locationName}.", executor);

		return new CallState(targetLocation.Object().DBRef.ToString());
	}

	[SharpCommand(Name = "@DECOMPILE", Switches = ["DB", "NAME", "PREFIX", "TF", "FLAGS", "ATTRIBS", "SKIPDEFAULTS"],
		Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object", "name"])]
	public async ValueTask<Option<CallState>> Decompile(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.YouMustSpecifyObjectToDecompile), executor);
			return new CallState(ErrorMessages.Returns.NoObjectSpecified);
		}

		var objectSpec = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(objectSpec))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.YouMustSpecifyObjectToDecompile), executor);
			return new CallState(ErrorMessages.Returns.NoObjectSpecified);
		}

		var prefix = args.Count >= 2 ? args["1"].Message?.ToPlainText() ?? "" : "";

		if (switches.Contains("TF"))
		{
			var tfPrefixAttr = await AttributeService.GetAttributeAsync(executor, executor, "TFPREFIX",
				IAttributeService.AttributeMode.Read, false);

			prefix = tfPrefixAttr is SharpAttribute[] attr
				? attr.Last().Value.ToPlainText()
				: "FugueEdit > ";
		}

		string? attributePattern = null;
		AnyOptionalSharpObject target;

		if (HelperFunctions.SplitDbRefAndOptionalAttr(objectSpec) is { Object: var objectName, Attribute: var maybeAttributePattern })
		{
			attributePattern = maybeAttributePattern;

			var locate = await LocateService.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				objectName,
				LocateFlags.All);

			if (locate is not AnySharpObject located)
			{
				return new None();
			}

			target = located;
		}
		else
		{
			var locate = await LocateService.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				objectSpec,
				LocateFlags.All);

			if (locate is not AnySharpObject located)
			{
				return new None();
			}

			target = located;
		}

		if (target is not AnySharpObject targetKnown)
		{
			return new None();
		}

		var canExamine = await PermissionService.CanExamine(executor, targetKnown);
		if (!canExamine)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var obj = targetKnown.Object();
		var useDbRef = switches.Contains("DB");
		var useName = switches.Contains("NAME") || !useDbRef; // NAME is default
		var showFlags = switches.Contains("FLAGS") || (!switches.Contains("ATTRIBS") && string.IsNullOrEmpty(attributePattern));
		var showAttribs = switches.Contains("ATTRIBS") || (!switches.Contains("FLAGS") && string.IsNullOrEmpty(attributePattern)) || !string.IsNullOrEmpty(attributePattern);
		var skipDefaults = switches.Contains("SKIPDEFAULTS");
		var isTf = switches.Contains("TF");

		if (!string.IsNullOrEmpty(attributePattern))
		{
			showFlags = false;
			showAttribs = true;
		}

		var objectRef = useDbRef ? $"#{obj.DBRef.Number}" : obj.Name;
		var outputs = new List<string>();

		if (showFlags)
		{
			var createCmd = obj.Type.ToUpperInvariant() switch
			{
				"ROOM" => $"@dig {objectRef}",
				"EXIT" => $"@open {objectRef}",
				"THING" => $"@create {objectRef}",
				"PLAYER" => $"@pcreate {objectRef}",
				_ => $"@create {objectRef}"
			};
			outputs.Add($"{prefix}{createCmd}");

			await foreach (var flag in obj.Flags.Value)
			{
				if (skipDefaults && IsDefaultFlag(obj.Type, flag.Name))
				{
					continue;
				}
				outputs.Add($"{prefix}@set {objectRef}={flag.Name}");
			}

			await foreach (var power in obj.Powers.Value)
			{
				outputs.Add($"{prefix}@power {objectRef}={power.Name}");
			}

			foreach (var (lockName, lockData) in obj.Locks.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
			{
				if (!BooleanExpressionParser.IsBound(lockData.LockString))
				{
					outputs.Add($"{prefix}@@ Invalid {lockName} lock omitted; replace it explicitly before decompiling.");
					continue;
				}
				var expression = await BooleanExpressionParser.RenderAsync(lockData.LockString, executor, LockRenderMode.Decompile, ExecutionBudget.CurrentToken);
				var standard = LockService.SystemLocks.TryGetValue(lockName, out var defaults);
				var switchName = standard ? LockNames.Display(lockName) : $"user:{lockName}";
				outputs.Add($"{prefix}@lock/{switchName} {objectRef}={expression}");
				foreach (var (flagName, (_, flag)) in LockService.LockPrivileges)
				{
					var set = lockData.Flags.HasFlag(flag);
					if (set && (!skipDefaults || !defaults.HasFlag(flag))) outputs.Add($"{prefix}@lset {objectRef}/{lockName}={flagName}");
					else if (!set && defaults.HasFlag(flag)) outputs.Add($"{prefix}@lset {objectRef}/{lockName}=!{flagName}");
				}
			}

			if (await obj.Parent.WithCancellation(CancellationToken.None) is AnySharpObject parent)
			{
				var parentObj = parent.Object();
				outputs.Add($"{prefix}@parent {objectRef}={parentObj.DBRef}");
			}
		}

		if (showAttribs)
		{
			SharpAttributesOrError atrs;
			if (!string.IsNullOrEmpty(attributePattern))
			{
				atrs = await AttributeService.GetAttributePatternAsync(
					executor,
					targetKnown,
					attributePattern,
					false, // don't check parents for decompile
					IAttributeService.AttributePatternMode.Wildcard);
			}
			else
			{
				atrs = await AttributeService.GetVisibleAttributesAsync(executor, targetKnown);
			}

			if (atrs is SharpAttribute[] decompiledAttributes)
			{
				foreach (var attr in decompiledAttributes)
				{
					const string VeiledFlagName = "VEILED";
					if (attr.Flags.Any(f => f.Name.Equals(VeiledFlagName, StringComparison.OrdinalIgnoreCase)))
					{
						continue;
					}

					var hasAnsi = ContainsAnsiMarkup(attr.Value);

					if (hasAnsi)
					{
						// Use @set format with decomposed value to ensure evaluation
						var decomposedValue = DecomposeAttributeValue(attr.Value);
						outputs.Add($"{prefix}@set {objectRef}={attr.Name}:{decomposedValue}");
					}
					else
					{
						var plainValue = attr.Value.ToPlainText();
						outputs.Add($"{prefix}&{attr.Name} {objectRef}={plainValue}");
					}

					if (!isTf && attr.Flags.Any())
					{
						if (!skipDefaults || !await AreDefaultAttrFlagsAsync(attr.Name, attr.Flags))
						{
							foreach (var flag in attr.Flags)
							{
								outputs.Add($"{prefix}@set {objectRef}/{attr.Name}={flag.Name}");
							}
						}
					}
				}
			}
		}

		foreach (var output in outputs)
		{
			await NotifyService.Notify(executor, output, executor);
		}

		return CallState.Empty;
	}

	/// <summary>
	/// Checks if an MString contains ANSI markup
	/// </summary>
	private bool ContainsAnsiMarkup(MString str)
	{
		var hasAnsi = false;
		MarkupWalker.EvaluateWith((markupType, innerText) =>
		{
			if (markupType is Ansi)
			{
				hasAnsi = true;
			}
			return innerText;
		}, str);
		return hasAnsi;
	}

	/// <summary>
	/// Decomposes an attribute value using the decompose() logic from StringFunctions
	/// </summary>
	private string DecomposeAttributeValue(MString input)
	{
		// Use same logic as decompose() function from StringFunctions
		var reconstructed = MarkupWalker.EvaluateWith((markupType, innerText) =>
		{
			return markupType switch
			{
				Ansi ansiMarkup
					=> Implementation.Functions.Functions.ReconstructAnsiCall(ansiMarkup.Style, innerText),
				_ => innerText
			};
		}, input);

		var result = reconstructed
			.Replace("\\", @"\\")
			.Replace("%", "\\%")
			.Replace(";", "\\;")
			.Replace("[", "\\[")
			.Replace("]", "\\]")
			.Replace("{", "\\{")
			.Replace("}", "\\}")
			.Replace("(", "\\(")
			.Replace(")", "\\)")
			.Replace(",", "\\,")
			.Replace("^", "\\^")
			.Replace("$", "\\$");

		result = MultipleWhitespaceRegex().Replace(result, m => string.Join("", Enumerable.Repeat("%b", m.Length)));

		result = result.Replace("\r", "%r").Replace("\n", "%r").Replace("\t", "%t");

		return result;
	}

	/// <summary>
	/// Checks if a flag is a default flag for the object type
	/// </summary>
	private bool IsDefaultFlag(string type, string flagName)
	{
		var defaultFlags = type.ToUpperInvariant() switch
		{
			"PLAYER" => Configuration?.CurrentValue.Flag.PlayerFlags ?? [],
			"ROOM" => Configuration?.CurrentValue.Flag.RoomFlags ?? [],
			"THING" => Configuration?.CurrentValue.Flag.ThingFlags ?? [],
			"EXIT" => Configuration?.CurrentValue.Flag.ExitFlags ?? [],
			_ => Array.Empty<string>()
		};

		return defaultFlags.Any(f => f.Equals(flagName, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Checks if attribute flags are the default for that attribute
	/// </summary>
	private async ValueTask<bool> AreDefaultAttrFlagsAsync(string attrName, IEnumerable<SharpAttributeFlag> flags)
	{
		var entry = await Mediator.Send(new GetAttributeEntryQuery(attrName.ToUpper()));

		if (entry == null)
		{
			// No entry means no custom defaults; empty flags are considered default
			return !flags.Any();
		}

		var currentFlagNames = flags.Select(f => f.Name.ToUpper()).OrderBy(n => n);
		var defaultFlagNames = entry.DefaultFlags.Select(f => f.ToUpper()).OrderBy(n => n);

		return currentFlagNames.SequenceEqual(defaultFlagNames);
	}

	[SharpCommand(Name = "@ENTRANCES", Switches = ["EXITS", "THINGS", "PLAYERS", "ROOMS"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 3, ParameterNames = ["object", "flags"])]
	public async ValueTask<Option<CallState>> Entrances(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		AnySharpObject targetObject;

		if (args.Count > 0 && args.ContainsKey("0"))
		{
			var targetName = args["0"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(targetName))
			{
				if (await LocateService.LocateAndNotifyIfInvalid(
						parser, executor, executor, targetName, LocateFlags.All) is not AnySharpObject located)
				{
					return new CallState(ErrorMessages.Returns.NotFound);
				}

				targetObject = located;
			}
			else
			{
				var location = await executor.AsContent.Location();
				targetObject = location.WithExitOption();
			}
		}
		else
		{
			var location = await executor.AsContent.Location();
			targetObject = location.WithExitOption();
		}

		int? beginDbref = null;
		int? endDbref = null;

		if (args.Count > 1 && args.ContainsKey("1"))
		{
			var beginStr = args["1"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(beginStr) && int.TryParse(beginStr, out var begin))
			{
				beginDbref = begin;
			}
		}

		if (args.Count > 2 && args.ContainsKey("2"))
		{
			var endStr = args["2"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(endStr) && int.TryParse(endStr, out var end))
			{
				endDbref = end;
			}
		}

		var targetObj = targetObject.Object();
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesToFormat), executor, targetObj.Name);

		var filterTypes = new List<string>();
		if (switches.Contains("EXITS")) filterTypes.Add("exits");
		if (switches.Contains("THINGS")) filterTypes.Add("things");
		if (switches.Contains("PLAYERS")) filterTypes.Add("players");
		if (switches.Contains("ROOMS")) filterTypes.Add("rooms");

		if (filterTypes.Count > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesFilteringForFormat), executor, string.Join(", ", filterTypes));
		}

		if (beginDbref.HasValue || endDbref.HasValue)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesRangeFormat), executor, beginDbref ?? 0, endDbref?.ToString() ?? "end");
		}

		var entrances = await Mediator.CreateStream(new GetEntrancesQuery(targetObj.DBRef)).ToListAsync();

		if (filterTypes.Count > 0 && !filterTypes.Contains("exits"))
		{
			entrances.Clear(); // GetEntrancesQuery only returns exits, so if exits not requested, clear
		}

		if (beginDbref.HasValue || endDbref.HasValue)
		{
			entrances = entrances.Where(e =>
			{
				var key = e.Object.Key;
				return (!beginDbref.HasValue || key >= beginDbref.Value) &&
							 (!endDbref.HasValue || key <= endDbref.Value);
			}).ToList();
		}

		if (entrances.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesZeroFound), executor);
		}
		else
		{
			foreach (var entrance in entrances)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesObjectEntryFormat), executor, entrance.Object.Key, entrance.Object.Name);
			}
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesCountFormat), executor, entrances.Count);
		}

		return new CallState(entrances.Count.ToString());
	}

	[SharpCommand(Name = "@GREP", Switches = ["LIST", "PRINT", "ILIST", "IPRINT", "REGEXP", "WILD", "NOCASE", "PARENT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 2, MaxArgs = 2, ParameterNames = ["object", "pattern"])]
	public async ValueTask<Option<CallState>> Grep(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);

		if (!args.TryGetValue("0", out var objAttrArg) || !args.TryGetValue("1", out var patternArg))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepInvalidArguments), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var objAttrText = objAttrArg.Message!.ToPlainText();
		var pattern = patternArg.Message!.ToPlainText();
		if (HelperFunctions.SplitDbRefAndOptionalAttr(objAttrText) is not { Object: var dbref, Attribute: var maybeAttributePattern })
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return new CallState(ErrorMessages.Returns.InvalidObject);
		}

		var attributePattern = string.IsNullOrEmpty(maybeAttributePattern) ? "*" : maybeAttributePattern;

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			dbref,
			LocateFlags.All) switch
		{
			AnySharpObject targetObject => await GrepAsync(parser, executor, targetObject, switches, pattern, attributePattern),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> GrepAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject targetObject, IEnumerable<string> switches, string pattern, string attributePattern)
	{
		var checkParents = switches.Contains("PARENT");

		// PennMUSH treats the obj/attr half of @grep as a single wildcard pattern
		// (predicat.c:1610-1617 defaults it to "*", then hands it to atr_iter_get), and "**" is
		// not a separate matching mode - it is the attribute-name wildcard that is allowed to
		// cross "`" (wild.c:89-107, real_atr_wild). That distinction lives in the wildcard-to-regex
		// translation in the database providers, so every pattern here is Wildcard.
		return await AttributeService.GetAttributePatternAsync(
			executor,
			targetObject,
			attributePattern,
			checkParents,
			IAttributeService.AttributePatternMode.Wildcard) switch
		{
			SharpAttribute[] attributes => await GrepAttributesAsync(parser, executor, attributes, switches, pattern),
			Error<string> error => await GrepUnreadableAsync(executor, error.Value)
		};
	}

	private async ValueTask<Option<CallState>> GrepUnreadableAsync(AnySharpObject executor, string error)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepErrorReadingAttributesFormat), executor, error);
		return new CallState($"#-1 {error}");
	}

	/// <summary>Reports the attributes whose value matches <paramref name="pattern"/>, the way the switches ask.</summary>
	private async ValueTask<Option<CallState>> GrepAttributesAsync(IMUSHCodeParser parser, AnySharpObject executor,
		SharpAttribute[] attributes, IEnumerable<string> switches, string pattern)
	{
		var isWild = switches.Contains("WILD");
		var isRegexp = switches.Contains("REGEXP");
		var isNoCase = switches.Contains("NOCASE") || switches.Contains("ILIST") || switches.Contains("IPRINT");
		var isPrint = switches.Contains("PRINT") || switches.Contains("IPRINT");

		var matchingAttributes = new List<SharpAttribute>();

		foreach (var attr in attributes)
		{
			var attrValue = attr.Value.ToPlainText();
			bool matches = false;

			if (isRegexp)
			{
				try
				{
					var regexOptions = isNoCase ? System.Text.RegularExpressions.RegexOptions.IgnoreCase : System.Text.RegularExpressions.RegexOptions.None;
					matches = System.Text.RegularExpressions.Regex.IsMatch(attrValue, pattern, regexOptions, TimeSpan.FromSeconds(1));
				}
				catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepRegexpTimedOutFormat), executor, pattern);
					return new CallState(ErrorMessages.Returns.RegexpTimeout);
				}
				catch
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepInvalidRegexpFormat), executor, pattern);
					return new CallState(ErrorMessages.Returns.InvalidRegexp);
				}
			}
			else if (isWild)
			{
				try
				{
					// grep_util passes cs = ((flags & GREP_NOCASE) == 0), so the wildcard grep is
					// case-SENSITIVE unless this is the "i" variant — unlike every other wildcard in
					// the game, which goes through quick_wild and its cs = 0.
					matches = SoftcodeRegex.Wildcard(pattern, caseSensitive: !isNoCase).IsMatch(attrValue);
				}
				catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepWildcardTimedOutFormat), executor, pattern);
					return new CallState(ErrorMessages.Returns.PatternTimeout);
				}
				catch
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepInvalidWildcardFormat), executor, pattern);
					return new CallState(ErrorMessages.Returns.InvalidPattern);
				}
			}
			else
			{
				var comparison = isNoCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
				matches = attrValue.Contains(pattern, comparison);
			}

			if (matches)
			{
				matchingAttributes.Add(attr);
			}
		}

		if (matchingAttributes.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepNoMatchingAttributesFound), executor);
			return new CallState(string.Empty);
		}

		if (isPrint)
		{
			// Lazily computed: only a flagged attribute needs it, and most @grep/PRINT calls have none.
			int? width = null;

			foreach (var attr in matchingAttributes)
			{
				var parseType = attr.SyntaxParseType();

				MString displayValue;

				if (parseType is null)
				{
					// Byte-identical to the pre-formatting behavior: nothing below this branch may
					// change when SyntaxParseType() is null, since that is the regression contract
					// covering all existing traffic.
					if (isRegexp || isWild)
					{
						displayValue = attr.Value;
					}
					else
					{
						// Highlight the matching parts using Span to avoid allocations
						var plainValue = attr.Value.ToPlainText();
						var comparison = isNoCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
						var index = plainValue.IndexOf(pattern, comparison);

						if (index >= 0)
						{
							var valueSpan = plainValue.AsSpan();
							var before = valueSpan.Slice(0, index).ToString();
							var match = valueSpan.Slice(index, pattern.Length).ToString();
							var after = valueSpan.Slice(index + pattern.Length).ToString();

							displayValue = MarkupText.Concat(MarkupText.Concat(MarkupText.Plain(before), MarkupText.Plain(match).Hilight()), MarkupText.Plain(after));
						}
						else
						{
							displayValue = attr.Value;
						}
					}
				}
				else
				{
					// The attribute carries a syntax flag: render the formatted, wrapped block instead
					// of the raw value. Empty values are left alone (mirrors @examine) rather than run
					// through the formatter, since an empty funsyntax/cmdsyntax body is itself a parse
					// error and would otherwise surface a stray parser-failure summary in place of blank.
					MString formatted;

					// Plain-text length of the code portion of `formatted` — everything but the error
					// summary the formatter appends beneath it.
					int codeLength;

					if (attr.Value.Length == 0)
					{
						formatted = attr.Value;
						codeLength = 0;
					}
					else
					{
						width ??= await ExecutorFormatWidthAsync(executor);

						var source = attr.Value;
						var tokens = parser.Tokenize(source);
						var semanticTokens = parser.GetSemanticTokens(source, parseType.Value);
						var errors = SoftcodeSource.Validate(parser, source, parseType.Value);
						formatted = SoftcodeFormatter.Format(source, tokens, semanticTokens, errors, width.Value, parser,
							parseType.Value, out codeLength);
					}

					if (isRegexp || isWild)
					{
						displayValue = formatted;
					}
					else
					{
						// Same highlight as the unflagged path, but sliced from the formatted block via
						// MarkupText.Substring (rather than rebuilt from plain-text spans) so the formatter's
						// own syntax colouring survives around the highlighted match.
						//
						// Bounded by codeLength: the attribute matched on its *value*, so the match is in the
						// code. Searching the whole block would let a pattern that occurs only in the appended
						// "#-1 PARSER FAILURE ..." summary highlight as though it were the match that put this
						// attribute in the result set.
						var plainFormatted = formatted.ToPlainText();
						var comparison = isNoCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
						var index = plainFormatted.IndexOf(pattern, 0, codeLength, comparison);

						if (index >= 0)
						{
							var before = formatted.Substring(0, index);
							var match = formatted.Substring(index, pattern.Length).Hilight();
							var after = formatted.Substring(index + pattern.Length, plainFormatted.Length - index - pattern.Length);

							displayValue = MarkupText.Concat(MarkupText.Concat(before, match), after);
						}
						else
						{
							displayValue = formatted;
						}
					}
				}

				await NotifyService.Notify(executor,
					MarkupText.Concat(MarkupText.Plain($"{attr.Name}: ").Hilight(), displayValue), executor);
			}
		}
		else
		{
			var attrNames = string.Join(" ", matchingAttributes.Select(a => a.Name));
			await NotifyService.Notify(executor, attrNames, executor);
		}

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@SWEEP", Switches = ["CONNECTED", "HERE", "INVENTORY", "EXITS"], Behavior = CB.Default,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["flags"])]
	public async ValueTask<Option<CallState>> Sweep(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var switches = parser.CurrentState.Switches.ToHashSet();
		var connectFlag = switches.Contains("CONNECTED");
		var hereFlag = switches.Contains("HERE");
		var inventoryFlag = switches.Contains("INVENTORY");
		var exitsFlag = switches.Contains("EXITS");

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var perceive = await ObserveRealityAsync(parser, executor);
		var location = await executor.Where();
		var locationObj = location.Object();
		var locationAnyObject = location.WithExitOption();
		var locationOwner = await locationObj.Owner.WithCancellation(CancellationToken.None);

		if (!inventoryFlag && !exitsFlag)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepListeningInRoom), executor);

			if (connectFlag)
			{
				if (await IsConnectedOrPuppetConnected(locationAnyObject))
				{
					if (location.IsPlayer)
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectIsListeningFormat), executor, locationObj.Name);
					}
					else
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectOwnerIsListeningFormat), executor, locationObj.Name, locationOwner.Object.Name);
					}
				}
			}
			else
			{
				if (await locationAnyObject.IsHearer(ConnectionService, AttributeService) ||
						await locationAnyObject.IsListener())
				{
					if (await ConnectionService.IsConnected(locationAnyObject))
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepRoomSpeechConnectedFormat), executor, locationObj.Name);
					else
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepRoomSpeechFormat), executor, locationObj.Name);
				}

				if (await locationAnyObject.HasActiveCommands(AttributeService))
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepRoomCommandsFormat), executor, locationObj.Name);
				if (await locationAnyObject.IsAudible())
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepRoomBroadcastingFormat), executor, locationObj.Name);
			}

			var contents = location.Content(Mediator)
				.Where((item, ct) => perceive(item.Object().DBRef, ct));
			await foreach (var obj in contents.WithCancellation(ExecutionBudget.CurrentToken))
			{
				var fullObj = obj.WithRoomOption();
				var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
				if (connectFlag)
				{
					if (await IsConnectedOrPuppetConnected(fullObj))
					{
						if (obj.IsPlayer)
						{
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectIsListeningFormat), executor, obj.Object().Name);
						}
						else
						{
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectOwnerIsListeningFormat), executor, obj.Object().Name, objOwner.Object.Name);
						}
					}
				}
				else
				{
					if (await fullObj.IsHearer(ConnectionService, AttributeService) || await fullObj.IsListener())
					{
						if (await ConnectionService.IsConnected(fullObj))
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectSpeechConnectedFormat), executor, obj.Object().Name);
						else
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectSpeechFormat), executor, obj.Object().Name);
					}

					if (await fullObj.HasActiveCommands(AttributeService))
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectCommandsFormat), executor, obj.Object().Name);
				}
			}
		}

		if (!connectFlag && !inventoryFlag && location.IsRoom && exitsFlag)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepListeningExits), executor);
			if (await locationAnyObject.IsAudible())
			{
				var exits = location.Content(Mediator).Where(x => x.IsExit)
					.Where((item, ct) => perceive(item.Object().DBRef, ct));
				await foreach (var exit in exits.WithCancellation(ExecutionBudget.CurrentToken))
				{
					if (await exit.WithRoomOption().IsAudible())
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepExitBroadcastingFormat), executor, exit.Object().Name);
					}
				}
			}
		}

		if (!hereFlag && !exitsFlag && inventoryFlag)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepListeningInInventory), executor);
			await foreach (var obj in executor.AsContainer.Content(Mediator)
				.Where((item, ct) => perceive(item.Object().DBRef, ct)).WithCancellation(ExecutionBudget.CurrentToken))
			{
				var fullObj = obj.WithRoomOption();
				var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
				if (connectFlag)
				{
					if (await IsConnectedOrPuppetConnected(fullObj))
					{
						if (obj.IsPlayer)
						{
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectIsListeningFormat), executor, obj.Object().Name);
						}
						else
						{
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectOwnerIsListeningFormat), executor, obj.Object().Name, objOwner.Object.Name);
						}
					}
				}
				else
				{
					if (await fullObj.IsHearer(ConnectionService, AttributeService) || await fullObj.IsListener())
					{
						if (await ConnectionService.IsConnected(fullObj))
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectSpeechConnectedFormat), executor, obj.Object().Name);
						else
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectSpeechFormat), executor, obj.Object().Name);
					}

					if (await fullObj.HasActiveCommands(AttributeService))
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectCommandsFormat), executor, obj.Object().Name);
				}
			}
		}

		return CallState.Empty;

		async Task<bool> IsConnectedOrPuppetConnected(AnySharpObject obj)
		{
			if (await ConnectionService.IsConnected(obj)) return true;

			return await obj.IsPuppet()
						 && await ConnectionService.IsConnected(await obj.Object().Owner.WithCancellation(CancellationToken.None));
		}
	}

	[GeneratedRegex(@"\s{2,}")]
	private static partial Regex MultipleWhitespaceRegex();
}
