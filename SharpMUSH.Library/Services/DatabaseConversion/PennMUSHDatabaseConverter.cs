using System.Diagnostics;
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

	public async Task<ConversionResult> ConvertDatabaseAsync(PennMUSHDatabase pennDatabase, CancellationToken cancellationToken = default)
	{
		var stopwatch = Stopwatch.StartNew();
		var result = new ConversionResult();
		var errors = new List<string>();
		var warnings = new List<string>();

		_logger.LogInformation("Converting {Count} PennMUSH objects to SharpMUSH format", pennDatabase.Objects.Count);

		try
		{
			// First pass: Create all objects without relationships
			var (playersConverted, roomsConverted, thingsConverted, exitsConverted) = 
				await CreateObjectsAsync(pennDatabase, errors, warnings, cancellationToken);

			// Second pass: Set up relationships (location, contents, exits, etc.)
			await EstablishRelationshipsAsync(pennDatabase, errors, warnings, cancellationToken);

			// Third pass: Create attributes
			var attributesConverted = await CreateAttributesAsync(pennDatabase, errors, warnings, cancellationToken);

			// Fourth pass: Set up locks
			var locksConverted = await CreateLocksAsync(pennDatabase, errors, warnings, cancellationToken);

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

		// First, we need to create a temporary player #1 (usually God) to own initial objects
		// Find player #1 (God) in the PennMUSH database
		var godPennObject = pennDatabase.GetObject(1);
		DBRef tempGodDbRef;
		
		if (godPennObject?.Type == PennMUSHObjectType.Player)
		{
			// Create God player first
			tempGodDbRef = await _database.CreatePlayerAsync(
				godPennObject.Name,
				godPennObject.Password ?? "NEEDS_RESET",
				new DBRef(0), // Limbo room (will create next)
				new DBRef(0), // Home is also Limbo
				godPennObject.Pennies > 0 ? godPennObject.Pennies : 1000,
				cancellationToken);
			
			_dbrefMapping[1] = tempGodDbRef;
			playersConverted++;
			_logger.LogInformation("Created God player #{PennDBRef} -> {SharpDBRef}: {Name}", 1, tempGodDbRef, godPennObject.Name);
		}
		else
		{
			// Create a default God player
			tempGodDbRef = await _database.CreatePlayerAsync(
				"God",
				"NEEDS_RESET",
				new DBRef(0),
				new DBRef(0),
				10000,
				cancellationToken);
			_dbrefMapping[1] = tempGodDbRef;
			playersConverted++;
			_logger.LogWarning("Created default God player as #{PennDBRef} was not a player", 1);
		}

		// Get the God player object for use as creator
		var godPlayerObj = await _database.GetObjectNodeAsync(tempGodDbRef, cancellationToken);
		if (!godPlayerObj.TryPickT0(out var godPlayerWrapped, out _))
		{
			throw new InvalidOperationException("Failed to retrieve God player after creation");
		}
		var godPlayer = godPlayerWrapped; // This is SharpPlayer directly

		// Create Room #0 (usually Limbo/Master Room)
		var room0Penn = pennDatabase.GetObject(0);
		DBRef tempRoom0DbRef;
		
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

		// Now create all other objects
		SharpRoom? room0 = null; // Cache the limbo room to avoid repeated lookups
		
		foreach (var pennObj in pennDatabase.Objects)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Skip already created objects (God and Limbo)
			if (pennObj.DBRef == 0 || pennObj.DBRef == 1)
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
						// Create player with password from PennMUSH
						// Players start in Limbo temporarily
						newDbRef = await _database.CreatePlayerAsync(
							pennObj.Name,
							pennObj.Password ?? "NEEDS_RESET",
							tempRoom0DbRef, // Start in Limbo
							tempRoom0DbRef, // Home is Limbo for now
							pennObj.Pennies > 0 ? pennObj.Pennies : 100,
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
					// TODO: Set parent relationship
					// This requires database support for setting parent
					_logger.LogDebug("Would set parent for #{PennDBRef} to #{ParentDBRef}", pennObj.DBRef, pennObj.Parent);
				}

				// Handle zone relationship
				if (pennObj.Zone >= 0 && _dbrefMapping.TryGetValue(pennObj.Zone, out var zoneDbRef))
				{
					// TODO: Set zone relationship
					// This requires database support for setting zone
					_logger.LogDebug("Would set zone for #{PennDBRef} to #{ZoneDBRef}", pennObj.DBRef, pennObj.Zone);
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
						// Convert PennMUSH attribute value to MString
						var value = MModule.single(pennAttr.Value);

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
						await _database.SetLockAsync(
							sharpObj.Object(),
							lockName,
							lockString,
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
}
