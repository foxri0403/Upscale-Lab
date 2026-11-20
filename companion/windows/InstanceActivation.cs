using System.IO.Pipes;
using System.Security.Principal;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UpscaleLab.Companion;

internal sealed class InstanceActivation(string name) : IDisposable
{
    internal static string Name => "UpscaleLabConnect-" + WindowsIdentity.GetCurrent().User!.Value + "-" + CompanionConfiguration.ServerId;
    private readonly CancellationTokenSource stop = new();
    private Task? listener;
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint id);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint id);
    internal void Listen(Action activate, Action? exit = null) => listener = Task.Run(async () => {
        while (!stop.IsCancellationRequested) {
            try {
                using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                var signal = new byte[1];
                if (await pipe.ReadAsync(signal, stop.Token) != 1) continue;
                if (signal[0] == 1) activate();
                if (signal[0] == 2) exit?.Invoke();
            } catch (OperationCanceledException) { return; }
            catch (IOException) when (!stop.IsCancellationRequested) { }
        }
    });
    internal Task<bool> Activate(TimeSpan timeout) => SendAsync(1, timeout);
    internal Task<bool> RequestExit(TimeSpan timeout) => SendAsync(2, timeout);

    private async Task<bool> SendAsync(byte signal, TimeSpan timeout) {
        using var deadline = new CancellationTokenSource(timeout);
        try {
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(deadline.Token);
            if (signal == 1 && GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var id)) AllowSetForegroundWindow(id);
            await pipe.WriteAsync(new[] { signal }, deadline.Token); return true;
        } catch (Exception e) when (e is OperationCanceledException or IOException or UnauthorizedAccessException) { return false; }
    }
    public void Dispose() { stop.Cancel(); if (listener != null) _ = listener.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default); else stop.Dispose(); }
}
