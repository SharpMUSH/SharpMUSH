using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using SharpMUSH.Server;

namespace SharpMUSH.Tests.Server;

public class StartupEnvironmentRegistrationTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task PassedEnvironmentIsAvailableWithoutReplacingTheExistingHostEnvironment(bool hasHostEnvironment)
	{
		var passed = Substitute.For<IHostEnvironment>();
		passed.EnvironmentName.Returns(Environments.Production);
		passed.ApplicationName.Returns("SharpMUSH.Server");
		passed.ContentRootPath.Returns(AppContext.BaseDirectory);
		var host = Substitute.For<IHostEnvironment>();
		host.EnvironmentName.Returns(Environments.Production);
		var services = new ServiceCollection();
		if (hasHostEnvironment) services.AddSingleton<IHostEnvironment>(host);
		new Startup("colors.json", "nats://localhost:4222").ConfigureServices(services, new ConfigurationBuilder().Build(), passed);
		using var provider = services.BuildServiceProvider();
		await Assert.That(ReferenceEquals(provider.GetRequiredService<IHostEnvironment>(), hasHostEnvironment ? host : passed)).IsTrue();
		await Assert.That(services.Count(service => service.ServiceType == typeof(IHostEnvironment))).IsEqualTo(1);
	}
}
