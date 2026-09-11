using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using System.Diagnostics;

namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// Converts PennMUSH database format to SharpMUSH objects.
/// </summary>
public class PennMUSHDatabaseConverter : IPennMUSHDatabaseConverter
{
	private readonly ISharpDatabase _database;
	private readonly PennMUSHDatabaseParser _parser;
	private readonly ILogger<PennMUSHDatabaseConverter> _logger;
	private readonly IAttributeService _attributeService;
	private readonly IMediator _mediator;

	public PennMUSHDatabaseConverter(
		ISharpDatabase database,
		PennMUSHDatabaseParser parser,
		IAttributeService attributeService,
		IMediator mediator,
		ILogger<PennMUSHDatabaseConverter> logger)
	{
		_database = database;
		_parser = parser;
		_attributeService = attributeService;
		_mediator = mediator;
		_logger = logger;
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
			ReportProgress("Creating objects", 0.0);

			var objectCounts = await CreateObjectsAsync(pennDatabase, context, cancellationToken);
			playersConverted = objectCounts.players;
			roomsConverted = objectCounts.rooms;
			thingsConverted = objectCounts.things;
			exitsConverted = objectCounts.exits;
			ReportProgress("Objects created", 0.25);

			await EstablishRelationshipsAsync(pennDatabase, context, cancellationToken);
			ReportProgress("Relationships established", 0.50);

			attributesConverted = await CreateAttributesAsync(pennDatabase, context, cancellationToken);
			ReportProgress("Attributes created", 0.75);

			locksConverted = await CreateLocksAsync(pennDatabase, context, cancellationToken);
			ReportProgress("Locks created", 1.0);

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
			var (salt, hash) = ExtractPennMUSHPasswordParts(pennObject.Password);
			await _mediator.Send(new SetPlayerPasswordCommand(seededPlayer, hash, salt), cancellationToken);
		}

		var (created, modified) = PennTimestamps(pennObject);
		if (created is null)
		{
			return new DBRef(number);
		}

		await _mediator.Send(new SetObjectTimestampsCommand(new DBRef(number), created.Value, modified),
			cancellationToken);
		_logger.LogDebug("Restamped reused object #{DBRef} with its PennMUSH creation time {Created}",
			number, created.Value);

		return new DBRef(number, created.Value);
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
		var existingPlayer1 = await _database.GetObjectNodeAsync(new DBRef(1), cancellationToken);
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
			var (godSalt, godHash) = ExtractPennMUSHPasswordParts(godPennObject.Password);
			var (godCreated, godModified) = PennTimestamps(godPennObject);
			tempGodDbRef = await _database.CreatePlayerAsync(
				godPennObject.Name,
				godHash,
				new DBRef(0), // Limbo room (will create or reuse next)
				new DBRef(0), // Home is also Limbo
				godPennObject.Pennies > 0 ? godPennObject.Pennies : 1000,
				godSalt,
				godCreated,
				godModified,
				cancellationToken);

