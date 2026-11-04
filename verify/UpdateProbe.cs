using System;
using System.IO;
using System.Reflection;
using System.Threading;

#if NEW_VERSION
[assembly: AssemblyVersion("1.0.1.0")]
#else
[assembly: AssemblyVersion("1.0.0.0")]
#endif

static class UpdateProbe
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--wait") { Thread.Sleep(2000); return; }
        string folder = AppDomain.CurrentDomain.BaseDirectory;
        File.WriteAllText(Path.Combine(folder, "started.txt"), Assembly.GetExecutingAssembly().GetName().Version.ToString());
#if !BAD_START
        if (args.Length == 2 && args[0] == "--updated") File.WriteAllText(args[1], "ready");
#endif
    }
}
