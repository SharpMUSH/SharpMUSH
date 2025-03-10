﻿using Microsoft.Extensions.Hosting;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Server;

public class SchedulerService(ITaskScheduler scheduler, IMUSHCodeParser parser) : BackgroundService
{
	private readonly PeriodicTimer _timer = new(TimeSpan.FromMilliseconds(1));

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (await _timer.WaitForNextTickAsync(stoppingToken))
		{
			await scheduler.ExecuteAsync(parser, stoppingToken);
		}
	}
}