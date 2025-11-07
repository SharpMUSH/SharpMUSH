using System.Collections.Immutable;
using DotNext.Threading;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Factory for creating fully initialized test objects with all required relationships
/// </summary>
public class TestObjectFactory
{
	private readonly Dictionary<int, SharpRoom> _rooms = new();
	private readonly Dictionary<int, AnySharpObject> _objects = new();
	
	/// <summary>
	/// Creates or retrieves a shared room for testing
	/// </summary>
	public SharpRoom CreateRoom(int key, string name)
	{
		if (_rooms.TryGetValue(key, out var existingRoom))
			return existingRoom;
			
		var room = new SharpRoom
		{
			Id = $"test-room-{key}",
			Object = new SharpObject
			{
				Key = key,
				Name = name,
				Type = "Room",
				Locks = ImmutableDictionary<string, string>.Empty,
				Owner = new(async ct => { await ValueTask.CompletedTask; return null!; }),
				Powers = new(() => AsyncEnumerable.Empty<SharpPower>()),
				Attributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
				LazyAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
				AllAttributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
				LazyAllAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
				Flags = new(() => AsyncEnumerable.Empty<SharpObjectFlag>()),
				Parent = new(async ct => { await ValueTask.CompletedTask; return new None(); }),
				Children = new(() => AsyncEnumerable.Empty<SharpObject>())
			},
			DropTo = new(async ct => { await ValueTask.CompletedTask; return new None(); })
		};
		
		_rooms[key] = room;
		return room;
	}
	
	/// <summary>
	/// Creates a player with all required properties and relationships
	/// </summary>
	public AnySharpObject CreatePlayer(int key, string name, SharpRoom? location = null)
	{
		if (_objects.TryGetValue(key, out var existingObject))
			return existingObject;
			
		// Use provided location or create a default one
		var playerLocation = location ?? CreateRoom(key + 10000, $"Room for {name}");
		
		var sharpObject = new SharpObject
		{
			Key = key,
			CreationTime = 0L, // Use 0 for test objects to make DBRef comparisons easier
			Name = name,
			Type = "Player",
			Locks = ImmutableDictionary<string, string>.Empty,
			Owner = new(async ct => 
			{
				await ValueTask.CompletedTask;
				// Players own themselves
				return _objects.TryGetValue(key, out var player) && player.IsPlayer 
					? player.AsPlayer 
					: null!;
			}),
			Powers = new(() => AsyncEnumerable.Empty<SharpPower>()),
			Attributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			AllAttributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAllAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			Flags = new(() => AsyncEnumerable.Empty<SharpObjectFlag>()),
			Parent = new(async ct => { await ValueTask.CompletedTask; return new None(); }),
			Children = new(() => AsyncEnumerable.Empty<SharpObject>())
		};
		
		var player = new SharpPlayer
		{
			Object = sharpObject,
			Aliases = Array.Empty<string>(),
			Location = new(async ct => { await ValueTask.CompletedTask; return playerLocation; }),
			Home = new(async ct => { await ValueTask.CompletedTask; return playerLocation; }),
			PasswordHash = string.Empty
		};
		
		var anySharpObject = new AnySharpObject(player);
		_objects[key] = anySharpObject;
		return anySharpObject;
	}
	
	/// <summary>
	/// Creates a thing with all required properties and relationships
	/// </summary>
	public AnySharpObject CreateThing(int key, string name, SharpRoom? location = null, AnySharpObject? owner = null)
	{
		if (_objects.TryGetValue(key, out var existingObject))
			return existingObject;
			
		// Use provided location or create a default one
		var thingLocation = location ?? CreateRoom(key + 10000, $"Room for {name}");
		
		var sharpObject = new SharpObject
		{
			Key = key,
			CreationTime = 0L, // Use 0 for test objects to make DBRef comparisons easier
			Name = name,
			Type = "Thing",
			Locks = ImmutableDictionary<string, string>.Empty,
			Owner = new(async ct => 
			{
				await ValueTask.CompletedTask;
				// Use provided owner or default to null
				return owner?.IsPlayer == true ? owner.AsPlayer : null!;
			}),
			Powers = new(() => AsyncEnumerable.Empty<SharpPower>()),
			Attributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			AllAttributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAllAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			Flags = new(() => AsyncEnumerable.Empty<SharpObjectFlag>()),
			Parent = new(async ct => { await ValueTask.CompletedTask; return new None(); }),
			Children = new(() => AsyncEnumerable.Empty<SharpObject>())
		};
		
		var thing = new SharpThing
		{
			Object = sharpObject,
			Aliases = Array.Empty<string>(),
			Location = new(async ct => { await ValueTask.CompletedTask; return thingLocation; }),
			Home = new(async ct => { await ValueTask.CompletedTask; return thingLocation; })
		};
		
		var anySharpObject = new AnySharpObject(thing);
		_objects[key] = anySharpObject;
		return anySharpObject;
	}
	
	/// <summary>
	/// Gets all rooms created by this factory
	/// </summary>
	public IEnumerable<SharpRoom> GetAllRooms() => _rooms.Values;
	
	/// <summary>
	/// Gets all objects created by this factory
	/// </summary>
	public IEnumerable<AnySharpObject> GetAllObjects() => _objects.Values;
	
	/// <summary>
	/// Clears all cached objects
	/// </summary>
	public void Clear()
	{
		_rooms.Clear();
		_objects.Clear();
	}
}
