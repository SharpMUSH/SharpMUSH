using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// What <c>@lock/&lt;switch&gt;</c> writes, <see cref="LockService.GetIfSet"/> must be able to read.
/// </summary>
/// <remarks>
/// The two sides name the same lock differently: <c>@lock</c> stores the canonical
/// <c>ILockService.SystemLocks</c> key (PennMUSH's own name, <c>src/lock.c:56-88</c>) while
/// <see cref="LockService.GetIfSet"/> asks for <c>LockType.ToString()</c>. Case is bridged by
/// <see cref="SharpObject.LockNameComparer"/>, as PennMUSH bridges it with <c>strcasecmp</c>
/// (<c>src/lock.c:364</c>); anything more than case has to be fixed in the enum member's name.
/// Run over every member so a newly-added one cannot quietly become unreadable.
/// </remarks>
public class StandardLockLookupTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ILockService Locks => WebAppFactoryArg.Services.GetRequiredService<ILockService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;

	public static IEnumerable<LockType> EveryStandardLock() => Enum.GetValues<LockType>();

	[Test]
	[MethodDataSource(nameof(EveryStandardLock))]
	public async Task ALockSetByItsSwitchIsFoundByItsLockType(LockType lockType)
	{
		var created = await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@create LockLookup{lockType}"));
		var target = DBRef.Parse(created.Message!.ToPlainText().Trim());

		await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@lock/{lockType} #{target.Number}=#TRUE"));

		var reread = (await Mediator.Send(new GetObjectNodeQuery(target))).Expect<AnySharpObject>();

		await Assert.That(LockService.GetIfSet(lockType, reread))
			.IsNotNull()
			.Because($"@lock/{lockType} stored a key that LockType.{lockType} cannot find");
	}

	/// <summary>
	/// The cheap half of the same invariant, without a database: every member of
	/// <see cref="LockType"/> names a lock the <c>@lock</c> switch table knows.
	/// </summary>
	[Test]
	[MethodDataSource(nameof(EveryStandardLock))]
	public async Task EveryLockTypeNamesASystemLock(LockType lockType)
		=> await Assert.That(Locks.SystemLocks.Keys
				.Any(key => SharpObject.LockNameComparer.Equals(key, lockType.ToString())))
			.IsTrue()
			.Because($"no @lock switch stores a key LockType.{lockType} would read back");
}
