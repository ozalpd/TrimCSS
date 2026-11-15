using InfiniFrame;
using TrimCSS.GUI.Bridge;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        //InfiniFrameSingleFileBootstrap.Initialize();
        
        string baseDirectory = AppContext.BaseDirectory;
        var uri = new Uri(Path.Combine(baseDirectory, "wwwroot", "index.html")).AbsoluteUri;

        var builder = InfiniFrameWindowBuilder.Create()
                   ?? throw new InvalidOperationException("Failed to create InfiniFrameWindowBuilder.");
        builder.SetTitle("Trim CSS")
               .SetSize(1280, 720)
               .SetStartPageUrl(uri);
#if DEBUG
        builder.SetRemoteDebuggingPort(1888);
        builder.EnableDevTools();
#endif

        var staticAssets = builder.StaticAssets;

        var bridge = new GuiMessageBridge();
        bridge.Register(builder);

        var window = builder.Build();
        window.Center();
        window.WaitForClose();
    }
}
