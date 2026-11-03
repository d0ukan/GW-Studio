using System.Configuration;
using System.Data;
using System.Windows;
using Dou_GW_Studio.Helpers; // Helpers klasörü namespace'i

namespace Dou_GW_Studio;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        NativeMethods.TimeBeginPeriod(1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        NativeMethods.TimeEndPeriod(1);
        base.OnExit(e);
    }
}
