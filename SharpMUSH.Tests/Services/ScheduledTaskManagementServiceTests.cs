using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Quartz;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Services;

public class ScheduledTaskManagementServiceTests
{
	[Test]
	public async Task ParseTimeInterval_ValidHourInterval_ReturnsCorrectTimeSpan()
	{
		// Use reflection to access the private ParseTimeInterval method
		var method = typeof(ScheduledTaskManagementService).GetMethod("ParseTimeInterval",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
		
		var result = (TimeSpan)method!.Invoke(null, ["1h"])!;
		
		await Assert.That(result).IsEqualTo(TimeSpan.FromHours(1));
	}

	[Test]
	public async Task ParseTimeInterval_ValidMinuteInterval_ReturnsCorrectTimeSpan()
	{
		var method = typeof(ScheduledTaskManagementService).GetMethod("ParseTimeInterval",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
		
		var result = (TimeSpan)method!.Invoke(null, ["30m"])!;
		
		await Assert.That(result).IsEqualTo(TimeSpan.FromMinutes(30));
	}

	[Test]
	public async Task ParseTimeInterval_ValidDayInterval_ReturnsCorrectTimeSpan()
	{
		var method = typeof(ScheduledTaskManagementService).GetMethod("ParseTimeInterval",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
		
		var result = (TimeSpan)method!.Invoke(null, ["2d"])!;
		
		await Assert.That(result).IsEqualTo(TimeSpan.FromDays(2));
	}

	[Test]
	public async Task ParseTimeInterval_ComplexInterval_ReturnsCorrectTimeSpan()
	{
		var method = typeof(ScheduledTaskManagementService).GetMethod("ParseTimeInterval",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
		
		// "1h30m" = 1 hour + 30 minutes = 90 minutes
		var result = (TimeSpan)method!.Invoke(null, ["1h30m"])!;
		
		await Assert.That(result).IsEqualTo(TimeSpan.FromMinutes(90));
	}

	[Test]
	public async Task ParseTimeInterval_ZeroString_ReturnsZeroTimeSpan()
	{
		var method = typeof(ScheduledTaskManagementService).GetMethod("ParseTimeInterval",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
		
		var result = (TimeSpan)method!.Invoke(null, ["0"])!;
		
		await Assert.That(result).IsEqualTo(TimeSpan.Zero);
	}

	[Test]
	public async Task ParseTimeInterval_EmptyString_ReturnsZeroTimeSpan()
	{
		var method = typeof(ScheduledTaskManagementService).GetMethod("ParseTimeInterval",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
		
		var result = (TimeSpan)method!.Invoke(null, [""])!;
		
		await Assert.That(result).IsEqualTo(TimeSpan.Zero);
	}

	[Test]
	public async Task UpdateWarningTimeJob_UpdatesTimeWhenExpired()
	{
		// Arrange
		var dataService = Substitute.For<IExpandedObjectDataService>();
		var logger = Substitute.For<ILogger<ScheduledTaskManagementService.UpdateWarningTimeJob>>();
		var jobContext = Substitute.For<IJobExecutionContext>();
		var jobDetail = Substitute.For<IJobDetail>();
		var jobDataMap = new JobDataMap { { "interval", 3600.0 } }; // 1 hour

		var now = DateTimeOffset.UtcNow;
		var oldData = new UptimeData(
			StartTime: now.AddHours(-2),
			LastRebootTime: now.AddHours(-2),
			Reboots: 0,
			NextWarningTime: now.AddMinutes(-5), // Expired 5 minutes ago
			NextPurgeTime: now.AddHours(1)
		);

		dataService.GetExpandedServerDataAsync<UptimeData>().Returns(ValueTask.FromResult<UptimeData?>(oldData));
		jobContext.JobDetail.Returns(jobDetail);
		jobDetail.JobDataMap.Returns(jobDataMap);

		var job = new ScheduledTaskManagementService.UpdateWarningTimeJob(dataService, logger);

		// Act
		await job.Execute(jobContext);

		// Assert
		await dataService.Received(1).SetExpandedServerDataAsync(
			Arg.Is<UptimeData>(d => d.NextWarningTime > now && d.NextWarningTime <= now.AddHours(1.1))
		);
	}

	[Test]
	public async Task UpdateWarningTimeJob_DoesNotUpdateWhenNotExpired()
	{
		// Arrange
		var dataService = Substitute.For<IExpandedObjectDataService>();
		var logger = Substitute.For<ILogger<ScheduledTaskManagementService.UpdateWarningTimeJob>>();
		var jobContext = Substitute.For<IJobExecutionContext>();
		var jobDetail = Substitute.For<IJobDetail>();
		var jobDataMap = new JobDataMap { { "interval", 3600.0 } }; // 1 hour

		var now = DateTimeOffset.UtcNow;
		var oldData = new UptimeData(
			StartTime: now.AddHours(-2),
			LastRebootTime: now.AddHours(-2),
			Reboots: 0,
			NextWarningTime: now.AddHours(1), // Not expired yet
			NextPurgeTime: now.AddHours(1)
		);

		dataService.GetExpandedServerDataAsync<UptimeData>().Returns(ValueTask.FromResult<UptimeData?>(oldData));
		jobContext.JobDetail.Returns(jobDetail);
		jobDetail.JobDataMap.Returns(jobDataMap);

		var job = new ScheduledTaskManagementService.UpdateWarningTimeJob(dataService, logger);

		// Act
		await job.Execute(jobContext);

		// Assert
		await dataService.DidNotReceive().SetExpandedServerDataAsync(Arg.Any<UptimeData>());
	}

	[Test]
	public async Task UpdateWarningTimeJob_HandlesNullData()
	{
		// Arrange
		var dataService = Substitute.For<IExpandedObjectDataService>();
		var logger = Substitute.For<ILogger<ScheduledTaskManagementService.UpdateWarningTimeJob>>();
		var jobContext = Substitute.For<IJobExecutionContext>();
		var jobDetail = Substitute.For<IJobDetail>();
		var jobDataMap = new JobDataMap { { "interval", 3600.0 } };

		dataService.GetExpandedServerDataAsync<UptimeData>().Returns(ValueTask.FromResult<UptimeData?>(null));
		jobContext.JobDetail.Returns(jobDetail);
		jobDetail.JobDataMap.Returns(jobDataMap);

		var job = new ScheduledTaskManagementService.UpdateWarningTimeJob(dataService, logger);

		// Act
		await job.Execute(jobContext);

		// Assert - should not throw, should not update
		await dataService.DidNotReceive().SetExpandedServerDataAsync(Arg.Any<UptimeData>());
	}

	[Test]
	public async Task UpdatePurgeTimeJob_UpdatesTimeWhenExpired()
	{
		// Arrange
		var dataService = Substitute.For<IExpandedObjectDataService>();
		var logger = Substitute.For<ILogger<ScheduledTaskManagementService.UpdatePurgeTimeJob>>();
		var jobContext = Substitute.For<IJobExecutionContext>();
		var jobDetail = Substitute.For<IJobDetail>();
		var jobDataMap = new JobDataMap { { "interval", 3600.0 } }; // 1 hour

		var now = DateTimeOffset.UtcNow;
		var oldData = new UptimeData(
			StartTime: now.AddHours(-2),
			LastRebootTime: now.AddHours(-2),
			Reboots: 0,
			NextWarningTime: now.AddHours(1),
			NextPurgeTime: now.AddMinutes(-5) // Expired 5 minutes ago
		);

		dataService.GetExpandedServerDataAsync<UptimeData>().Returns(ValueTask.FromResult<UptimeData?>(oldData));
		jobContext.JobDetail.Returns(jobDetail);
		jobDetail.JobDataMap.Returns(jobDataMap);

		var job = new ScheduledTaskManagementService.UpdatePurgeTimeJob(dataService, logger);

		// Act
		await job.Execute(jobContext);

		// Assert
		await dataService.Received(1).SetExpandedServerDataAsync(
			Arg.Is<UptimeData>(d => d.NextPurgeTime > now && d.NextPurgeTime <= now.AddHours(1.1))
		);
	}

	[Test]
	public async Task UpdatePurgeTimeJob_DoesNotUpdateWhenNotExpired()
	{
		// Arrange
		var dataService = Substitute.For<IExpandedObjectDataService>();
		var logger = Substitute.For<ILogger<ScheduledTaskManagementService.UpdatePurgeTimeJob>>();
		var jobContext = Substitute.For<IJobExecutionContext>();
		var jobDetail = Substitute.For<IJobDetail>();
		var jobDataMap = new JobDataMap { { "interval", 3600.0 } }; // 1 hour

		var now = DateTimeOffset.UtcNow;
		var oldData = new UptimeData(
			StartTime: now.AddHours(-2),
			LastRebootTime: now.AddHours(-2),
			Reboots: 0,
			NextWarningTime: now.AddHours(1),
			NextPurgeTime: now.AddHours(1) // Not expired yet
		);

		dataService.GetExpandedServerDataAsync<UptimeData>().Returns(ValueTask.FromResult<UptimeData?>(oldData));
		jobContext.JobDetail.Returns(jobDetail);
		jobDetail.JobDataMap.Returns(jobDataMap);

		var job = new ScheduledTaskManagementService.UpdatePurgeTimeJob(dataService, logger);

		// Act
		await job.Execute(jobContext);

		// Assert
		await dataService.DidNotReceive().SetExpandedServerDataAsync(Arg.Any<UptimeData>());
	}

	[Test]
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public async Task Service_StartsWithValidConfiguration(WebAppFactory factory)
	{
		// This integration test verifies the service can start with the WebAppFactory
		var schedulerFactory = factory.Services.GetService<ISchedulerFactory>();
		
		// The fact that the factory initialized successfully and has a scheduler means the service can work
		await Assert.That(schedulerFactory).IsNotNull();
	}
}
