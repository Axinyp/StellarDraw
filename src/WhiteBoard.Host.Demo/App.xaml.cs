namespace WhiteBoard.Host.Demo;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局未处理异常处理
        DispatcherUnhandledException += (_, ex) =>
        {
            System.Diagnostics.Debug.WriteLine($"[App] Unhandled: {ex.Exception}");
            ex.Handled = true;
        };
    }
}