using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

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
	private readonly IMoveService _moveService;
	
	// Mapping from PennMUSH DBRef to SharpMUSH DBRef
	private readonly Dictionary<int, DBRef> _dbrefMapping = [];

	// Compiled regex patterns for efficient escape sequence stripping
	// Raw ANSI escape sequences as stored in PennMUSH database files
	private static readonly Regex AnsiEscapePattern = new(@"\x1b\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);
	private static readonly Regex AnsiOscPattern = new(@"\x1b\][^\x1b]*(\x1b\\|\x07)", RegexOptions.Compiled);
	private static readonly Regex SimpleAnsiPattern = new(@"\x1b[A-Za-z]", RegexOptions.Compiled);

	public PennMUSHDatabaseConverter(
		ISharpDatabase database,
		PennMUSHDatabaseParser parser,
		IAttributeService attributeService,
		IMoveService moveService,
		ILogger<PennMUSHDatabaseConverter> logger)
	{
		_database = database;
		_parser = parser;
		_attributeService = attributeService;
		_moveService = moveService;
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
		var stopwatch = Stopwatch.StartNew();
		var result = new ConversionResult();
		var errors = new List<string>();
		var warnings = new List<string>();
		
		var totalObjects = pennDatabase.Objects.Count;
		var playersConverted = 0;
		var roomsConverted = 0;
		var thingsConverted = 0;
		var exitsConverted = 0;
		var attributesConverted = 0;
		var locksConverted = 0;

		_logger.LogInformation("Converting {Count} PennMUSH objects to SharpMUSH format", totalObjects);

		// Helper to report progress
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
			
			var objectCounts = await CreateObjectsAsync(pennDatabase, errors, warnings, cancellationToken);
			playersConverted = objectCounts.players;
			roomsConverted = objectCounts.rooms;
			thingsConverted = objectCounts.things;
			exitsConverted = objectCounts.exits;
			ReportProgress("Objects created", 0.25);

			await EstablishRelationshipsAsync(pennDatabase, errors, warnings, cancellationToken);
			ReportProgress("Relationships established", 0.50);

			attributesConverted = await CreateAttributesAsync(pennDatabase, errors, warnings, cancellationToken);
			ReportProgress("Attributes created", 0.75);

			locksConverted = await CreateLocksAsync(pennDatabase, errors, warnings, cancellationToken);
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

	private async Task<(int players, int rooms, int things, int exits)> CreateObjectsAsync(
		PennMUSHDatabase pennDatabase, 
		List<string> errors, 
		List<string> warnings,
		CancellationToken cancellationToken)
	{
		int playersConverted = 0, roomsConverted = 0, thingsConverted = 0, exitsConverted = 0;

		_logger.LogInformation("Object creation phase - converting {Count} objects", pennDatabase.Objects.Count);

		// If the database is empty, there's nothing to convert
		if (pennDatabase.Objects.Count == 0)
		{
			_logger.LogInformation("Empty database - no objects to convert");
			return (0, 0, 0, 0);
		}

		// Check if default objects from migration already exist (#0, #1, #2)
		// If they do, we'll reuse them instead of creating new ones
		DBRef tempGodDbRef;
		var existingPlayer1 = await _database.GetObjectNodeAsync(new DBRef(1), cancellationToken);
		
		if (existingPlayer1.IsT0)
		{
			// Player #1 already exists (from database migration), reuse it
			tempGodDbRef = new DBRef(1);
			_dbrefMapping[1] = tempGodDbRef;
			playersConverted++; // Count reused object in totals
			_logger.LogInformation("Reusing existing God player #1 from database migration");
			
			// Update the name and password from PennMUSH if available
			var godPennObject = pennDatabase.GetObject(1);
			if (godPennObject?.Type == PennMUSHObjectType.Player)
			{
				// Update God player name/password from PennMUSH data
				await _database.SetObjectName(existingPlayer1.AsT0, MModule.single(godPennObject.Name), cancellationToken);
				
				if (!string.IsNullOrEmpty(godPennObject.Password))
					{
						var (salt, hash) = ExtractPennMUSHPasswordParts(godPennObject.Password);
						await _database.SetPlayerPasswordAsync(existingPlayer1.AsT0, hash, salt, cancellationToken);
					}
				
				_logger.LogDebug("Updated God player #{PennDBRef} with name: {Name}", 1, godPennObject.Name);
			}
		}
		else
		{
			// No existing player, create one
			var godPennObject = pennDatabase.GetObject(1);
			
			if (godPennObject?.Type == PennMUSHObjectType.Player)
				{
					// Create God player first - extract salt from PennMUSH password
					var (godSalt, godHash) = ExtractPennMUSHPasswordParts(godPennObject.Password);
					tempGodDbRef = await _database.CreatePlayerAsync(
						godPennObject.Name,
						godHash,
						new DBRef(0), // Limbo room (will create or reuse next)
						new DBRef(0), // Home is also Limbo
						godPennObject.Pennies > 0 ? godPennObject.Pennies : 1000,
						godSalt,
						cancellationToken);
				
					_dbrefMapping[1] = tempGodDbRef;
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
						cancellationToken);
					_dbrefMapping[1] = tempGodDbRef;
					playersConverted++;
					_logger.LogWarning("Created default God player as #{PennDBRef} was not a player", 1);
				}
		}

		// Get the God player object for use as creator
		var godPlayerObj = await _database.GetObjectNodeAsync(tempGodDbRef, cancellationToken);
		if (!godPlayerObj.TryPickT0(out var godPlayerWrapped, out _))
		{
			throw new InvalidOperationException("Failed to retrieve God player after creation or reuse");
		}
		var godPlayer = godPlayerWrapped; // This is SharpPlayer directly

		// Check if Room #0 already exists (from database migration)
		DBRef tempRoom0DbRef;
		var existingRoom0 = await _database.GetObjectNodeAsync(new DBRef(0), cancellationToken);
		
		if (existingRoom0.IsT0)
		{
			// Room #0 already exists (from database migration), reuse it
			tempRoom0DbRef = new DBRef(0);
			_dbrefMapping[0] = tempRoom0DbRef;
			roomsConverted++; // Count reused object in totals
			_logger.LogInformation("Reusing existing Limbo room #0 from database migration");
			
			// Update the name from PennMUSH if available
			var room0Penn = pennDatabase.GetObject(0);
			if (room0Penn?.Type == PennMUSHObjectType.Room)
			{
				// Update Room #0 name from PennMUSH data
				await _database.SetObjectName(existingRoom0.AsT1, MModule.single(room0Penn.Name), cancellationToken);
				_logger.LogDebug("Updated Limbo room #{PennDBRef} with name: {Name}", 0, room0Penn.Name);
			}
		}
		else
		{
			// No existing room, create one
			var room0Penn = pennDatabase.GetObject(0);
			
			if (room0Penn?.Type == PennMUSHObjectType.Room)
			{
				tempRoom0DbRef = await _database.CreateRoomAsync(
					room0Penn.Name,
					godPlayer,
					cancellationToken);
				_dbrefMapping[0] = tempRoom0DbRef;
				roomsConverted++;
				_logger.LogInformation("Created Limbo room #{PennDBRef} -> {SharpDBRef}: {Name}", 0, tempRoom0DbRef, room0Penn.Name);
			}
			else
			{
				tempRoom0DbRef = await _database.CreateRoomAsync(
					"Limbo",
					godPlayer,
					cancellationToken);
				_dbrefMapping[0] = tempRoom0DbRef;
				roomsConverted++;
				_logger.LogWarning("Created default Limbo room as #{PennDBRef} was not a room", 0);
			}
		}

		// Check if Master Room #2 already exists (from database migration)
		var existingRoom2 = await _database.GetObjectNodeAsync(new DBRef(2), cancellationToken);
		
		if (existingRoom2.IsT0)
		{
			// Master Room #2 already exists (from database migration), reuse it
			_dbrefMapping[2] = new DBRef(2);
			
			// Count reused object in appropriate counter based on PennMUSH type
			var room2Penn = pennDatabase.GetObject(2);
			if (room2Penn != null)
			{
				switch (room2Penn.Type)
				{
					case PennMUSHObjectType.Room:
						roomsConverted++;
						break;
					case PennMUSHObjectType.Thing:
						thingsConverted++;
						break;
					case PennMUSHObjectType.Exit:
						exitsConverted++;
						break;
					case PennMUSHObjectType.Player:
						playersConverted++;
						break;
				}
				_logger.LogInformation("Reusing existing object #2 from database migration as {Type}", room2Penn.Type);
			}
			else
			{
				// Object #2 doesn't exist in PennMUSH database, but exists in Sharp
				roomsConverted++; // Assume it's a room from migration
				_logger.LogInformation("Reusing existing Master Room #2 from database migration (not in PennMUSH database)");
			}
		}
		else
		{
			// No existing #2 in Sharp database
			// It will be created in the main loop below based on its actual type in PennMUSH
			_logger.LogDebug("Object #2 does not pre-exist in Sharp database, will be created in main loop");
		}

		// Now create all other objects
		SharpRoom? room0 = null; // Cache the limbo room to avoid repeated lookups
		
		foreach (var pennObj in pennDatabase.Objects)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Skip already created or reused objects (God, Limbo, and potentially #2 if it was reused)
			if (pennObj.DBRef == 0 || pennObj.DBRef == 1 || _dbrefMapping.ContainsKey(pennObj.DBRef))
			{
				continue;
			}

			try
			{
				DBRef newDbRef;
				
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
							if (!room0Obj.TryPickT1(out room0, out _))
							{
								throw new InvalidOperationException("Failed to retrieve Limbo room");
							}
						}
						
						newDbRef = await _database.CreateThingAsync(
							pennObj.Name,
							room0, // Start in Limbo
							godPlayer, // God owns it temporarily
							room0, // Home is Limbo for now
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
							if (!room0Obj.TryPickT1(out room0, out _))
							{
								throw new InvalidOperationException("Failed to retrieve Limbo room");
							}
						}
						
						var aliases = ExtractAliases(pennObj.Name);
						newDbRef = await _database.CreateExitAsync(
							aliases.name,
							aliases.aliases,
							room0, // Start in Limbo
							godPlayer, // God owns it temporarily
							cancellationToken);
						exitsConverted++;
						break;
					}

					default:
						warnings.Add($"Unknown object type for #{pennObj.DBRef}: {pennObj.Type}");
						continue;
				}

				// Store mapping from PennMUSH DBRef to SharpMUSH DBRef
				_dbrefMapping[pennObj.DBRef] = newDbRef;

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
		List<string> errors,
		List<string> warnings,
		CancellationToken cancellationToken)
	{
		_logger.LogInformation("Establishing object relationships for {Count} objects", pennDatabase.Objects.Count);

		foreach (var pennObj in pennDatabase.Objects)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Skip if we don't have a mapping for this object
			if (!_dbrefMapping.TryGetValue(pennObj.DBRef, out var sharpDbRef))
			{
				continue;
			}

			try
			{
				// Get the Sharp object
				var sharpObjResult = await _database.GetObjectNodeAsync(sharpDbRef, cancellationToken);
				if (sharpObjResult.IsNone)
				{
					warnings.Add($"Could not retrieve object #{sharpDbRef} for relationship setup");
					continue;
				}

				var sharpObj = sharpObjResult;

				// Handle location for content objects (players, things, exits)
				if (pennObj.Type != PennMUSHObjectType.Room && pennObj.Location >= 0)
				{
					if (_dbrefMapping.TryGetValue(pennObj.Location, out var locationDbRef))
					{
						var locationObj = await _database.GetObjectNodeAsync(locationDbRef, cancellationToken);
						var container = TryGetContainer(locationObj);

						if (container != null)
						{
							// Convert object to content (player, exit, or thing)
							var hasContent = sharpObj.Match(
								player => true,
								room => false, // Rooms aren't content
								exit => true,
								thing => true,
								_ => false);

							if (hasContent)
							{
								var content = sharpObj.Match<AnySharpContent>(
									player => player,
									room => throw new InvalidOperationException("Room cannot be content"),
									exit => exit,
									thing => thing,
									_ => throw new InvalidOperationException("None cannot be content"));

								// Use MoveService to properly move the object
								// Note: Passing null for parser as this is a system operation during conversion
								var moveResult = await _moveService.ExecuteMoveAsync(
									null!, // No parser context during conversion
									content,
									container,
									null, // System move
									"conversion",
									silent: true);

								if (moveResult.IsT1)
								{
									var errorMsg = $"Failed to move object #{pennObj.DBRef} to location #{pennObj.Location}: {moveResult.AsT1.Value}";
									warnings.Add(errorMsg);
									_logger.LogDebug("Move error during conversion: {Error}", errorMsg);
								}
							}
						}
					}
				}

				// Handle exit destination (link)
				if (pennObj.Type == PennMUSHObjectType.Exit && pennObj.Link >= 0)
				{
					if (_dbrefMapping.TryGetValue(pennObj.Link, out var destDbRef))
					{
						var destObj = await _database.GetObjectNodeAsync(destDbRef, cancellationToken);
						var container = TryGetContainer(destObj);

						if (container != null && sharpObj.IsExit)
						{
							await _database.LinkExitAsync(sharpObj.AsExit, container, cancellationToken);
							_logger.LogDebug("Linked exit #{PennDBRef} to destination #{DestDBRef}", pennObj.DBRef, pennObj.Link);
						}
					}
				}

				// Handle parent relationship
				if (pennObj.Parent >= 0 && _dbrefMapping.TryGetValue(pennObj.Parent, out var parentDbRef))
				{
					// Set parent relationship
					var parentObj = await _database.GetObjectNodeAsync(parentDbRef, cancellationToken);
					if (!parentObj.IsNone)
					{
						await _database.SetObjectParent(sharpObj.Known, parentObj.Known, cancellationToken);
						_logger.LogDebug("Set parent for #{PennDBRef} to #{ParentDBRef}", pennObj.DBRef, pennObj.Parent);
					}
					else
					{
						warnings.Add($"Parent object #{pennObj.Parent} not found for object #{pennObj.DBRef}");
					}
				}

				// Handle zone relationship
				if (pennObj.Zone >= 0 && _dbrefMapping.TryGetValue(pennObj.Zone, out var zoneDbRef))
				{
					// Set zone relationship
					var zoneObj = await _database.GetObjectNodeAsync(zoneDbRef, cancellationToken);
					if (!zoneObj.IsNone)
					{
						await _database.SetObjectZone(sharpObj.Known, zoneObj.Known, cancellationToken);
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
		List<string> errors,
		List<string> warnings,
		CancellationToken cancellationToken)
	{
		_logger.LogInformation("Creating attributes");
		var count = 0;

		foreach (var pennObj in pennDatabase.Objects)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Skip if we don't have a mapping for this object
			if (!_dbrefMapping.TryGetValue(pennObj.DBRef, out var sharpDbRef))
			{
				continue;
			}

			// Skip if no attributes
			if (pennObj.Attributes.Count == 0)
			{
				continue;
			}

			try
			{
				// Get the Sharp object
				var sharpObjResult = await _database.GetObjectNodeAsync(sharpDbRef, cancellationToken);
				if (sharpObjResult.IsNone)
				{
					warnings.Add($"Could not retrieve object #{sharpDbRef} for attribute creation");
					continue;
				}

				var sharpObj = sharpObjResult.Known;

				// Create each attribute
				foreach (var pennAttr in pennObj.Attributes)
				{
					try
					{
						// Convert ANSI escape sequences to MarkupString
						var value = AnsiEscapeParser.ConvertAnsiToMarkupString(pennAttr.Value);
						
						// Log if escape sequences were converted
						if (pennAttr.Value != null && pennAttr.Value.Contains('\x1b'))
						{
							_logger.LogTrace("Converted ANSI escape sequences from attribute {AttrName} on object #{DBRef}", 
								pennAttr.Name, pennObj.DBRef);
						}

						// Set the attribute using AttributeService
						var result = await _attributeService.SetAttributeAsync(
							sharpObj, // executor (system)
							sharpObj, // object to set attribute on
							pennAttr.Name,
							value);

						if (result.IsT0)
						{
							count++;
							_logger.LogTrace("Set attribute {AttrName} on object #{DBRef}", pennAttr.Name, pennObj.DBRef);

							// Set attribute flags if any
							foreach (var flag in pennAttr.Flags)
							{
								await _attributeService.SetAttributeFlagAsync(sharpObj, sharpObj, pennAttr.Name, flag);
							}
						}
						else
						{
							warnings.Add($"Failed to set attribute {pennAttr.Name} on #{pennObj.DBRef}: {result.AsT1.Value}");
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
		List<string> errors,
		List<string> warnings,
		CancellationToken cancellationToken)
	{
		_logger.LogInformation("Creating locks");
		var count = 0;

		foreach (var pennObj in pennDatabase.Objects)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Skip if we don't have a mapping for this object
			if (!_dbrefMapping.TryGetValue(pennObj.DBRef, out var sharpDbRef))
			{
				continue;
			}

			// Skip if no locks
			if (pennObj.Locks.Count == 0)
			{
				continue;
			}

			try
			{
				// Get the Sharp object
				var sharpObjResult = await _database.GetObjectNodeAsync(sharpDbRef, cancellationToken);
				if (sharpObjResult.IsNone)
				{
					warnings.Add($"Could not retrieve object #{sharpDbRef} for lock creation");
					continue;
				}

				var sharpObj = sharpObjResult.Known;

				// Create each lock
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
		if (obj.IsNone || obj.IsExit)
		{
			return null;
		}

		return obj.Match<AnySharpContainer>(
			player => player,
			room => room,
			exit => throw new InvalidOperationException("Exit cannot be container"),
			thing => thing,
			_ => throw new InvalidOperationException("None cannot be container"));
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

		// Extract the 2-character salt and the remaining hash
		var salt = saltedHash[..2];
		var hash = saltedHash[2..];

		// Return the salt and the full password (we keep the full format for verification)
		return (salt, password);
	}

	[Obsolete("Use AnsiEscapeParser.ConvertAnsiToMarkupString instead")]
	private static string StripEscapeSequences(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return string.Empty;
		}

		// Strip CSI (Control Sequence Introducer) sequences: ESC[ ... letter
		// This handles most ANSI codes including:
		// - Colors: ESC[31m (red), ESC[32m (green), etc.
		// - 256-color: ESC[38;5;nm (foreground), ESC[48;5;nm (background)
		// - RGB color: ESC[38;2;r;g;bm (foreground), ESC[48;2;r;g;bm (background)
		// - Styles: ESC[1m (bold), ESC[4m (underline), ESC[7m (inverse)
		// - Reset: ESC[0m (reset all attributes)
		text = AnsiEscapePattern.Replace(text, string.Empty);

		// Strip OSC (Operating System Command) sequences: ESC] ... ESC\ or ESC] ... BEL
		// These are used by Pueblo for special markup and hyperlinks
		text = AnsiOscPattern.Replace(text, string.Empty);

		// Strip other ANSI escape sequences (ESC followed by a single character)
		// This handles simpler escape codes
		text = SimpleAnsiPattern.Replace(text, string.Empty);

		// Strip any remaining ESC characters that weren't part of sequences
		text = text.Replace("\x1b", string.Empty);

		return text;
	}
}
