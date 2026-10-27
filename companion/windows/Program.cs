using System.Text.Json;
using ImageMagick;

namespace UpscaleLab.Companion;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        if (args.Contains("--inventory"))
        {
            var json = JsonSerializer.Serialize(new { platform = "Windows", manufacturer = DisplayInventory.Hardware("SystemManufacturer"),
                model = DisplayInventory.Hardware("SystemProductName"), displays = DisplayInventory.Read() }, AgentApi.Json);
            var index = Array.IndexOf(args, "--inventory");
            if (index + 1 < args.Length) File.WriteAllText(args[index + 1], json); else Console.WriteLine(json);
            return;
        }
        using var mutex = new Mutex(true, @"Local\UpscaleLabConnect", out var first);
        if (!first) { MessageBox.Show("Upscale Lab Connect가 이미 실행 중입니다. 트레이에서 열어 주세요."); return; }
        ResourceLimits.Memory = 256UL * 1024 * 1024;
        ResourceLimits.Disk = 256UL * 1024 * 1024; ResourceLimits.Thread = 2;
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainForm());
    }
}
