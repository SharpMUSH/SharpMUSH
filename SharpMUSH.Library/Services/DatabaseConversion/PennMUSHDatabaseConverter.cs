using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Diagnostics;

namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// Converts PennMUSH database format to SharpMUSH objects.
/// </summary>
public class PennMUSHDatabaseConverter : IPennMUSHDatabaseConverter
{
	private readonly PennMUSHDatabaseParser _parser;
	private readonly ILogger<PennMUSHDatabaseConverter> _logger;
	private readonly IMediator _mediator;
	private readonly IOptionsWrapper<SharpMUSHOptions> _options;
	private readonly ConfigurationReloadService? _configurationReload;

	public PennMUSHDatabaseConverter(
		PennMUSHDatabaseParser parser,
		IMediator mediator,
		IOptionsWrapper<SharpMUSHOptions> options,
		ILogger<PennMUSHDatabaseConverter> logger,
		ConfigurationReloadService? configurationReload = null)
	{
		_parser = parser;
		_mediator = mediator;
		_options = options;
		_logger = logger;
		_configurationReload = configurationReload;
	}

	public async Task<ConversionResult> ConvertDatabaseAsync(string databaseFilePath, CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("Starting conversion of PennMUSH database from: {FilePath}", databaseFilePath);

		var pennDatabase = await _parser.ParseFileAsync(databaseFilePath, cancellationToken);
		return await ConvertDatabaseAsync(pennDatabase, cancellationToken);
	}

	public async Task<ConversionResult> ConvertDatabaseAsync(
		string databaseFilePath,
		IProgress<ConversionProgress> progress,
		CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("Starting conversion of PennMUSH database from: {FilePath}", databaseFilePath);

		var pennDatabase = await _parser.ParseFileAsync(databaseFilePath, cancellationToken);
		return await ConvertDatabaseAsync(pennDatabase, progress, cancellationToken);
	}

	public async Task<ConversionResult> ConvertDatabaseAsync(PennMUSHDatabase pennDatabase, CancellationToken cancellationToken = default)
	{
		return await ConvertDatabaseAsync(pennDatabase, null, cancellationToken);
	}

	public async Task<ConversionResult> ConvertDatabaseAsync(
		PennMUSHDatabase pennDatabase,
		IProgress<ConversionProgress>? progress,
		CancellationToken cancellationToken = default)
	{
		// Per-conversion, never a field: the converter is a singleton, so state kept on it is shared
		// with every other import running at the same time. See PennMUSHConversionContext.
		var context = new PennMUSHConversionContext();
		var errors = context.Errors;
		var warnings = context.Warnings;

		var stopwatch = Stopwatch.StartNew();
		var result = new ConversionResult();

		var totalObjects = pennDatabase.Objects.Count;
		var playersConverted = 0;
		var roomsConverted = 0;
		var thingsConverted = 0;
		var exitsConverted = 0;
		var attributesConverted = 0;
		var locksConverted = 0;

		_logger.LogInformation("Converting {Count} PennMUSH objects to SharpMUSH format", totalObjects);

		void ReportProgress(string phase, double percentComplete)
		{
			if (progress == null) return;

			var elapsed = stopwatch.Elapsed;
			TimeSpan? estimatedRemaining = percentComplete > 0.001
				? TimeSpan.FromSeconds(elapsed.TotalSeconds / percentComplete * (1 - percentComplete))
				: null;

			progress.Report(new ConversionProgress
			{
				TotalObjects = totalObjects,
				ProcessedObjects = playersConverted + roomsConverted + thingsConverted + exitsConverted,
				PlayersCreated = playersConverted,
				RoomsCreated = roomsConverted,
				ThingsCreated = thingsConverted,
				ExitsCreated = exitsConverted,
				AttributesCreated = attributesConverted,
				LocksCreated = locksConverted,
				CurrentPhase = phase,
				PercentageComplete = percentComplete * 100,
				ElapsedTime = elapsed,
				EstimatedTimeRemaining = estimatedRemaining,
				RecentErrors = errors.TakeLast(5).ToList(),
				RecentWarnings = warnings.TakeLast(5).ToList()
			});
		}

		try
		{
			// Ahead of the first progress report, since nothing to report on exists until it has run.
			await ImportDefinitionsAsync(pennDatabase, context, cancellationToken);
			ReportProgress("Creating objects", 0.0);

			var objectCounts = await CreateObjectsAsync(pennDatabase, context, cancellationToken);
			playersConverted = objectCounts.players;
			roomsConverted = objectCounts.rooms;
			thingsConverted = objectCounts.things;
			exitsConverted = objectCounts.exits;
			ReportProgress("Objects created", 0.25);

			await EstablishRelationshipsAsync(pennDatabase, context, cancellationToken);
			context.ReportUnconverted();
			ReportProgress("Relationships established", 0.50);

			attributesConverted = await CreateAttributesAsync(pennDatabase, context, cancellationToken);
			ReportProgress("Attributes created", 0.75);

			locksConverted = await CreateLocksAsync(pennDatabase, context, cancellationToken);
			ReportProgress("Locks created", 1.0);

			await EnableParenGroupsAsync(warnings, cancellationToken);

			stopwatch.Stop();

			result = result with
			{
				PlayersConverted = playersConverted,
				RoomsConverted = roomsConverted,
				ThingsConverted = thingsConverted,
				ExitsConverted = exitsConverted,
				AttributesConverted = attributesConverted,
				LocksConverted = locksConverted,
				Errors = errors,
				Warnings = warnings,
				Duration = stopwatch.Elapsed
			};

			_logger.LogInformation(
				"Conversion completed in {Duration}. Players: {Players}, Rooms: {Rooms}, Things: {Things}, Exits: {Exits}, Attributes: {Attributes}",
				stopwatch.Elapsed, playersConverted, roomsConverted, thingsConverted, exitsConverted, attributesConverted);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error during database conversion");
			errors.Add($"Fatal error: {ex.Message}");
			stopwatch.Stop();
			result = result with { Errors = errors, Warnings = warnings, Duration = stopwatch.Elapsed };
		}

		return result;
	}

