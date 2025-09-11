using Basip;
using Microsoft.Extensions.Logging;

await Host.CreateDefaultBuilder(args).ConfigureServices((context, services) =>
{
    IConfiguration configuration = context.Configuration;

    WorkerOptions options = configuration.GetSection("Service").Get<WorkerOptions>();

    services.AddSingleton(options);
    services.AddHostedService<Worker>();
}).ConfigureLogging((context, logging) =>
{
    logging.ClearProviders();
    logging.AddConsole();
    string path = context.Configuration.GetSection("Log").GetValue<string>("LogFolerPath");
    int? retainedFileCountLimit = context.Configuration.GetSection("Log").GetValue<int?>("retainedFileCountLimit");
    logging.AddConfiguration(context.Configuration.GetSection("Logging"));
    logging.AddFile(
        pathFormat: $"{path}\\ArtonitBasIpTools.log",
        minimumLevel: context.Configuration.GetSection("Log").GetValue<LogLevel>("LogLevel"),
        retainedFileCountLimit: retainedFileCountLimit,
        outputTemplate: "{Timestamp:dd-MM-yyyy HH:mm:ss.fff}\t-\t[{Level:u3}] {Message}{NewLine}{Exception}");

}).UseWindowsService().Build().RunAsync();