			dbrefMapping[1] = tempGodDbRef;
			playersConverted++;
			_logger.LogInformation("Created God player #{PennDBRef} -> {SharpDBRef}: {Name}", 1, tempGodDbRef, godPennObject.Name);
		}
		else
		{
			// Create a default God player (no salt needed for new password)
			tempGodDbRef = await _database.CreatePlayerAsync(
				"God",
				"NEEDS_RESET",
				new DBRef(0),
				new DBRef(0),
				10000,
				null,
				cancellationToken: cancellationToken);
			if (godPennObject is null)
			{
				dbrefMapping[1] = tempGodDbRef;
			}

			_logger.LogWarning("Created default God player as #{PennDBRef} was not a player", 1);
		}

		var godPlayerObj = await _database.GetObjectNodeAsync(tempGodDbRef, cancellationToken);
		if (godPlayerObj is not (AnySharpObject and SharpPlayer godPlayerWrapped))
		{
			throw new InvalidOperationException("Failed to retrieve God player after creation or reuse");
		}
		var godPlayer = godPlayerWrapped;

		var room0Penn = pennDatabase.GetObject(0);
		var existingRoom0 = await _database.GetObjectNodeAsync(new DBRef(0), cancellationToken);
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
			tempRoom0DbRef = await _database.CreateRoomAsync(
				room0Penn.Name,
				godPlayer,
				room0Created,
				room0Modified,
				cancellationToken);
			dbrefMapping[0] = tempRoom0DbRef;
			roomsConverted++;
			_logger.LogInformation("Created Limbo room #{PennDBRef} -> {SharpDBRef}: {Name}", 0, tempRoom0DbRef, room0Penn.Name);
		}
		else
		{
			tempRoom0DbRef = await _database.CreateRoomAsync(
				"Limbo",
				godPlayer,
				cancellationToken: cancellationToken);
			if (room0Penn is null)
			{
				dbrefMapping[0] = tempRoom0DbRef;
			}

			_logger.LogWarning("Created default Limbo room as #{PennDBRef} was not a room", 0);
		}

		var room2Penn = pennDatabase.GetObject(2);
		var existingRoom2 = await _database.GetObjectNodeAsync(new DBRef(2), cancellationToken);
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
							// Create player with password from PennMUSH - extract salt
							// Players start in Limbo temporarily
							var (playerSalt, playerHash) = ExtractPennMUSHPasswordParts(pennObj.Password);
							newDbRef = await _database.CreatePlayerAsync(
								pennObj.Name,
								playerHash,
								tempRoom0DbRef, // Start in Limbo
								tempRoom0DbRef, // Home is Limbo for now
								pennObj.Pennies > 0 ? pennObj.Pennies : 100,
								playerSalt,
								created,
								modified,
								cancellationToken);
							playersConverted++;
							break;
						}

					case PennMUSHObjectType.Room:
						{
							// Rooms are created with God as owner initially
							newDbRef = await _database.CreateRoomAsync(
								pennObj.Name,
								godPlayer,
								created,
								modified,
								cancellationToken);
							roomsConverted++;
							break;
						}

					case PennMUSHObjectType.Thing:
						{
							// Things need location and home - use Limbo temporarily
							if (room0 == null)
							{
								var room0Obj = await _database.GetObjectNodeAsync(tempRoom0DbRef, cancellationToken);
								room0 = room0Obj is AnySharpObject and SharpRoom limbo
									? limbo
									: throw new InvalidOperationException("Failed to retrieve Limbo room");
							}

							newDbRef = await _database.CreateThingAsync(
								pennObj.Name,
								room0, // Start in Limbo
								godPlayer, // God owns it temporarily
								room0, // Home is Limbo for now
								created,
								modified,
								cancellationToken);
							thingsConverted++;
							break;
						}

					case PennMUSHObjectType.Exit:
						{
							// Exits need location - use Limbo temporarily
							if (room0 == null)
							{
								var room0Obj = await _database.GetObjectNodeAsync(tempRoom0DbRef, cancellationToken);
								room0 = room0Obj is AnySharpObject and SharpRoom limbo
									? limbo
									: throw new InvalidOperationException("Failed to retrieve Limbo room");
							}

							var aliases = ExtractAliases(pennObj.Name);
							newDbRef = await _database.CreateExitAsync(
								aliases.name,
								aliases.aliases,
								room0, // Start in Limbo
								godPlayer, // God owns it temporarily
								created,
								modified,
								cancellationToken);
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

		return (playersConverted, roomsConverted, thingsConverted, exitsConverted);
	}

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
				if (await _database.GetObjectNodeAsync(sharpDbRef, cancellationToken) is not AnySharpObject sharpObj)
				{
					warnings.Add($"Could not retrieve object #{sharpDbRef} for relationship setup");
					continue;
				}

				// Handle location for content objects (players, things, exits)
				if (pennObj.Type != PennMUSHObjectType.Room && pennObj.Location >= 0)
				{
					if (dbrefMapping.TryGetValue(pennObj.Location, out var locationDbRef))
					{
						var locationObj = await _database.GetObjectNodeAsync(locationDbRef, cancellationToken);
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
						var destObj = await _database.GetObjectNodeAsync(destDbRef, cancellationToken);
						var container = TryGetContainer(destObj);

						if (container != null && sharpObj is SharpExit exit)
						{
							await _database.LinkExitAsync(exit, container, cancellationToken);
							_logger.LogDebug("Linked exit #{PennDBRef} to destination #{DestDBRef}", pennObj.DBRef, pennObj.Link);
						}
					}
				}

				if (pennObj.Parent >= 0 && dbrefMapping.TryGetValue(pennObj.Parent, out var parentDbRef))
				{
					if (await _database.GetObjectNodeAsync(parentDbRef, cancellationToken) is AnySharpObject parentObj)
					{
						await _database.SetObjectParent(sharpObj, parentObj, cancellationToken);
						_logger.LogDebug("Set parent for #{PennDBRef} to #{ParentDBRef}", pennObj.DBRef, pennObj.Parent);
					}
					else
					{
						warnings.Add($"Parent object #{pennObj.Parent} not found for object #{pennObj.DBRef}");
					}
				}

				if (pennObj.Zone >= 0 && dbrefMapping.TryGetValue(pennObj.Zone, out var zoneDbRef))
				{
					if (await _database.GetObjectNodeAsync(zoneDbRef, cancellationToken) is AnySharpObject zoneObj)
					{
						await _database.SetObjectZone(sharpObj, zoneObj, cancellationToken);
						_logger.LogDebug("Set zone for #{PennDBRef} to #{ZoneDBRef}", pennObj.DBRef, pennObj.Zone);
					}
					else
					{
						warnings.Add($"Zone object #{pennObj.Zone} not found for object #{pennObj.DBRef}");
					}
				}
			}
			catch (Exception ex)
			{
				var error = $"Failed to establish relationships for object #{pennObj.DBRef}: {ex.Message}";
				_logger.LogError(ex, "Relationship error for object #{DBRef}", pennObj.DBRef);
				errors.Add(error);
			}
		}
	}

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
				if (await _database.GetObjectNodeAsync(sharpDbRef, cancellationToken) is not AnySharpObject sharpObj)
				{
					warnings.Add($"Could not retrieve object #{sharpDbRef} for attribute creation");
					continue;
				}

				foreach (var pennAttr in pennObj.Attributes)
				{
					try
					{
						var value = MarkupString.Ansi.AnsiEscapeParser.Parse(pennAttr.Value);

						if (pennAttr.Value != null && pennAttr.Value.Contains('\x1b'))
						{
							_logger.LogTrace("Converted ANSI escape sequences from attribute {AttrName} on object #{DBRef}",
								pennAttr.Name, pennObj.DBRef);
						}

						var result = await _attributeService.SetAttributeAsync(
							sharpObj, // executor (system)
							sharpObj, // object to set attribute on
							pennAttr.Name,
							value);

						if (result is Error<string> setError)
						{
							warnings.Add($"Failed to set attribute {pennAttr.Name} on #{pennObj.DBRef}: {setError.Value}");
						}
						else
						{
							count++;
							_logger.LogTrace("Set attribute {AttrName} on object #{DBRef}", pennAttr.Name, pennObj.DBRef);

							if (pennAttr.Flags.Count > 0)
							{
								// One batch, not one call per flag (Task 6 fix round 1, M3):
								// applying imported flags one at a time re-checks permission
								// after each mutation, so e.g. a converted attribute carrying
								// both safe and wizard would silently lose wizard once safe
								// landed first (the importing executor is the object itself,
								// never God). Penn's own converter has no such per-flag gate.
								// The batch is all-or-nothing (Penn's string_to_atrflagsets fails the
								// whole argument on one unrecognized name), so a single unsupported
								// imported flag silently leaves the attribute with NO flags at all.
								// Surface it rather than reporting a clean conversion.
								var flagResult = await _attributeService.SetAttributeFlagsAsync(sharpObj, sharpObj,
									pennAttr.Name, pennAttr.Flags);

								if (flagResult is Error<string> flagError)
								{
									warnings.Add(
										$"Failed to set flags [{string.Join(", ", pennAttr.Flags)}] on attribute " +
										$"{pennAttr.Name} of #{pennObj.DBRef}: {flagError.Value}");
								}
							}
						}
					}
					catch (Exception ex)
					{
						warnings.Add($"Failed to set attribute {pennAttr.Name} on #{pennObj.DBRef}: {ex.Message}");
						_logger.LogDebug(ex, "Attribute creation error");
					}
				}
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
				if (await _database.GetObjectNodeAsync(sharpDbRef, cancellationToken) is not AnySharpObject sharpObj)
				{
					warnings.Add($"Could not retrieve object #{sharpDbRef} for lock creation");
					continue;
				}

				foreach (var (lockName, lockString) in pennObj.Locks)
				{
					try
					{
						var lockData = new Models.SharpLockData { LockString = lockString, Flags = Services.LockService.LockFlags.Default };
						await _database.SetLockAsync(
							sharpObj.Object(),
							lockName,
							lockData,
							cancellationToken);

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

	private static (string name, string[] aliases) ExtractAliases(string nameString)
	{
		// PennMUSH exit names can be like "north;n;out;o"
		var parts = nameString.Split(';', StringSplitOptions.RemoveEmptyEntries);
		var name = parts.Length > 0 ? parts[0] : nameString;
		var aliases = parts.Length > 1 ? parts[1..] : [];
		return (name, aliases);
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
	/// Extracts the salt and hash from a PennMUSH password format.
	/// PennMUSH format: V:ALGO:SALTEDHASH:TIMESTAMP
	/// The first 2 characters of SALTEDHASH are the salt.
	/// </summary>
	/// <param name="password">The PennMUSH password string</param>
	/// <returns>A tuple of (salt, hash) if valid PennMUSH format, or (null, password) if not</returns>
	private static (string? salt, string hash) ExtractPennMUSHPasswordParts(string? password)
	{
		if (string.IsNullOrEmpty(password))
			return (null, password ?? "NEEDS_RESET");

		var parts = password.Split(':');
		if (parts.Length < 3)
			return (null, password);

		// Check if first part is a version number (1 or 2)
		if (!int.TryParse(parts[0], out var version) || version < 1 || version > 2)
			return (null, password);

		// Check if second part is a known algorithm
		var algo = parts[1].ToUpperInvariant();
		if (algo is not ("SHA1" or "SHA-1" or "SHA256" or "SHA-256"))
			return (null, password);

		var saltedHash = parts[2];
		if (saltedHash.Length < 3)
			return (null, password);

		var salt = saltedHash[..2];

		// Return the salt and the full password (we keep the full format for verification)
		return (salt, password);
	}

}
