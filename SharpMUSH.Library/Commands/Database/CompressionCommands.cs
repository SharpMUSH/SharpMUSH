using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

// A stored value reads the same whichever dictionary made it, so nothing cached changes and nothing is invalidated.

/// <summary>Compresses one kind of value. See <see cref="ICompressionStore.CompactAsync"/>.</summary>
public record CompactValuesCommand(CompressedValues Values, DateTimeOffset At) : ICommand<DictionaryCompaction>;

/// <summary>Switches one kind of value to another dictionary. See <see cref="ICompressionStore.UseDictionaryAsync"/>.</summary>
public record UseDictionaryCommand(CompressedValues Values, int Id) : ICommand<Found<long>>;
