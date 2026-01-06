using System.Configuration;
using System.Data;
using System.Windows;
using Serilog;

namespace PdfTranslator;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Configure logging
        var logPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PdfTranslator",
            "logs",
            "app.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Log.Information("Application started");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application exiting");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}

