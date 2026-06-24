using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Quartz;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Services;

public class ScheduledTaskManagementServiceTests
{
	[Test]
	public async Task ParseTimeInterval_ValidHourInterval_ReturnsCorrectTimeSpan()
	{
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
		var dataService = Substitute.For<IExpandedObjectDataService>();
		var logger = Substitute.For<ILogger<ScheduledTaskManagementService.UpdateWarningTimeJob>>();
		var jobContext = Substitute.For<IJobExecutionContext>();
		var jobDetail = Substitute.For<IJobDetail>();
		var jobDataMap = new JobDataMap { { "interval", 3600.0 } };

		var now = DateTimeOffset.UtcNow;
		var oldData = new UptimeData(
			StartTime: now.AddHours(-2),
			LastRebootTime: now.AddHours(-2),
			Reboots: 0,
			NextWarningTime: now.AddMinutes(-5),
			NextPurgeTime: now.AddHours(1)
		);

		dataService.GetExpandedServerDataAsync<UptimeData>().Returns(ValueTask.FromResult<UptimeData?>(oldData));
		jobContext.JobDetail.Returns(jobDetail);
		jobDetail.JobDataMap.Returns(jobDataMap);

		var job = new ScheduledTaskManagementService.UpdateWarningTimeJob(dataService, logger);

		await job.Execute(jobContext);

		await dataService.Received(1).SetExpandedServerDataAsync(
			Arg.Is<UptimeData>(d => d.NextWarningTime > now && d.NextWarningTime <= now.AddHours(1.1))
		);
	}

	[Test]
	public async Task UpdateWarningTimeJob_DoesNotUpdateWhenNotExpired()
	{
		var dataService = Substitute.For<IExpandedObjectDataService>();
		var logger = Substitute.For<ILogger<ScheduledTaskManagementService.UpdateWarningTimeJob>>();
		var jobContext = Substitute.For<IJobExecutionContext>();
		var jobDetail = Substitute.For<IJobDetail>();
		var jobDataMap = new JobDataMap { { "interval", 3600.0 } };

		var now = DateTimeOffset.UtcNow;
		var oldData = new UptimeData(
			StartTime: now.AddHours(-2),
			LastRebootTime: now.AddHours(-2),
			Reboots: 0,
			NextWarningTime: now.AddHours(1),
			NextPurgeTime: now.AddHours(1)
		);

		dataService.GetExpandedServerDataAsync<UptimeData>().Returns(ValueTask.FromResult<UptimeData?>(oldData));
		jobContext.JobDetail.Returns(jobDetail);
		jobDetail.JobDataMap.Returns(jobDataMap);

		var job = new ScheduledTaskManagementService.UpdateWarningTimeJob(dataService, logger);

		await job.Execute(jobContext);

		await dataService.DidNotReceive().SetExpandedServerDataAsync(Arg.Any<UptimeData>());
	}

	[Test]
	public async Task UpdateWarningTimeJob_HandlesNullData()
	{
		var dataService = Substitute.For<IExpandedObjectDataService>();
		var logger = Substitute.For<ILogger<ScheduledTaskManagementService.UpdateWarningTimeJob>>();
		var jobContext = Substitute.For<IJobExecutionContext>();
		var jobDetail = Substitute.For<IJobDetail>();
		var jobDataMap = new JobDataMap { { "interval", 3600.0 } };

		dataService.GetExpandedServerDataAsync<UptimeData>().Returns(ValueTask.FromResult<UptimeData?>(null));
		jobContext.JobDetail.Returns(jobDetail);
		jobDetail.JobDataMap.Returns(jobDataMap);

		var job = new ScheduledTaskManagementService.UpdateWarningTimeJob(dataService, logger);

		await job.Execute(jobContext);

		await dataService.DidNotReceive().SetExpandedServerDataAsync(Arg.Any<UptimeData>());
	}

	[Test]
	public async Task UpdatePurgeTimeJob_UpdatesTimeWhenExpired()
	{
		var dataService = Substitute.For<IExpandedObjectDataService>();
		var logger = Substitute.For<ILogger<ScheduledTaskManagementService.UpdatePurgeTimeJob>>();
		var jobContext = Substitute.For<IJobExecutionContext>();
		var jobDetail = Substitute.For<IJobDetail>();
		var jobDataMap = new JobDataMap { { "interval", 3600.0 } };

		var now = DateTimeOffset.UtcNow;
		var oldData = new UptimeData(
			StartTime: now.AddHours(-2),
			LastRebootTime: now.AddHours(-2),
			Reboots: 0,
			NextWarningTime: now.AddHours(1),
			NextPurgeTime: now.AddMinutes(-5)
		);

		dataService.GetExpandedServerDataAsync<UptimeData>().Returns(ValueTask.FromResult<UptimeData?>(oldData));
		jobContext.JobDetail.Returns(jobDetail);
		jobDetail.JobDataMap.Returns(jobDataMap);

		var job = new ScheduledTaskManagementService.UpdatePurgeTimeJob(dataService, logger);

		await job.Execute(jobContext);

		await dataService.Received(1).SetExpandedServerDataAsync(
			Arg.Is<UptimeData>(d => d.NextPurgeTime > now && d.NextPurgeTime <= now.AddHours(1.1))
		);
	}

	[Test]
	public async Task UpdatePurgeTimeJob_DoesNotUpdateWhenNotExpired()
	{
		var dataService = Substitute.For<IExpandedObjectDataService>();
		var logger = Substitute.For<ILogger<ScheduledTaskManagementService.UpdatePurgeTimeJob>>();
		var jobContext = Substitute.For<IJobExecutionContext>();
		var jobDetail = Substitute.For<IJobDetail>();
		var jobDataMap = new JobDataMap { { "interval", 3600.0 } };

		var now = DateTimeOffset.UtcNow;
		var oldData = new UptimeData(
			StartTime: now.AddHours(-2),
			LastRebootTime: now.AddHours(-2),
			Reboots: 0,
			NextWarningTime: now.AddHours(1),
			NextPurgeTime: now.AddHours(1)
		);

		dataService.GetExpandedServerDataAsync<UptimeData>().Returns(ValueTask.FromResult<UptimeData?>(oldData));
		jobContext.JobDetail.Returns(jobDetail);
		jobDetail.JobDataMap.Returns(jobDataMap);

		var job = new ScheduledTaskManagementService.UpdatePurgeTimeJob(dataService, logger);

		await job.Execute(jobContext);

		await dataService.DidNotReceive().SetExpandedServerDataAsync(Arg.Any<UptimeData>());
	}

	[Test]
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public async Task Service_StartsWithValidConfiguration(ServerWebAppFactory factory)
	{
		var schedulerFactory = factory.Services.GetService<ISchedulerFactory>();

		await Assert.That(schedulerFactory).IsNotNull();
	}
}
