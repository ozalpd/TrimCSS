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
        var window = InfiniFrameWindowBuilder.Create()
                                            .SetTitle("TrimCSS")
                                            .SetSize(1280, 720)
                                            .SetStartPageUrl(uri)
                                            .Build();

        var bridge = new GuiMessageBridge();
        bridge.Register(window);

        window.Center();
        window.WaitForClose();
    }
}
