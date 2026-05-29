using Microsoft.Extensions.Logging;
using SharpMUSH.Database.SurrealDB;
using SharpMUSH.Library;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;

namespace SharpMUSH.Database.LoraDB;

/// <summary>
/// LoraDB adapter used by SharpMUSH in embedded mode.
/// This currently reuses the embedded SurrealDB implementation for in-process execution.
/// </summary>
public sealed class LoraDatabase(
ILogger<SurrealDatabase> logger,
ISurrealDbClient db,
IPasswordService passwordService
) : SurrealDatabase(logger, db, passwordService);
