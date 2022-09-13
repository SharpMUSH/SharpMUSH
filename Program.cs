using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
//using Microsoft.Extensions.Hosting.Abstractions;
using SharpMUSH.DB;
using SharpMUSH.Nologin;
using SharpMUSH.Python;

namespace SharpMUSH
{
    public class Program
    {
        // Main

        private static void Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();
            host.RunAsync();
            Game game = host.Services.GetService<Game>();
            game.Start();

            var Server = host.Services.GetService<MUSHServer>();

            for (; ; )
            {
                string line = Console.ReadLine();
                //if (string.IsNullOrEmpty(line))
                //    break;

                // Restart the server
                if (line == "!")
                {
                    Console.Write("Server restarting...");
                    Server.Restart();
                    Console.WriteLine("Done!");
                    continue;
                }

                // Multicast admin message to all sessions
                line = "(admin) " + line;
                Server.Multicast(line);
            }

            // Stop the server


        }

        private static IHostBuilder CreateHostBuilder(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs/myapp.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();
            var hostBuilder = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, builder) =>
                {
                    builder.SetBasePath(Directory.GetCurrentDirectory());


                }).ConfigureLogging((context, logger) =>
                {
                    logger.Services.AddLogging(builder => builder.AddSerilog(dispose: true));
                })
                .ConfigureServices((context, services) =>
                {
                    //add your service registrations
                    services.AddTransient<Program>();


                    services.AddSingleton<Game>();

                    string DbPath = System.IO.Path.Join(Environment.CurrentDirectory, "mush.db");
                    services.AddDbContext<MUSHContext>(options =>
                    {
                        options.UseSqlite($"Data Source={DbPath}");
                        //if (context.HostingEnvironment.IsDevelopment())
                        //    options.EnableSensitiveDataLogging(false);

                        //options.EnableDetailedErrors(false);

                    });
                    services.AddSingleton<MUSHServer>();
                    services.AddTransient<MUSHSession>();
                    services.AddScoped<InputHandler>();
                    services.AddTransient<MUSHDatabase>();
                    services.AddTransient<ScriptNotify>();
                    services.AddTransient<Notify>();
                    services.AddTransient<ScriptDB>();
                    services.AddTransient<connect>();
                    services.AddTransient<ScriptFormat>();

                });



            return hostBuilder;
        }

    }


}