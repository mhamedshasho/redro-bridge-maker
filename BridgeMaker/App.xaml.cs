using System;
using System.IO;
using System.Windows;

namespace RedroBridgeMaker;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrash(args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrash(args.Exception);
            MessageBox.Show("Redro Bridge Maker could not start.\n\n" + args.Exception.Message + "\n\nA crash log was saved to:\n" + LogPath(),"Redro Bridge Maker - Startup Error",MessageBoxButton.OK,MessageBoxImage.Error);
            args.Handled = true; Shutdown(1);
        };
        try { base.OnStartup(e); }
        catch (Exception ex)
        {
            WriteCrash(ex);
            MessageBox.Show("Redro Bridge Maker could not start.\n\n" + ex,"Redro Bridge Maker - Startup Error",MessageBoxButton.OK,MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    static string LogPath()
    {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RedroBridgeMaker");
        Directory.CreateDirectory(dir); return Path.Combine(dir,"startup-error.txt");
    }
    static void WriteCrash(Exception? ex)
    {
        try { File.WriteAllText(LogPath(),DateTime.Now+Environment.NewLine+(ex?.ToString()??"Unknown startup error")); } catch {}
    }
}