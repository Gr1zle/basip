using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;

namespace CustomController;

class Program
{
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine("logs", "controller-worker-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            IHost host = Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureServices((hostContext, services) =>
                {
                    // Привязываем секцию "Service"
                    services.Configure<WorkerOptions>(hostContext.Configuration.GetSection("Service"));

                    services.AddSingleton<WorkerOptions>(sp =>
                        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkerOptions>>().Value);

                    services.AddHostedService<Worker>();
                })
                .Build();

            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Критическая ошибка запуска сервиса");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}