	/// <summary>
	/// Turns on <c>paren_groups</c> in the configuration of the world being written. PennMUSH softcode
	/// writes a literal parenthesis inside a function's arguments unescaped, which SharpMUSH reads as
	/// PennMUSH does only with that option on. Written through the Mediator, like the objects, so it
	/// lands in the same world, and signalled so a running game rereads its configuration.
	/// </summary>
	/// <remarks>
	/// It runs after the whole world is written, so a failure here is a warning: the conversion stands,
	/// and the option is left for the administrator to set.
	/// </remarks>
	private async ValueTask EnableParenGroupsAsync(List<string> warnings, CancellationToken cancellationToken)
	{
		try
		{
			var options = _options.CurrentValue;
			if (options.Compatibility.ParenGroups) return;

			await _mediator.Send(new SetExpandedServerDataCommand(nameof(SharpMUSHOptions),
				options with { Compatibility = options.Compatibility with { ParenGroups = true } }), cancellationToken);
			_configurationReload?.SignalChange();
			_logger.LogInformation("Turned on paren_groups for the imported PennMUSH softcode");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "Could not turn on paren_groups for the imported PennMUSH softcode");
			warnings.Add($"paren_groups could not be turned on ({ex.Message}); imported softcode that writes literal " +
				"parentheses unescaped needs it: set paren_groups to yes in the configuration and reload it.");
		}
	}

	/// <summary>
	/// Hands a seeded object the identity of the source object it stands in for: its name, God's
	/// password, and its PennMUSH times.
	/// </summary>
	/// <remarks>
	/// #0, #1 and #2 already exist in a migrated database, so the importer reuses them instead of
	/// creating them and never reaches the timestamp-aware create path. Left unstamped they keep the
	/// seed's startup time and their PennMUSH objids do not resolve — God's especially, which imported
	/// softcode references constantly. Every write goes through the Mediator: a running game has
	/// usually read these three already, and a raw store write would leave its cached copies stale.
	/// </remarks>
	private async Task<DBRef> AdoptSeededObjectAsync(AnySharpObject seeded, PennMUSHObject pennObject,
		CancellationToken cancellationToken)
	{
		var number = seeded.Object().Key;
		await _mediator.Send(new SetNameCommand(seeded, MarkupText.Plain(pennObject.Name)), cancellationToken);

		// Before the restamp, because the command carries the object as read before it. The order is
		// otherwise free: an imported PennMUSH hash validates against salt + plaintext, never the objid.
		if (seeded is SharpPlayer seededPlayer && !string.IsNullOrEmpty(pennObject.Password))
		{
			await _mediator.Send(new SetPlayerPasswordCommand(seededPlayer, ImportedPassword(pennObject.Password),
				StoredVerbatim), cancellationToken);
		}

		var (created, modified) = PennTimestamps(pennObject);
		if (created is null && modified is null)
		{
			return new DBRef(number);
		}

		// A source with no creation time keeps the seeded one, and with it the objid.
		var creation = created ?? seeded.Object().CreationTime;
		await _mediator.Send(new SetObjectTimestampsCommand(new DBRef(number), creation, modified),
			cancellationToken);
		_logger.LogDebug("Restamped reused object #{DBRef} with creation time {Created}", number, creation);

		return new DBRef(number, creation);
	}

	/// <summary>
	/// PennMUSH's creation/modification stamps, scaled into the milliseconds SharpMUSH stores.
	/// </summary>
	/// <remarks>
	/// PennMUSH keeps a <c>time_t</c> in seconds (<c>src/db.c</c> writes <c>o-&gt;creation_time</c>
	/// as an int) while SharpMUSH keeps milliseconds and puts them in the objid, so unscaled stamps
	/// would date every imported object to January 1970.
	/// <para>An object with no recorded creation time (a 0 field) defaults to now, since 1970 is not
	/// a more truthful answer than the import date.</para>
	/// </remarks>
	internal static (long? Created, long? Modified) PennTimestamps(PennMUSHObject pennObject)
		=> (pennObject.CreationTime > 0 ? pennObject.CreationTime * 1000 : null,
			pennObject.ModificationTime > 0 ? pennObject.ModificationTime * 1000 : null);

	/// <summary>
	/// The source's definition tables — its flags, powers and standard attributes — before the object,
	/// flag and attribute writes that resolve names against them.
	/// </summary>
	/// <remarks>
	/// <para>A definition this server already has, by name or by alias, is left exactly as it is: a
	/// built-in flag's permissions and type restrictions gate this server's own <c>@set</c> and
	/// permission checks, and a system definition cannot be rewritten at all. What the source has and
	/// this server does not is created without <see cref="SharpObjectFlag.System"/>, so an administrator
	/// can still drop it afterwards.</para>
	/// <para>PennMUSH's own definitions stay behind: <c>internal</c> among a flag's permissions marks
	/// server state rather than site configuration — CONNECTED is a live session, GOING a queued
	/// destruction — and the one internal standard attribute, XYXXY, is the password slot the parser
	/// lifts into <see cref="PennMUSHObject.Password"/>.</para>
	/// <para>Four collection reads front the pass and one create covers each new definition, so a stock
	/// table costs those reads rather than a point query per row. Every write goes through the Mediator,
	/// which invalidates the definition caches a running game has already read.</para>
	/// </remarks>
	private async Task ImportDefinitionsAsync(PennMUSHDatabase pennDatabase, PennMUSHConversionContext context,
		CancellationToken cancellationToken)
	{
		await ImportFlagDefinitionsAsync(pennDatabase.FlagDefinitions, context, cancellationToken);
		await ImportPowerDefinitionsAsync(pennDatabase.PowerDefinitions, context, cancellationToken);
		await ImportAttributeDefinitionsAsync(pennDatabase, context, cancellationToken);
	}

	/// <summary>
	/// The source's object flags, under the rules in <see cref="ImportDefinitionsAsync"/>. A flag
	/// keeps every alias this server has not already spent elsewhere.
	/// </summary>
	private async Task ImportFlagDefinitionsAsync(List<PennMUSHFlagDefinition> definitions,
		PennMUSHConversionContext context, CancellationToken cancellationToken)
	{
		if (definitions.Count == 0)
		{
			return;
		}

		var known = new KnownDefinitions(await _mediator.CreateStream(new GetAllObjectFlagsQuery(), cancellationToken)
			.Select(flag => new KnownDefinition(flag.Name, flag.Symbol, flag.TypeRestrictions, flag.Aliases ?? []))
			.ToArrayAsync(cancellationToken));

		var kept = new List<string>();
		var created = 0;

		foreach (var definition in definitions)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (definition.IsInternal)
			{
				continue;
			}

			if (known.Resolve(definition.Name) is { } existing)
			{
				kept.Add(definition.Name);
				ReportAliasesOfKept("Flag", definition, existing, known, context);
				ReportNarrowerKept("Flag", definition, existing, context);
				continue;
			}

			var letter = UsableLetter("Flag", definition, known, context);
			var aliases = UsableAliases("Flag", definition, known, context);
			var flag = await _mediator.Send(new CreateObjectFlagCommand(definition.Name, aliases, letter, false,
				[.. definition.SetPermissions], [.. definition.UnsetPermissions], [.. definition.Types]), cancellationToken);
			if (flag is null)
			{
				context.Warnings.Add($"Flag {definition.Name} from the source's flag table could not be created");
				continue;
			}

			known.Add(new KnownDefinition(flag.Name, flag.Symbol, flag.TypeRestrictions, aliases));
			created++;
		}

		ReportKept("flag", kept, context);
		_logger.LogInformation("Imported {Created} flag definitions, keeping SharpMUSH's for {Kept}", created, kept.Count);
	}

	/// <summary>
	/// The source's powers, under the rules in <see cref="ImportDefinitionsAsync"/>. The one
	/// difference from a flag is the alias: <see cref="SharpPower.Alias"/> holds one, and PennMUSH
	/// writes a row per alias, so a power with two keeps the first and reports the rest.
	/// </summary>
	private async Task ImportPowerDefinitionsAsync(List<PennMUSHFlagDefinition> definitions,
		PennMUSHConversionContext context, CancellationToken cancellationToken)
	{
		if (definitions.Count == 0)
		{
			return;
		}

		var known = new KnownDefinitions(await _mediator.CreateStream(new GetPowersQuery(), cancellationToken)
			.Select(power => new KnownDefinition(power.Name, power.Symbol, power.TypeRestrictions,
				string.IsNullOrEmpty(power.Alias) ? [] : [power.Alias]))
			.ToArrayAsync(cancellationToken));

		var kept = new List<string>();
		var created = 0;

		foreach (var definition in definitions)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (definition.IsInternal)
			{
				continue;
			}

			if (known.Resolve(definition.Name) is { } existing)
			{
				kept.Add(definition.Name);
				ReportAliasesOfKept("Power", definition, existing, known, context);
				ReportNarrowerKept("Power", definition, existing, context);
				continue;
			}

			var letter = UsableLetter("Power", definition, known, context);
			var aliases = UsableAliases("Power", definition, known, context);

			// SharpMUSH gives a power one alias; PennMUSH writes a row per alias, and Announce has two.
			if (aliases.Length > 1)
			{
				context.Warnings.Add($"Power {definition.Name}: SharpMUSH gives a power one alias, so only " +
					$"{aliases[0]} is imported ({string.Join(" ", aliases[1..])} dropped)");
				aliases = [aliases[0]];
			}

			var power = await _mediator.Send(new CreatePowerCommand(definition.Name,
				aliases.Length == 0 ? string.Empty : aliases[0], letter, false,
				[.. definition.SetPermissions], [.. definition.UnsetPermissions], [.. definition.Types]), cancellationToken);
			if (power is null)
			{
				context.Warnings.Add($"Power {definition.Name} from the source's power table could not be created");
				continue;
			}

			known.Add(new KnownDefinition(power.Name, power.Symbol, power.TypeRestrictions, aliases));
			created++;
		}

		ReportKept("power", kept, context);
		_logger.LogInformation("Imported {Created} power definitions, keeping SharpMUSH's for {Kept}", created, kept.Count);
	}

	/// <summary>
	/// The source's standard attributes: the default flags an attribute of each name is created with.
	/// </summary>
	/// <remarks>
	/// The table is read whole through <see cref="GetAllAttributeEntriesQuery"/> rather than a point
	/// query per row, which a 213-name stock table would make 213 of.
	/// </remarks>
	private async Task ImportAttributeDefinitionsAsync(PennMUSHDatabase pennDatabase,
		PennMUSHConversionContext context, CancellationToken cancellationToken)
	{
		var definitions = pennDatabase.AttributeDefinitions;
		if (definitions.Count == 0)
		{
			return;
		}

		var flagTable = await _mediator.CreateStream(new GetAttributeFlagsQuery(), cancellationToken)
			.ToArrayAsync(cancellationToken);
		var known = new HashSet<string>(await _mediator.CreateStream(new GetAllAttributeEntriesQuery(), cancellationToken)
			.Select(entry => entry.Name).ToArrayAsync(cancellationToken), StringComparer.OrdinalIgnoreCase);

		var kept = new List<string>();
		var aliased = new List<string>();
		var created = 0;

		foreach (var definition in definitions)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (definition.IsInternal)
			{
				continue;
			}

			// SharpMUSH resolves an attribute name without aliases, so DESC does not reach DESCRIBE.
			aliased.AddRange(definition.Aliases);

			if (!known.Add(definition.Name))
			{
				kept.Add(definition.Name);
				continue;
			}

			if (definition.Data.Length > 0)
			{
				context.Warnings.Add($"Standard attribute {definition.Name}: its default value is not imported, " +
					"because SharpMUSH's attribute table holds none");
			}

			if (definition.Creator is { } creator && creator != 0 && creator != pennDatabase.GodPlayer)
			{
				context.Warnings.Add($"Standard attribute {definition.Name}: #{creator} defined it in the source, " +
					"which SharpMUSH's attribute table does not record");
			}

			// One flag name SharpMUSH does not know fails the whole set, as string_to_atrflagsets does
			// (src/attrib.c): the definition arrives with no default flags rather than with some of them.
			var named = definition.Flags.Select(flagTable.Named).ToArray();
			string[] flags = [.. named.OfType<SharpAttributeFlag>().Select(flag => flag.Name)];
			if (flags.Length != named.Length)
			{
				context.Warnings.Add($"Standard attribute {definition.Name}: its default flags " +
					$"[{string.Join(", ", definition.Flags)}] are not imported: {ErrorMessages.Returns.UnrecognizedAttributeFlag}");
				flags = [];
			}

			// Create-if-absent, not the upsert @attribute/access needs: the table was read once, up at
			// the top, so a name absent then can have been defined since by a concurrent import or by a
			// live @attribute. The check and the write have to be one step for this pass to keep the
			// promise it makes about definitions this server already has.
			if (await _mediator.Send(new CreateAttributeEntryIfAbsentCommand(definition.Name, flags), cancellationToken) is null)
			{
				kept.Add(definition.Name);
				continue;
			}

			created++;
		}

		ReportKept("standard attribute", kept, context);
		if (aliased.Count > 0)
		{
			context.Warnings.Add($"{aliased.Count} source attribute alias(es) are not imported, because SharpMUSH " +
				$"resolves an attribute name without them ({Sample(aliased)})");
		}

		_logger.LogInformation("Imported {Created} standard attribute definitions, keeping SharpMUSH's for {Kept}",
			created, kept.Count);
	}

	/// <summary>A definition this server already has, reduced to what an imported one is checked against.</summary>
	private sealed record KnownDefinition(string Name, string Symbol, string[] TypeRestrictions, string[] Aliases);

	/// <summary>
	/// The definitions this server already has, and every name — each one's own and its aliases — that
	/// resolves to one. A name wins over an alias, as it does in the store's own lookup.
	/// </summary>
	private sealed class KnownDefinitions
	{
		private readonly Dictionary<string, KnownDefinition> _byName = new(StringComparer.OrdinalIgnoreCase);
		private readonly List<KnownDefinition> _all;

		public KnownDefinitions(IEnumerable<KnownDefinition> definitions)
		{
			_all = [.. definitions];
			foreach (var definition in _all)
			{
				_byName[definition.Name] = definition;
			}

			foreach (var alias in _all.SelectMany(definition => definition.Aliases.Select(alias => (alias, definition))))
			{
				_byName.TryAdd(alias.alias, alias.definition);
			}
		}

		public KnownDefinition? Resolve(string name) => _byName.GetValueOrDefault(name);

		/// <summary>
		/// The definition already spending a letter on an object type one of <paramref name="types"/>
		/// covers, if any. PennMUSH allows a letter one meaning per type (<c>letter_to_flagptr</c>,
		/// src/flags.c), which is why SharpMUSH can seed ABODE and ANSI both on <c>A</c>.
		/// </summary>
		public KnownDefinition? LetterHolder(string letter, List<string> types)
			=> letter.Length == 0
				? null
				: _all.Find(definition => definition.Symbol == letter && Overlaps(definition.TypeRestrictions, types));

		public void Add(KnownDefinition definition)
		{
			_all.Add(definition);
			_byName[definition.Name] = definition;
			foreach (var alias in definition.Aliases)
			{
				_byName.TryAdd(alias, definition);
			}
		}

		/// <summary>Whether two type restrictions can meet on one object; empty means any type.</summary>
		private static bool Overlaps(string[] left, List<string> right)
			=> left.Length == 0 || right.Count == 0 || left.Intersect(right, StringComparer.OrdinalIgnoreCase).Any();
	}

	/// <summary>
	/// The source aliases an imported definition may keep. One this server already resolves elsewhere
	/// would shadow that definition, so it is dropped and reported.
	/// </summary>
	private static string[] UsableAliases(string kind, PennMUSHFlagDefinition definition, KnownDefinitions known,
		PennMUSHConversionContext context)
	{
		var usable = new List<string>(definition.Aliases.Count);
		foreach (var alias in definition.Aliases)
		{
			if (known.Resolve(alias) is { } holder)
			{
				context.Warnings.Add(
					$"{kind} {definition.Name}: its alias {alias} is not imported, because {holder.Name} answers to it here");
			}
			else
			{
				usable.Add(alias);
			}
		}

		return [.. usable];
	}

	/// <summary>
	/// The letter an imported definition may keep. One this server already spends on a definition of an
	/// overlapping type is dropped and reported; the definition itself still arrives.
	/// </summary>
	private static string UsableLetter(string kind, PennMUSHFlagDefinition definition, KnownDefinitions known,
		PennMUSHConversionContext context)
	{
		if (known.LetterHolder(definition.Letter, definition.Types) is not { } holder)
		{
			return definition.Letter;
		}

		context.Warnings.Add(
			$"{kind} {definition.Name}: its letter {definition.Letter} is not imported, because {holder.Name} uses it here");
		return string.Empty;
	}

	/// <summary>
	/// What a source definition this server already has loses by that: a name it answered to there and
	/// does not here, or one that means something else here, both of which imported softcode may use.
	/// </summary>
	private static void ReportAliasesOfKept(string kind, PennMUSHFlagDefinition definition, KnownDefinition existing,
		KnownDefinitions known, PennMUSHConversionContext context)
	{
		foreach (var alias in definition.Aliases)
		{
			switch (known.Resolve(alias))
			{
				case null:
					context.Warnings.Add(
						$"{kind} {existing.Name}: SharpMUSH's own definition is kept and does not answer to {alias}");
					break;

				case { } other when !other.Name.Equals(existing.Name, StringComparison.OrdinalIgnoreCase):
					context.Warnings.Add($"{kind} {existing.Name}: {alias} names it in the source, but {other.Name} here");
					break;
			}
		}
	}

	/// <summary>
	/// What an imported object loses to a definition this server keeps a narrower version of. A flag
	/// the source allowed on a type this server does not will be refused object by object in
	/// <see cref="SetFlagsAsync"/>, which is far from the table that knew the source's own type list.
	/// </summary>
	private static void ReportNarrowerKept(string kind, PennMUSHFlagDefinition definition, KnownDefinition existing,
		PennMUSHConversionContext context)
	{
		if (existing.TypeRestrictions.Length == 0 || definition.Types.Count == 0)
		{
			return;
		}

		var lost = definition.Types.Except(existing.TypeRestrictions, StringComparer.OrdinalIgnoreCase).ToArray();
		if (lost.Length == 0)
		{
			return;
		}

		context.Warnings.Add($"{kind} {existing.Name}: the source allows it on {string.Join(" ", lost)}, " +
			"which SharpMUSH's own definition does not, so objects of those types will not keep it");
	}

	/// <summary>
	/// The source definitions this server already has, as one line for the lot: a stock PennMUSH table
	/// overlaps SharpMUSH's almost entirely, and a line each would bury the rest of the report.
	/// </summary>
	private static void ReportKept(string kind, List<string> kept, PennMUSHConversionContext context)
	{
		if (kept.Count == 0)
		{
			return;
		}

		context.Warnings.Add($"{kept.Count} source {kind} definition(s) already exist here, " +
			$"so SharpMUSH's own are kept ({Sample(kept)})");
	}

	private static string Sample(List<string> names)
		=> $"{string.Join(" ", names.Take(10))}{(names.Count > 10 ? " ..." : "")}";

	private async Task<(int players, int rooms, int things, int exits)> CreateObjectsAsync(
		PennMUSHDatabase pennDatabase,
		PennMUSHConversionContext context,
		CancellationToken cancellationToken)
	{
		var dbrefMapping = context.DbrefMapping;
		var errors = context.Errors;
		var warnings = context.Warnings;

		int playersConverted = 0, roomsConverted = 0, thingsConverted = 0, exitsConverted = 0;

		_logger.LogInformation("Object creation phase - converting {Count} objects", pennDatabase.Objects.Count);

		if (pennDatabase.Objects.Count == 0)
		{
			_logger.LogInformation("Empty database - no objects to convert");
			return (0, 0, 0, 0);
		}

		// Migration seeds #0-#2 the way PennMUSH's create_minimal_db lays out every database: Room Zero,
		// God (PennMUSH hardcodes GOD as #1) and the Master Room (MASTER_ROOM must be a room). A seeded
		// object stands in for the source's only when both are the same type; a source object of any
		// other type at those numbers is created in the main loop like the rest. A source that lacks one
		// of them still maps its number onto the seeded object, so references to it resolve.
		var godPennObject = pennDatabase.GetObject(1);
		var existingPlayer1 = await _mediator.Send(new GetObjectNodeQuery(new DBRef(1)), cancellationToken);
		DBRef tempGodDbRef;

		if (existingPlayer1 is AnySharpObject seededGod && seededGod.IsPlayer)
		{
			tempGodDbRef = new DBRef(1);
			if (godPennObject?.Type == PennMUSHObjectType.Player)
			{
				tempGodDbRef = await AdoptSeededObjectAsync(seededGod, godPennObject, cancellationToken);
				playersConverted++;
				_logger.LogInformation("Reusing existing God player #1 from database migration: {Name}", godPennObject.Name);
			}

			if (godPennObject is null || godPennObject.Type == PennMUSHObjectType.Player)
			{
				dbrefMapping[1] = tempGodDbRef;
			}
		}
		else if (godPennObject?.Type == PennMUSHObjectType.Player)
		{
			var (godCreated, godModified) = PennTimestamps(godPennObject);
			tempGodDbRef = await _mediator.Send(new CreatePlayerCommand(
				godPennObject.Name,
				ImportedPassword(godPennObject.Password),
				new DBRef(0), // Limbo room (will create or reuse next)
				new DBRef(0), // Home is also Limbo
				QuotaFor(godPennObject, pennDatabase),
				StoredVerbatim,
				ApplyDefaultFlags: false,
				godCreated,
				godModified,
				godPennObject.DBRef), cancellationToken);

			dbrefMapping[1] = tempGodDbRef;
			playersConverted++;
			_logger.LogInformation("Created God player #{PennDBRef} -> {SharpDBRef}: {Name}", 1, tempGodDbRef, godPennObject.Name);
		}
		else
		{
			tempGodDbRef = await _mediator.Send(new CreatePlayerCommand(
				"God",
				PasswordService.LockedHash,
				new DBRef(0),
				new DBRef(0),
				10000,
				StoredVerbatim,
				ApplyDefaultFlags: false), cancellationToken);
			if (godPennObject is null)
			{
				dbrefMapping[1] = tempGodDbRef;
			}

			_logger.LogWarning("Created default God player as #{PennDBRef} was not a player", 1);
		}

		if (await _mediator.Send(new GetObjectNodeQuery(tempGodDbRef), cancellationToken) is not (AnySharpObject and SharpPlayer godPlayerWrapped))
		{
			throw new InvalidOperationException("Failed to retrieve God player after creation or reuse");
		}
		var godPlayer = godPlayerWrapped;

		var room0Penn = pennDatabase.GetObject(0);
		var existingRoom0 = await _mediator.Send(new GetObjectNodeQuery(new DBRef(0)), cancellationToken);
		DBRef tempRoom0DbRef;

		if (existingRoom0 is AnySharpObject seededRoom0 && seededRoom0.IsRoom)
		{
			tempRoom0DbRef = new DBRef(0);
			if (room0Penn?.Type == PennMUSHObjectType.Room)
			{
				tempRoom0DbRef = await AdoptSeededObjectAsync(seededRoom0, room0Penn, cancellationToken);
				roomsConverted++;
				_logger.LogInformation("Reusing existing Limbo room #0 from database migration: {Name}", room0Penn.Name);
			}

			if (room0Penn is null || room0Penn.Type == PennMUSHObjectType.Room)
			{
				dbrefMapping[0] = tempRoom0DbRef;
			}
		}
		else if (room0Penn?.Type == PennMUSHObjectType.Room)
		{
			var (room0Created, room0Modified) = PennTimestamps(room0Penn);
			tempRoom0DbRef = await _mediator.Send(
				new CreateRoomCommand(room0Penn.Name, godPlayer, ApplyDefaultFlags: false, room0Created, room0Modified,
					room0Penn.DBRef),
				cancellationToken);
			dbrefMapping[0] = tempRoom0DbRef;
			roomsConverted++;
			_logger.LogInformation("Created Limbo room #{PennDBRef} -> {SharpDBRef}: {Name}", 0, tempRoom0DbRef, room0Penn.Name);
		}
		else
		{
			tempRoom0DbRef = await _mediator.Send(new CreateRoomCommand("Limbo", godPlayer, ApplyDefaultFlags: false),
				cancellationToken);
			if (room0Penn is null)
			{
				dbrefMapping[0] = tempRoom0DbRef;
			}

			_logger.LogWarning("Created default Limbo room as #{PennDBRef} was not a room", 0);
		}

		var room2Penn = pennDatabase.GetObject(2);
		var existingRoom2 = await _mediator.Send(new GetObjectNodeQuery(new DBRef(2)), cancellationToken);
		if (existingRoom2 is AnySharpObject seededRoom2 && seededRoom2.IsRoom)
		{
			if (room2Penn?.Type == PennMUSHObjectType.Room)
			{
				dbrefMapping[2] = await AdoptSeededObjectAsync(seededRoom2, room2Penn, cancellationToken);
				roomsConverted++;
				_logger.LogInformation("Reusing existing Master Room #2 from database migration: {Name}", room2Penn.Name);
			}
			else if (room2Penn is null)
			{
				dbrefMapping[2] = new DBRef(2);
			}
		}

		SharpRoom? room0 = null; // Cache the limbo room to avoid repeated lookups

		foreach (var pennObj in pennDatabase.Objects)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Skip the seeded objects already standing in for source objects
			if (dbrefMapping.ContainsKey(pennObj.DBRef))
			{
				continue;
			}

			try
			{
				DBRef newDbRef;
				var (created, modified) = PennTimestamps(pennObj);

				switch (pennObj.Type)
				{
					case PennMUSHObjectType.Player:
						{
							// Players start in Limbo temporarily
							newDbRef = await _mediator.Send(new CreatePlayerCommand(
								pennObj.Name,
								ImportedPassword(pennObj.Password),
								tempRoom0DbRef, // Start in Limbo
								tempRoom0DbRef, // Home is Limbo for now
								QuotaFor(pennObj, pennDatabase),
								StoredVerbatim,
								ApplyDefaultFlags: false,
								created,
								modified,
									pennObj.DBRef), cancellationToken);
							playersConverted++;
							break;
						}

					case PennMUSHObjectType.Room:
						{
							// Rooms are created with God as owner initially
							newDbRef = await _mediator.Send(
								new CreateRoomCommand(pennObj.Name, godPlayer, ApplyDefaultFlags: false, created, modified, pennObj.DBRef),
								cancellationToken);
							roomsConverted++;
							break;
						}

					case PennMUSHObjectType.Thing:
						{
							// Things need location and home - use Limbo temporarily
							if (room0 == null)
							{
								room0 = await _mediator.Send(new GetObjectNodeQuery(tempRoom0DbRef), cancellationToken) is AnySharpObject and SharpRoom limbo
									? limbo
									: throw new InvalidOperationException("Failed to retrieve Limbo room");
							}

							newDbRef = await _mediator.Send(new CreateThingCommand(
								pennObj.Name,
								room0, // Start in Limbo
								godPlayer, // God owns it temporarily
								room0, // Home is Limbo for now
								ApplyDefaultFlags: false,
								created,
								modified,
								pennObj.DBRef), cancellationToken);
							thingsConverted++;
							break;
						}

					case PennMUSHObjectType.Exit:
						{
							// Exits need location - use Limbo temporarily
							if (room0 == null)
							{
								room0 = await _mediator.Send(new GetObjectNodeQuery(tempRoom0DbRef), cancellationToken) is AnySharpObject and SharpRoom limbo
									? limbo
									: throw new InvalidOperationException("Failed to retrieve Limbo room");
							}

							var (exitName, aliases) = ExitNameAndAliases(pennObj);
							newDbRef = await _mediator.Send(new CreateExitCommand(
								exitName,
								aliases,
								room0, // Start in Limbo
								godPlayer, // God owns it temporarily
								ApplyDefaultFlags: false,
								created,
								modified,
								pennObj.DBRef), cancellationToken);
							exitsConverted++;
							break;
						}

					default:
						warnings.Add($"Unknown object type for #{pennObj.DBRef}: {pennObj.Type}");
						continue;
				}

				dbrefMapping[pennObj.DBRef] = newDbRef;

				_logger.LogDebug("Created object #{PennDBRef} -> {SharpDBRef}: {Name}",
					pennObj.DBRef, newDbRef, pennObj.Name);
			}
			catch (Exception ex)
			{
				var error = $"Failed to convert object #{pennObj.DBRef} ({pennObj.Name}): {ex.Message}";
				_logger.LogError(ex, "Conversion error for object #{DBRef}", pennObj.DBRef);
				errors.Add(error);
			}
		}

		var withPennies = pennDatabase.Objects.Count(o => o.Pennies > 0);
		if (withPennies > 0)
		{
			warnings.Add($"Pennies on {withPennies} object(s) were not imported: SharpMUSH does not track money.");
		}

		return (playersConverted, roomsConverted, thingsConverted, exitsConverted);
	}

	/// <summary>
	/// A player's quota limit. SharpMUSH's quota is the limit itself; PennMUSH keeps what is left of it
	/// in the RQUOTA attribute (src/wiz.c, <c>do_quota</c>), so the limit is what the player owns plus
	/// that. Without RQUOTA PennMUSH would derive one from its own <c>starting_quota</c>, which the dump
	/// does not carry, so this game's stands in, never below what the player already owns.
	/// </summary>
	private int QuotaFor(PennMUSHObject player, PennMUSHDatabase pennDatabase)
	{
		// PennMUSH does not count the player itself (get_current_quota in src/predicat.c).
		var owned = pennDatabase.Objects.Count(o => o.Owner == player.DBRef && o.DBRef != player.DBRef);
		var remaining = player.Attributes.Find(a => a.Name.Equals(RemainingQuota, StringComparison.OrdinalIgnoreCase));
		return remaining is not null && int.TryParse(remaining.Value, out var left)
			? owned + left
			: Math.Max(owned, (int)_options.CurrentValue.Limit.StartingQuota);
	}

	/// <summary>PennMUSH's remaining-quota attribute, which becomes the player's quota rather than an attribute.</summary>
	private const string RemainingQuota = "RQUOTA";

	private async Task EstablishRelationshipsAsync(
		PennMUSHDatabase pennDatabase,
		PennMUSHConversionContext context,
		CancellationToken cancellationToken)
	{
		var dbrefMapping = context.DbrefMapping;
		var errors = context.Errors;
		var warnings = context.Warnings;

		_logger.LogInformation("Establishing object relationships for {Count} objects", pennDatabase.Objects.Count);

		foreach (var pennObj in pennDatabase.Objects)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (!dbrefMapping.TryGetValue(pennObj.DBRef, out var sharpDbRef))
			{
				continue;
			}

			try
			{
				if (await _mediator.Send(new GetObjectNodeQuery(sharpDbRef), cancellationToken) is not AnySharpObject sharpObj)
				{
					warnings.Add($"Could not retrieve object #{sharpDbRef} for relationship setup");
					continue;
				}

				// Handle location for content objects (players, things, exits)
				if (pennObj.Type != PennMUSHObjectType.Room && pennObj.Location >= 0)
				{
					if (dbrefMapping.TryGetValue(pennObj.Location, out var locationDbRef))
					{
						var locationObj = await _mediator.Send(new GetObjectNodeQuery(locationDbRef), cancellationToken);
						var container = TryGetContainer(locationObj);

						if (container != null)
						{
							// Rooms aren't content
							if (sharpObj.IsContent)
							{
								var content = sharpObj.AsContent;

								// A conversion is building a world out of a dump, not moving anyone: there is no
								// actor, no parser and nobody present to notify, so this places the object
								// directly rather than going through the movement pipeline and its triads.
								var currentContainer = await content.Location();

								await _mediator.Send(new MoveObjectCommand(
									content,
									container,
									Enactor: null,
									IsSilent: true,
									Cause: "conversion",
									OldContainer: currentContainer.Object().DBRef), cancellationToken);
							}
						}
					}
				}

				if (pennObj.Type == PennMUSHObjectType.Exit && pennObj.Link >= 0)
				{
					if (dbrefMapping.TryGetValue(pennObj.Link, out var destDbRef))
					{
						var destObj = await _mediator.Send(new GetObjectNodeQuery(destDbRef), cancellationToken);
						var container = TryGetContainer(destObj);

						if (container != null && sharpObj is SharpExit exit)
						{
							await _mediator.Send(new LinkExitCommand(exit, container), cancellationToken);
							_logger.LogDebug("Linked exit #{PennDBRef} to destination #{DestDBRef}", pennObj.DBRef, pennObj.Link);
						}
					}
				}

				if (pennObj.Parent >= 0 && dbrefMapping.TryGetValue(pennObj.Parent, out var parentDbRef))
				{
					if (await _mediator.Send(new GetObjectNodeQuery(parentDbRef), cancellationToken) is AnySharpObject parentObj)
					{
						await _mediator.Send(new SetObjectParentCommand(sharpObj, parentObj), cancellationToken);
						_logger.LogDebug("Set parent for #{PennDBRef} to #{ParentDBRef}", pennObj.DBRef, pennObj.Parent);
					}
					else
					{
						warnings.Add($"Parent object #{pennObj.Parent} not found for object #{pennObj.DBRef}");
					}
				}

				if (pennObj.Zone >= 0 && dbrefMapping.TryGetValue(pennObj.Zone, out var zoneDbRef))
				{
					if (await _mediator.Send(new GetObjectNodeQuery(zoneDbRef), cancellationToken) is AnySharpObject zoneObj)
					{
						await _mediator.Send(new SetObjectZoneCommand(sharpObj, zoneObj), cancellationToken);
						_logger.LogDebug("Set zone for #{PennDBRef} to #{ZoneDBRef}", pennObj.DBRef, pennObj.Zone);
					}
					else
					{
						warnings.Add($"Zone object #{pennObj.Zone} not found for object #{pennObj.DBRef}");
					}
				}

				var target = sharpObj;
				await SetOwnerAsync(pennObj, target, context, cancellationToken);
				await SetHomeOrDropToAsync(pennObj, target, context, cancellationToken);
				await SetFlagsAsync(pennObj, target, context, cancellationToken);
				await SetPowersAsync(pennObj, target, context, cancellationToken);
				await SetWarningsAsync(pennObj, target, context, cancellationToken);
			}
			catch (Exception ex)
			{
				var error = $"Failed to establish relationships for object #{pennObj.DBRef}: {ex.Message}";
				_logger.LogError(ex, "Relationship error for object #{DBRef}", pennObj.DBRef);
				errors.Add(error);
			}
		}
	}

	/// <summary>The SharpMUSH object a source dbref became, or none if it was not imported.</summary>
	private async Task<AnyOptionalSharpObject> MappedAsync(int pennDbref, PennMUSHConversionContext context,
		CancellationToken cancellationToken)
		=> context.DbrefMapping.TryGetValue(pennDbref, out var dbref)
			? await _mediator.Send(new GetObjectNodeQuery(dbref), cancellationToken)
			: new None();

	/// <summary>The source owner, resolved through the conversion's mapping. A player owns itself.</summary>
	private async Task SetOwnerAsync(PennMUSHObject pennObj, AnySharpObject target, PennMUSHConversionContext context,
		CancellationToken cancellationToken)
	{
		if (pennObj.Owner < 0)
		{
			return;
		}

		if (await MappedAsync(pennObj.Owner, context, cancellationToken) is not (AnySharpObject and SharpPlayer owner))
		{
			context.Warnings.Add($"Owner #{pennObj.Owner} of #{pennObj.DBRef} is not an imported player; God keeps it");
			return;
		}

		await _mediator.Send(new SetObjectOwnerCommand(target, owner), cancellationToken);
	}

	/// <summary>
	/// PennMUSH keeps a thing's or player's home, and a room's drop-to, in the link field the parser
	/// hands over as <see cref="PennMUSHObject.Link"/>. An exit's link is its destination, set above.
	/// </summary>
	private async Task SetHomeOrDropToAsync(PennMUSHObject pennObj, AnySharpObject target,
		PennMUSHConversionContext context, CancellationToken cancellationToken)
	{
		if (pennObj.Type == PennMUSHObjectType.Exit || pennObj.Link < 0)
		{
			return;
		}

		var container = TryGetContainer(await MappedAsync(pennObj.Link, context, cancellationToken));
		if (container is null)
		{
			context.Warnings.Add(
				$"{(target.IsRoom ? "Drop-to" : "Home")} #{pennObj.Link} of #{pennObj.DBRef} is not an imported room, thing or player");
			return;
		}

		if (target is SharpRoom targetRoom)
		{
			await _mediator.Send(new LinkRoomCommand(targetRoom, container.WithNoneOption()), cancellationToken);
		}
		else
		{
			await _mediator.Send(new SetObjectHomeCommand(target.AsContent, container), cancellationToken);
		}
	}

	/// <summary>
	/// Imported objects are created without this game's default flags, so the source's are the whole
	/// set. CONNECTED is session state that PennMUSH itself clears on every load (db_read in src/db.c).
	/// </summary>
	private async Task SetFlagsAsync(PennMUSHObject pennObj, AnySharpObject target, PennMUSHConversionContext context,
		CancellationToken cancellationToken)
	{
		foreach (var name in pennObj.Flags)
		{
			if (name.Equals("CONNECTED", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var flag = await _mediator.Send(new GetObjectFlagQuery(name), cancellationToken);
			if (flag is null)
			{
				context.NoteUnconverted($"Flag {name.ToUpperInvariant()}, which SharpMUSH does not have", pennObj.DBRef);
			}
			else if (!AllowedOn(flag.TypeRestrictions, target))
			{
				context.NoteUnconverted($"Flag {flag.Name}, which SharpMUSH does not allow on a {target.Object().Type}", pennObj.DBRef);
			}
			else
			{
				await _mediator.Send(new SetObjectFlagCommand(target, flag), cancellationToken);
			}
		}
	}

	/// <summary>Whether a flag or power with these type restrictions may sit on the target; none means any.</summary>
	private static bool AllowedOn(string[] typeRestrictions, AnySharpObject target)
		=> typeRestrictions.Length == 0 || typeRestrictions.Contains(target.Object().Type, StringComparer.OrdinalIgnoreCase);

	private async Task SetPowersAsync(PennMUSHObject pennObj, AnySharpObject target, PennMUSHConversionContext context,
		CancellationToken cancellationToken)
	{
		foreach (var name in pennObj.Powers)
		{
			var power = await _mediator.Send(new GetPowerQuery(name), cancellationToken);
			if (power is null)
			{
				context.NoteUnconverted($"Power {name}, which SharpMUSH does not have", pennObj.DBRef);
			}
			else if (!AllowedOn(power.TypeRestrictions, target))
			{
				context.NoteUnconverted($"Power {power.Name}, which SharpMUSH does not allow on a {target.Object().Type}", pennObj.DBRef);
			}
			else
			{
				await _mediator.Send(new SetObjectPowerCommand(target, power), cancellationToken);
			}
		}
	}

	private async Task SetWarningsAsync(PennMUSHObject pennObj, AnySharpObject target, PennMUSHConversionContext context,
		CancellationToken cancellationToken)
	{
		if (pennObj.Warnings.Count == 0)
		{
			return;
		}

		var unknown = new List<string>();
		var warnings = WarningTypeHelper.ParseWarnings(string.Join(' ', pennObj.Warnings), unknown);
		foreach (var name in unknown)
		{
			context.NoteUnconverted($"Warning {name}, which SharpMUSH does not have", pennObj.DBRef);
		}

		await _mediator.Send(new SetObjectWarningsCommand(target, warnings), cancellationToken);
	}

	/// <summary>
	/// Every object's attributes, as one <see cref="SetAttributesCommand"/> per object.
	/// </summary>
	/// <remarks>
	/// <para>This loads a database; it is not a player typing <c>@set</c>. PennMUSH's loader applies each
	/// attribute's stored flags and creator as they are, with no permission check, so a wizard-only attribute
	/// on a mortal's object stays wizard-only and belongs to whoever set it. Two things are kept from
	/// <c>@set</c>. Flag names are matched the same way. One name SharpMUSH does not know fails the
	/// attribute's whole flag list, as it does in <c>string_to_atrflagsets</c>, so the attribute keeps its
	/// value, takes none of its flags, and is reported.</para>
	/// <para>One command per object means one cache invalidation and one store write per object. Doing it
	/// per attribute, with a read before each write, cost the import most of its time.</para>
	/// </remarks>
	private async Task<int> CreateAttributesAsync(
		PennMUSHDatabase pennDatabase,
		PennMUSHConversionContext context,
		CancellationToken cancellationToken)
	{
		var dbrefMapping = context.DbrefMapping;
		var errors = context.Errors;
		var warnings = context.Warnings;

		_logger.LogInformation("Creating attributes");
		var count = 0;
		var flagTable = await _mediator.CreateStream(new GetAttributeFlagsQuery(), cancellationToken)
			.ToArrayAsync(cancellationToken);
		var creators = new Dictionary<int, SharpPlayer?>();

		foreach (var pennObj in pennDatabase.Objects)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (!dbrefMapping.TryGetValue(pennObj.DBRef, out var sharpDbRef))
			{
				continue;
			}

			if (pennObj.Attributes.Count == 0)
			{
				continue;
			}

			try
			{
				if (await _mediator.Send(new GetObjectNodeQuery(sharpDbRef), cancellationToken) is not AnySharpObject sharpObj)
				{
					warnings.Add($"Could not retrieve object #{sharpDbRef} for attribute creation");
					continue;
				}

				var objectOwner = await sharpObj.Object().Owner.WithCancellation(cancellationToken);
				var writes = new List<(PennMUSHAttribute Source, AttributeWrite Write)>(pennObj.Attributes.Count);

				foreach (var pennAttr in pennObj.Attributes)
				{
					if (pennObj.Type == PennMUSHObjectType.Player
						&& pennAttr.Name.Equals(RemainingQuota, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					try
					{
						var named = pennAttr.Flags.Select(flagTable.Named).ToArray();
						SharpAttributeFlag[] flags = [.. named.OfType<SharpAttributeFlag>()];
						if (flags.Length != named.Length)
						{
							warnings.Add(
								$"Failed to set flags [{string.Join(", ", pennAttr.Flags)}] on attribute " +
								$"{pennAttr.Name} of #{pennObj.DBRef}: {ErrorMessages.Returns.UnrecognizedAttributeFlag}");
							flags = [];
						}

						var creator = await CreatorAsync(pennAttr, dbrefMapping, creators, cancellationToken) ?? objectOwner;
						var value = MarkupString.Ansi.AnsiEscapeParser.Parse(pennAttr.Value);
						writes.Add((pennAttr, new AttributeWrite(pennAttr.Name.Split('`'), value, creator, flags)));
					}
					catch (Exception ex)
					{
						warnings.Add($"Failed to set attribute {pennAttr.Name} on #{pennObj.DBRef}: {ex.Message}");
						_logger.LogDebug(ex, "Attribute creation error");
					}
				}

				count += await WriteAttributesAsync(pennObj, sharpDbRef, writes, warnings, cancellationToken);
			}
			catch (Exception ex)
			{
				var error = $"Failed to create attributes for object #{pennObj.DBRef}: {ex.Message}";
				_logger.LogError(ex, "Attribute creation error for object #{DBRef}", pennObj.DBRef);
				errors.Add(error);
			}
		}

		_logger.LogInformation("Created {Count} attributes", count);
		return count;
	}

	/// <summary>
	/// The player who set an attribute in the source (PennMUSH's <c>AL_CREATOR</c>), or null when it names
	/// no player that was imported. Remembered per source dbref, since a handful of players set nearly all
	/// of a game's attributes.
	/// </summary>
	private async ValueTask<SharpPlayer?> CreatorAsync(PennMUSHAttribute pennAttr, IReadOnlyDictionary<int, DBRef> dbrefMapping,
		Dictionary<int, SharpPlayer?> creators, CancellationToken cancellationToken)
	{
		if (pennAttr.Owner is not { } source)
		{
			return null;
		}

		if (!creators.TryGetValue(source, out var creator))
		{
			creator = dbrefMapping.TryGetValue(source, out var mapped)
				&& await _mediator.Send(new GetObjectNodeQuery(mapped), cancellationToken) is AnySharpObject and SharpPlayer node
					? node
					: null;
			creators[source] = creator;
		}

		return creator;
	}

	/// <summary>
	/// One object's attributes in one write. Should that fail, each is written alone, so the warnings
	/// name the attributes that did not arrive rather than the object's whole list.
	/// </summary>
	private async Task<int> WriteAttributesAsync(PennMUSHObject pennObj, DBRef target,
		List<(PennMUSHAttribute Source, AttributeWrite Write)> writes, List<string> warnings,
		CancellationToken cancellationToken)
	{
		if (writes.Count == 0)
		{
			return 0;
		}

		try
		{
			if (await _mediator.Send(new SetAttributesCommand(target, [.. writes.Select(w => w.Write)]), cancellationToken))
			{
				return writes.Count;
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogDebug(ex, "Batched attribute write for #{DBRef} failed; writing its attributes singly", pennObj.DBRef);
		}

		var written = 0;
		foreach (var (source, write) in writes)
		{
			try
			{
				if (await _mediator.Send(new SetAttributesCommand(target, [write]), cancellationToken))
				{
					written++;
				}
				else
				{
					warnings.Add($"Failed to set attribute {source.Name} on #{pennObj.DBRef}: the store refused it");
				}
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				warnings.Add($"Failed to set attribute {source.Name} on #{pennObj.DBRef}: {ex.Message}");
				_logger.LogDebug(ex, "Attribute creation error");
			}
		}

		return written;
	}

	private async Task<int> CreateLocksAsync(
		PennMUSHDatabase pennDatabase,
		PennMUSHConversionContext context,
		CancellationToken cancellationToken)
	{
		var dbrefMapping = context.DbrefMapping;
		var errors = context.Errors;
		var warnings = context.Warnings;

		_logger.LogInformation("Creating locks");
		var count = 0;

		foreach (var pennObj in pennDatabase.Objects)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (!dbrefMapping.TryGetValue(pennObj.DBRef, out var sharpDbRef))
			{
				continue;
			}

			if (pennObj.Locks.Count == 0)
			{
				continue;
			}

			try
			{
				if (await _mediator.Send(new GetObjectNodeQuery(sharpDbRef), cancellationToken) is not AnySharpObject sharpObj)
				{
					warnings.Add($"Could not retrieve object #{sharpDbRef} for lock creation");
					continue;
				}

				foreach (var (lockName, importedLock) in pennObj.Locks)
				{
					try
					{
						var creator = importedLock.Creator is { } creatorNumber && context.DbrefMapping.TryGetValue(creatorNumber, out var mappedCreator)
							? (DBRef?)mappedCreator : null;
						await _mediator.Send(new ImportLockCommand(sharpObj.Object(), lockName,
							importedLock.ToSharpLockData(creator)), cancellationToken);

						count++;
						_logger.LogTrace("Set lock {LockName} on object #{DBRef}", lockName, pennObj.DBRef);
					}
					catch (Exception ex)
					{
						warnings.Add($"Failed to set lock {lockName} on #{pennObj.DBRef}: {ex.Message}");
						_logger.LogDebug(ex, "Lock creation error");
					}
				}
			}
			catch (Exception ex)
			{
				var error = $"Failed to create locks for object #{pennObj.DBRef}: {ex.Message}";
				_logger.LogError(ex, "Lock creation error for object #{DBRef}", pennObj.DBRef);
				errors.Add(error);
			}
		}

		_logger.LogInformation("Created {Count} locks", count);
		return count;
	}

	/// <summary>
	/// An exit's name and the aliases SharpMUSH matches it by. PennMUSH 1.8 keeps the aliases in the
	/// ALIAS attribute, which the parser lifts into <see cref="PennMUSHObject.Aliases"/>; a name written
	/// as <c>north;n</c>, as in <c>@open north;n</c>, carries its own.
	/// </summary>
	private static (string Name, string[] Aliases) ExitNameAndAliases(PennMUSHObject exit)
	{
		var parts = exit.Name.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		var name = parts.Length > 0 ? parts[0] : exit.Name;
		return (name, [.. parts.Skip(1).Concat(exit.Aliases).Distinct(StringComparer.OrdinalIgnoreCase)]);
	}

	/// <summary>
	/// Helper method to convert an AnyOptionalSharpObject to AnySharpContainer if possible.
	/// Returns null if the object is None or an Exit (which can't be containers).
	/// </summary>
	private static AnySharpContainer? TryGetContainer(AnyOptionalSharpObject obj)
	{
		return obj switch
		{
			AnySharpObject found => found switch
			{
				SharpPlayer player => player,
				SharpRoom room => room,
				SharpThing thing => thing,
				SharpExit => null
			},
			None => null
		};
	}

	/// <summary>
	/// Converts Pueblo ANSI escape sequences from text as stored in PennMUSH database files to MarkupStrings.
	/// PennMUSH stores ANSI escape codes as literal ESC sequences in attribute text.
	/// Standard HTML tags are preserved as they may be intentional content.
	/// 
	/// Handles Pueblo-specific ANSI formats:
	/// - CSI sequences: ESC[...m (colors, styles) - e.g., ESC[31m (red), ESC[1m (bold), ESC[38;5;n]m (256-color)
	/// - OSC sequences: ESC]...ESC\ (operating system commands, used by Pueblo for special markup and hyperlinks)
	/// - Simple escapes: ESC followed by single character (stripped if not recognized)
	/// 
	/// Converted to MarkupStrings:
	/// - ANSI SGR (Select Graphic Rendition) codes → MarkupString colors/styles
	/// - ANSI 256-color codes (ESC[38;5;nm, ESC[48;5;nm) → MarkupString RGB colors
	/// - ANSI RGB codes (ESC[38;2;r;g;bm, ESC[48;2;r;g;bm) → MarkupString RGB colors
	/// - Bold (ESC[1m), underline (ESC[4m), etc. → MarkupString formatting
	/// - Pueblo OSC 8 sequences (hyperlinks) → MarkupString hyperlinks
	/// 
	/// Unrecognized escape sequences are stripped from the output.
	/// </summary>
	/// <param name="text">Text potentially containing Pueblo ANSI escape sequences</param>
	/// <returns>Text with escape sequences removed but standard HTML preserved, or empty string if input is null</returns>
	private static string StripPuebloEscapes(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return string.Empty;
		}

		// For now, just return the text as-is
		// TODO: Implement proper Pueblo escape stripping
		return text;
	}

	/// <summary>
	/// The stored password an imported player gets: the source value exactly as the source stored it,
	/// or <see cref="PasswordService.LockedHash"/> when it has none. <see cref="PasswordService"/>
	/// verifies every shape PennMUSH accepts, so the value is never hashed again here.
	/// </summary>
	private static string ImportedPassword(string? password)
		=> string.IsNullOrEmpty(password) ? PasswordService.LockedHash : password;

	/// <summary>
	/// The salt to pass alongside <see cref="ImportedPassword"/>. The providers keep a password verbatim
	/// only when a salt accompanies it; without one they hash it as plaintext, which would make the
	/// stored hash string itself the password. Nothing reads the salt back.
	/// </summary>
	private const string StoredVerbatim = "";

}
