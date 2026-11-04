using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>One kind's dictionaries and training runs. See <see cref="ICompressionStore.GetDictionariesAsync"/>.</summary>
public record GetDictionariesQuery(CompressedValues Values) : IQuery<StoredDictionaries>;
