using System.Runtime.InteropServices;
using System.Text.Json;

namespace UpscaleLab.Companion;

internal static class SecureStore
{
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UpscaleLab", "Connect");
    private static string PathName => Path.Combine(Root, "credentials.bin");
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            var success = protect ? CryptProtectData(ref input, "UpscaleLab Connect", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
    }
    public static Credentials? Read()
    {
        try { return JsonSerializer.Deserialize<Credentials>(Transform(File.ReadAllBytes(PathName), false), AgentApi.Json); }
        catch (Exception error) when (error is IOException or JsonException or System.ComponentModel.Win32Exception) { return null; }
    }
    public static void Write(Credentials value)
    {
        Directory.CreateDirectory(Root);
        var bytes = Transform(JsonSerializer.SerializeToUtf8Bytes(value with { Session = value.Remember ? value.Session : null }, AgentApi.Json), true);
        File.WriteAllBytes(PathName + ".tmp", bytes);
        File.Move(PathName + ".tmp", PathName, true);
    }
}
