using InfiniFrame;
using TrimCSS.GUI.Bridge;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        //InfiniFrameSingleFileBootstrap.Initialize();

        var window = InfiniFrameWindowBuilder.Create()
                                            .SetTitle("TrimCSS")
                                            .SetSize(1280, 720)
                                            .SetStartPageUrl("index.html")
                                            .Build();

        var bridge = new GuiMessageBridge();
        bridge.Register(window);

        window.Center();
        window.WaitForClose();
    }
}
