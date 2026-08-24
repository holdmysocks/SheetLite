namespace SheetLite;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        ToolStripManager.RenderMode = ToolStripManagerRenderMode.Professional;
        ToolStripManager.Renderer = new DraculaRenderer(); // global fallback: every strip renders themed even if its own renderer is lost
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) => ShowUnexpectedError(eventArgs.Exception, terminating: false);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) => ShowUnexpectedError(eventArgs.ExceptionObject as Exception ?? new Exception("Unknown application error."), eventArgs.IsTerminating);
        Application.Run(new MainForm(args.FirstOrDefault()));
    }

    private static void ShowUnexpectedError(Exception exception, bool terminating)
    {
        try
        {
            string guidance = terminating ? "SheetLite must close." : "You may be able to save your work, but restarting SheetLite is recommended.";
            MessageBox.Show($"SheetLite encountered an unexpected error.\n\n{exception.Message}\n\n{guidance}", "SheetLite error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }
}
