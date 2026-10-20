using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>Package transactions for tests that drive the write log directly rather than through the installer.</summary>
internal static class TestPackageWrites
{
	/// <summary>Issued by the world's own factory, writing as <paramref name="manager"/>, with no retention of its own.</summary>
	public static PackageWriteTransaction Issue(IServiceProvider services, SharpPlayer manager, TimeProvider? recoveryClock = null)
		=> ((PackageWriteTransactionFactory)services.GetRequiredService<IPackageWriteTransactionFactory>())
			.Issue(manager, NoRetention.Instance, recoveryClock);

	/// <summary>Issued by the world's own factory, reporting to and released through <paramref name="hold"/>.</summary>
	public static PackageWriteTransaction Issue(IServiceProvider services, SharpPlayer manager, IPackageWriteHold hold)
		=> ((PackageWriteTransactionFactory)services.GetRequiredService<IPackageWriteTransactionFactory>()).Issue(manager, hold);

	/// <summary>Records the verdict a transaction reports and when it releases its hold.</summary>
	public sealed class RecordingHold : IPackageWriteHold
	{
		public List<PackageWriteVerdict> Verdicts { get; } = [];
		public bool Released { get; private set; }
		public void Report(PackageWriteVerdict verdict) { lock (Verdicts) Verdicts.Add(verdict); }
		public ValueTask DisposeAsync() { Released = true; return ValueTask.CompletedTask; }
	}

	/// <summary>Issued by a factory composed over <paramref name="services"/> with a replacement application registry.</summary>
	public static PackageWriteTransaction Compose(IServiceProvider services, IApplicationRegistryService applications, SharpPlayer manager)
		=> Factory(services, applications).Issue(manager, NoRetention.Instance);

	/// <summary>A factory over <paramref name="services"/>' stores and an explicit application registry, unretained.</summary>
	public static PackageWriteTransactionFactory Factory(IServiceProvider services, IApplicationRegistryService applications)
	{
		var database = services.GetRequiredService<ISharpDatabase>();
		return new PackageWriteTransactionFactory(database, PackageWriteTransactionFactory.Unretained,
			services.GetRequiredService<IMediator>(), database, database, (IPackageRegistryService)database, applications,
			services.GetRequiredService<ILockService>(), () => Helpers(services));
	}

	/// <summary>
	/// A factory over substitutes, for installer tests that never reach a real write. The substitute
	/// <paramref name="objects"/> answers the Package Manager lookup with a player.
	/// </summary>
	public static PackageWriteTransactionFactory Factory(IObjectStore objects, IAttributeStore attributes,
		IFlagAndPowerStore flags, IPackageRegistryService registry, IApplicationRegistryService applications,
		ILockService locks, IMediator mediator)
	{
		objects.GetObjectNodeAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>())
			.Returns(new AnyOptionalSharpObject(new TestObjectFactory().CreatePlayer(7, "Package Manager")));
		return new(objects, PackageWriteTransactionFactory.Unretained, mediator, attributes, flags, registry, applications, locks,
			() => throw new InvalidOperationException("This test world has no package helpers."));
	}

	private static PackageWriteHelpers Helpers(IServiceProvider services) => new(
		services.GetRequiredService<IWikiService>(), services.GetRequiredService<IWikiLocalizationService>(),
		services.GetRequiredService<IRoleRegistryService>(), services.GetRequiredService<IAccountClaimsInvalidator>(),
		services.GetRequiredService<IAttributeIndexBuilder>(), services.GetRequiredService<IConfigOptionWriter>());

	private sealed class NoRetention : IPackageWriteHold
	{
		public static readonly NoRetention Instance = new();
		public void Report(PackageWriteVerdict verdict) { }
		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
