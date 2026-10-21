using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>Registers the one transaction origin owned by this service provider.</summary>
public static class PackageWriteTransactionRegistration
{
	public static IServiceCollection AddPackageWriteTransactions<TProvider>(this IServiceCollection services)
		where TProvider : class, IObjectStore, IPackageWriteTransactionLifetime
	{
		services.AddSingleton<IPackageWriteTransactionFactory>(sp =>
		{
			var provider = sp.GetRequiredService<TProvider>();
			if (!ReferenceEquals(provider, sp.GetRequiredService<IObjectStore>()))
				throw new InvalidOperationException("Package writes require the registered world's object store.");
			return new PackageWriteTransactionFactory(provider, provider, sp.GetRequiredService<IMediator>(),
				sp.GetRequiredService<IAttributeStore>(), sp.GetRequiredService<IFlagAndPowerStore>(),
				sp.GetRequiredService<IPackageRegistryService>(), sp.GetRequiredService<IApplicationRegistryService>(),
				sp.GetRequiredService<ILockService>(),
				() => new PackageWriteHelpers(sp.GetRequiredService<IWikiService>(),
					sp.GetRequiredService<IWikiLocalizationService>(), sp.GetRequiredService<IRoleRegistryService>(),
					sp.GetRequiredService<IAccountClaimsInvalidator>(), sp.GetRequiredService<IAttributeIndexBuilder>(),
					sp.GetRequiredService<IConfigOptionWriter>()));
		});
		return services;
	}
}

/// <summary>The helper services one world's package writes may run through, as that world registered them.</summary>
internal sealed record PackageWriteHelpers(
	IWikiService Wiki, IWikiLocalizationService Localization, IRoleRegistryService Roles,
	IAccountClaimsInvalidator Claims, IAttributeIndexBuilder IndexBuilder, IConfigOptionWriter Config);

/// <summary>
/// The only origin of package transactions. Every transaction it issues carries it, and the installer
/// and each helper compare their own dependencies with the ones captured here before they read or
/// write, so a service from another world, or a hand-composed mix, is refused before any callback.
/// </summary>
internal sealed class PackageWriteTransactionFactory(
	IObjectStore objects, IPackageWriteTransactionLifetime lifetime, IMediator mediator,
	IAttributeStore attributes, IFlagAndPowerStore flags, IPackageRegistryService registry,
	IApplicationRegistryService applications, ILockService locks, Func<PackageWriteHelpers> helpers)
	: IPackageWriteTransactionFactory
{
	private readonly Lazy<PackageWriteHelpers> _helpers = new(helpers);

	/// <summary>A provider with no package-operation lifetime: its writes need no retention.</summary>
	internal static readonly IPackageWriteTransactionLifetime Unretained = new CompletedLifetime();

	public async ValueTask<PackageWriteTransaction> CreateAsync(DBRef packageManager, CancellationToken cancellationToken = default)
	{
		using var request = ExecutionBudget.EnterLinked(cancellationToken);
		ExecutionBudget.Current?.ThrowIfExceeded();
		// Retain before manager lookup too: closing an issued world cannot invalidate that read.
		var retained = await lifetime.BeginPackageWritesAsync(ExecutionBudget.CurrentToken);
		try
		{
			ExecutionBudget.Current?.ThrowIfExceeded();
			var manager = await objects.GetObjectNodeAsync(packageManager, ExecutionBudget.CurrentToken);
			ExecutionBudget.Current?.ThrowIfExceeded();
			return manager switch
			{
				AnySharpObject and SharpPlayer player => Issue(player, retained),
				AnySharpObject => throw new InvalidOperationException($"#{packageManager.Number} is not a player."),
				None => throw new InvalidOperationException($"Package Manager wizard #{packageManager.Number} does not exist.")
			};
		}
		catch
		{
			await retained.DisposeAsync();
			throw;
		}
	}

	/// <summary>A transaction writing as <paramref name="manager"/> that releases <paramref name="retained"/> when it closes.</summary>
	internal PackageWriteTransaction Issue(SharpPlayer manager, IPackageWriteHold retained, TimeProvider? recoveryClock = null)
		=> new(this, mediator, objects, attributes, flags, registry, applications, locks, manager, retained)
		{
			RecoveryClock = recoveryClock ?? TimeProvider.System
		};

	internal void RequireInstaller(IObjectStore installerObjects, IAttributeStore installerAttributes,
		IFlagAndPowerStore installerFlags, IPackageRegistryService installerRegistry,
		IApplicationRegistryService installerApplications, ILockService installerLocks, IMediator installerMediator)
		=> RequireSameWorld(ReferenceEquals(objects, installerObjects) && ReferenceEquals(attributes, installerAttributes)
			&& ReferenceEquals(flags, installerFlags) && ReferenceEquals(registry, installerRegistry)
			&& ReferenceEquals(applications, installerApplications) && ReferenceEquals(locks, installerLocks)
			&& ReferenceEquals(mediator, installerMediator));

	internal void RequireWiki(IWikiService wiki, IWikiLocalizationService localization,
		IPackageRegistryService packages, IRoleRegistryService roles)
		=> RequireSameWorld(ReferenceEquals(registry, packages) && _helpers.Value is var known
			&& ReferenceEquals(known.Wiki, wiki) && ReferenceEquals(known.Localization, localization)
			&& ReferenceEquals(known.Roles, roles));

	internal void RequireDeclarations(IRoleRegistryService roles, IPackageRegistryService packages,
		IAccountClaimsInvalidator claims, IMediator declarationMediator, IAttributeIndexBuilder indexBuilder)
		=> RequireSameWorld(ReferenceEquals(registry, packages) && ReferenceEquals(mediator, declarationMediator)
			&& _helpers.Value is var known && ReferenceEquals(known.Roles, roles)
			&& ReferenceEquals(known.Claims, claims) && ReferenceEquals(known.IndexBuilder, indexBuilder));

	internal void RequireSettings(IConfigOptionWriter config, IPackageRegistryService packages)
		=> RequireSameWorld(ReferenceEquals(registry, packages) && ReferenceEquals(_helpers.Value.Config, config));

	private static void RequireSameWorld(bool same)
	{
		if (!same) throw new InvalidOperationException("A package service does not belong to the world its transaction writes.");
	}

	private sealed class CompletedLifetime : IPackageWriteTransactionLifetime, IPackageWriteHold
	{
		public ValueTask<IPackageWriteHold> BeginPackageWritesAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return ValueTask.FromResult<IPackageWriteHold>(this);
		}

		public void Report(PackageWriteVerdict verdict) { }

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
