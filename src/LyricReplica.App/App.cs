using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using LyricReplica.Core;

namespace LyricReplica.App;

public sealed class App : Application
{
    public static string AssemblySha256 { get; } = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location)));

    public static string DataFolder { get; } = Path.Combine(
        AppContext.BaseDirectory,
        "UserData",
        Environment.GetCommandLineArgs().Contains("--verify-runtime") ? "RuntimeVerification" :
        Environment.GetCommandLineArgs().Contains("--verify-isolation-gpu") ? "GpuVerification" :
        Environment.GetCommandLineArgs().Contains("--verify-offscreen") ? "Offscreen" :
        Environment.GetCommandLineArgs().Contains("--self-test") ? "SelfTest" : "Profile");

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.AppendAllText(Path.Combine(DataFolder, "app.log"), DateTime.Now.ToString("O") + " " + message + Environment.NewLine);
        }
        catch (IOException) { }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        Directory.CreateDirectory(DataFolder);
        var app = new App();
        if (args.Contains("--verify-runtime"))
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            try
            {
                var verification = RuntimeChecks.RunAsync(DataFolder);
                var frame = new DispatcherFrame();
                verification.ContinueWith(_ => app.Dispatcher.BeginInvoke(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                verification.GetAwaiter().GetResult();
                app.Shutdown();
                return 0;
            }
            catch (Exception exception) { File.WriteAllText(Path.Combine(DataFolder, "failure.txt"), exception.ToString()); app.Shutdown(); return 1; }
        }
        if (args.Contains("--verify-isolation-gpu"))
        {
            try
            {
                IsolationGpuChecks.Run(DataFolder);
                app.Shutdown();
                return 0;
            }
            catch (Exception exception) { File.WriteAllText(Path.Combine(DataFolder, "failure.txt"), exception.ToString()); app.Shutdown(); return 1; }
        }
        if (args.Contains("--verify-offscreen"))
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            try
            {
                LoadResources(app);
                var verification = new MainWindow(args).VerifyOffscreenAsync(Path.Combine(DataFolder, "verification"));
                var frame = new DispatcherFrame();
                verification.ContinueWith(_ => app.Dispatcher.BeginInvoke(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                verification.GetAwaiter().GetResult();
                File.Delete(Path.Combine(DataFolder, "failure.txt"));
                app.Shutdown();
                return 0;
            }
            catch (Exception e) { Log(e.ToString()); File.WriteAllText(Path.Combine(DataFolder, "failure.txt"), e.ToString()); app.Shutdown(); return 1; }
        }
        app.DispatcherUnhandledException += (_, e) => { Log(e.Exception.ToString()); if (args.Contains("--self-test")) { app.Shutdown(1); return; } MessageBox.Show(e.Exception.Message, "LyricReplica", MessageBoxButton.OK, MessageBoxImage.Error); e.Handled = true; };
        LoadResources(app);
        var window = new MainWindow(args);
        return app.Run(window);
    }
    private static void LoadResources(Application app)
    {
        foreach (string name in new[] { "AppleMusicIcons", "ReferenceMenus" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Assets/{name}.xaml") });
    }
    public static ViewOptions LoadOptions()
    {
        try
        {
            return SettingsFile.Load(Path.Combine(DataFolder, "settings.json"), exception => Log("Settings: " + exception.Message));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { Log("Settings: " + e.Message); return new ViewOptions(); }
    }
    public static void SaveOptions(ViewOptions options)
    {
        SettingsFile.Save(Path.Combine(DataFolder, "settings.json"), options);
    }
}
