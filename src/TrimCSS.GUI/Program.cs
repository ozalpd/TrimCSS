using InfiniFrame;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        //InfiniFrameSingleFileBootstrap.Initialize();

        var window = InfiniFrameWindowBuilder.Create()
                                            .SetTitle("Trim CSS")
                                            .SetSize(1280, 720)
                                            .SetStartPageUrl("index.html")
                                            .Build();

        window.Center();
        window.WaitForClose();
    }
}