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
    internal void Listen(Action activate) => listener = Task.Run(async () => {
        while (!stop.IsCancellationRequested) {
            try {
                using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                var signal = new byte[1];
                if (await pipe.ReadAsync(signal, stop.Token) == 1 && signal[0] == 1) activate();
            } catch (OperationCanceledException) { return; }
            catch (IOException) when (!stop.IsCancellationRequested) { }
        }
    });
    internal async Task<bool> Activate(TimeSpan timeout) {
        using var deadline = new CancellationTokenSource(timeout);
        try {
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(deadline.Token);
            if (GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var id)) AllowSetForegroundWindow(id);
            await pipe.WriteAsync(new byte[] { 1 }, deadline.Token); return true;
        } catch (Exception e) when (e is OperationCanceledException or IOException or UnauthorizedAccessException) { return false; }
    }
    public void Dispose() { stop.Cancel(); if (listener != null) _ = listener.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default); else stop.Dispose(); }
}
