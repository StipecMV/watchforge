using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;

namespace WatchForge.MotionSentinel.Server.Service.Infrastructure.Logging;

/// <summary>Configures log4net with a rolling daily file appender without relying on an XML config file.</summary>
public static class Log4NetConfigurator
{
    /// <summary>
    /// Initialises log4net and starts writing logs to daily files under <paramref name="logDirectory"/>.
    /// Creates the directory if it does not already exist.
    /// </summary>
    public static void Configure(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);

        var hierarchy = (Hierarchy)LogManager.GetRepository();

        var patternLayout = new PatternLayout
        {
            ConversionPattern = "%date [%thread] %-5level %logger — %message%newline"
        };
        patternLayout.ActivateOptions();

        var rollingAppender = new RollingFileAppender
        {
            File               = Path.Combine(logDirectory, string.Empty),
            DatePattern        = "HH-dd_MM_yyyy'.log'",
            RollingStyle       = RollingFileAppender.RollingMode.Date,
            StaticLogFileName  = false,
            MaxSizeRollBackups = 30,          // retain last 30 log files
            Layout             = patternLayout,
            AppendToFile       = true,
            LockingModel       = new RollingFileAppender.MinimalLock()
        };
        rollingAppender.ActivateOptions();

        hierarchy.Root.AddAppender(rollingAppender);
        hierarchy.Root.Level  = Level.Debug;
        hierarchy.Configured  = true;
    }

    /// <summary>Returns the log file name that <see cref="Configure"/> would produce for the given timestamp.</summary>
    public static string GetLogFileName(DateTimeOffset at)
        => $"{at:HH-dd_MM_yyyy}.log";
}
