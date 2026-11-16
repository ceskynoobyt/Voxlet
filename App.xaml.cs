using System.Windows;
namespace Voxlet;
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); LocalPaths.Initialize(); MeetingAudioCapture.CleanupAbandoned();
        var settings = SettingsStore.Load();
        if (!settings.SetupComplete) new SetupWindow(settings).ShowDialog();
        if (!settings.SetupComplete) { Shutdown(); return; }
        MainWindow = new MainWindow(settings); MainWindow.Show();
    }
}

