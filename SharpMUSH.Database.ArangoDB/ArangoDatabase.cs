using Core.Arango;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase(
ILogger<ArangoDatabase> logger,
IArangoContext arangoDb,
ArangoHandle handle,
IMediator mediator,
IPasswordService passwordService
) : ISharpDatabase, ISharpDatabaseWithLogging
{
private const string StartVertex = "startVertex";
}
