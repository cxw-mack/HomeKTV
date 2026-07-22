using HomeKTV.Core.Portable;
using Serilog;

namespace HomeKTV.Infrastructure.Logging;

public static class LoggingBootstrap
{
    public static ILogger CreateLogger(PortablePaths paths, string level = "Information")
    {
        paths.EnsureDirectories();
        var minimum = Enum.TryParse<Serilog.Events.LogEventLevel>(level, true, out var parsed) ? parsed : Serilog.Events.LogEventLevel.Information;
        return new LoggerConfiguration().MinimumLevel.Is(minimum).Enrich.FromLogContext()
            .WriteTo.File(Path.Combine(paths.Logs, "HomeKTV-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30, shared: true)
            .CreateLogger();
    }
}

