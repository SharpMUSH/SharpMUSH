using Asp.Versioning;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Mcp;
using SharpMUSH.Server.Middleware;
using SharpMUSH.Server.RateLimiting;
using SharpMUSH.Server.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using System.Globalization;
using System.Threading.RateLimiting;
using OpenTelemetry.ResourceDetectors.Container;
using Quartz;
using Serilog;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Commands;
using SharpMUSH.Implementation.Functions;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using SharpMUSH.Messaging.NATS;
using Microsoft.Extensions.Caching.Memory;
using ZiggyCreatures.Caching.Fusion;
using TaskScheduler = SharpMUSH.Library.Services.TaskScheduler;
namespace SharpMUSH.Server.Registration;

/// <summary>
/// The background services the host starts. Order matters in one place and is commented where it
/// does: plugins load before softcode packages and startup attributes run.
/// </summary>
internal static class HostedServiceRegistration
{
	/// <summary>Bootstrap, reconciliation, monitoring, scheduled work and the conversion worker.</summary>
	public static IServiceCollection AddSharpMushHostedServices(this IServiceCollection services)
	{
		services.AddQuartzHostedService();
		services.AddHostedService<StartupHandler>();
		// Load C# plugins before softcode packages/startup attributes run, so plugin commands/functions
		// are present in the libraries when later bootstrap stages execute.
		services.AddHostedService<Services.PluginBootstrapService>();
		services.AddHostedService<Services.DefaultPackagesBootstrapService>();
		services.AddHostedService<Services.DefaultApplicationsBootstrapService>();
		// Run @STARTUP on all objects at boot — registered after the other bootstrap services so
		// any objects/attributes they seed already exist. Re-establishes in-memory @function regs.
		services.AddHostedService<Services.StartupAttributeBootstrapService>();
		services.AddHostedService<NatsBridgeService>();
		services.AddHostedService<Services.ConnectionReconciliationService>();
		services.AddHostedService<Services.ConnectionLoggingService>();
		services.AddHostedService<Services.HealthMonitoringService>();
		services.AddHostedService<Services.ScheduledTaskManagementService>();
		services.AddHostedService<Services.WarningCheckService>();
		services.AddHostedService<Services.WorldBackupScheduleService>();
		services.AddHostedService<Services.RecurringJobRunner>();
		services.AddHostedService<Services.PennMUSHDatabaseConversionService>();

		// Configure OpenTelemetry Metrics with GKE/Kubernetes-aware resource detection

		return services;
	}
}
