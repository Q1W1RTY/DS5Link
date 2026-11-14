using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace DS5Link;

public sealed class ProcessLoopback(uint pid, Action<byte[], int> onData, Action<Exception> onError) : IDisposable
{
    readonly CancellationTokenSource stop = new();
    internal static readonly WaveFormat Format = new(44100, 16, 2);

    internal void Start()
    {
        var token = stop.Token;
        _ = Task.Run(async () =>
        {
            try { await Capture(token); }
            catch (Exception error) when (!token.IsCancellationRequested) { onError(error); }
            catch (Exception) when (token.IsCancellationRequested) { }
        });
    }

    async Task Capture(CancellationToken token)
    {
        var callback = new Activation();
        var parameters = Marshal.AllocHGlobal(12);
        IActivationOperation operation;
        try
        {
            Marshal.WriteInt32(parameters, 0, 1); // Process loopback activation.
            Marshal.WriteInt32(parameters, 4, (int)pid);
            Marshal.WriteInt32(parameters, 8, 0); // Include process tree.
            var variant = new BlobVariant { Type = 65, Size = 12, Data = parameters };
            var iid = typeof(IAudioClient).GUID;
            Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid,
                ref variant, callback, out operation));
        }
        finally { Marshal.FreeHGlobal(parameters); }

        IAudioClient client;
        try { client = await callback.Result.Task.WaitAsync(TimeSpan.FromSeconds(5), token); }
        catch { callback.Abandon(); throw; }
        finally { GC.KeepAlive(operation); }
        try
        {
            token.ThrowIfCancellationRequested();
            Marshal.ThrowExceptionForHR(client.Initialize(0, (NAudio.CoreAudioApi.AudioClientStreamFlags)0x80020000,
                0, 0, Format, Guid.Empty));
            var iid = typeof(IAudioCaptureClient).GUID;
            Marshal.ThrowExceptionForHR(client.GetService(iid, out var service));
            var capture = (IAudioCaptureClient)service;
            try
            {
                Marshal.ThrowExceptionForHR(client.Start());
                try
                {
                    byte[] buffer = new byte[8192];
                    while (!token.IsCancellationRequested)
                    {
                        Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out var frames));
                        if (frames == 0) { await Task.Delay(5, token); continue; }
                        Marshal.ThrowExceptionForHR(capture.GetBuffer(out var data, out frames, out var flags, out _, out _));
                        try
                        {
                            int length = checked(frames * Format.BlockAlign);
                            if (buffer.Length < length) buffer = new byte[length];
                            if (((int)flags & 2) != 0) Array.Clear(buffer, 0, length);
                            else Marshal.Copy(data, buffer, 0, length);
                            onData(buffer, length);
                        }
                        finally { Marshal.ThrowExceptionForHR(capture.ReleaseBuffer(frames)); }
                    }
                }
                finally { client.Stop(); }
            }
            finally { Marshal.ReleaseComObject(capture); }
        }
        finally { Marshal.ReleaseComObject(client); }
    }

    public void Dispose() { stop.Cancel(); stop.Dispose(); }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct BlobVariant
    {
        [FieldOffset(0)] internal ushort Type;
        [FieldOffset(8)] internal int Size;
        [FieldOffset(16)] internal IntPtr Data;
    }

    [DllImport("Mmdevapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int ActivateAudioInterfaceAsync(string path, ref Guid iid, ref BlobVariant parameters,
        IActivationHandler handler, out IActivationOperation operation);

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out int frames, out uint flags, out ulong position, out ulong timestamp);
        [PreserveSig] int ReleaseBuffer(int frames);
        [PreserveSig] int GetNextPacketSize(out int frames);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivationOperation
    {
        [PreserveSig] int GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object client);
    }

    [ComVisible(true), Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivationHandler
    {
        [PreserveSig] int ActivateCompleted(IActivationOperation operation);
    }

    [ComVisible(true), Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAgileObject { }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class Activation : IActivationHandler, IAgileObject
    {
        internal readonly TaskCompletionSource<IAudioClient> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Abandon()
        {
            if (!Result.TrySetCanceled() && Result.Task.IsCompletedSuccessfully)
                Marshal.ReleaseComObject(Result.Task.Result);
        }

        public int ActivateCompleted(IActivationOperation operation)
        {
            try
            {
                Marshal.ThrowExceptionForHR(operation.GetActivateResult(out int result, out var client));
                Marshal.ThrowExceptionForHR(result);
                if (!Result.TrySetResult((IAudioClient)client)) Marshal.ReleaseComObject(client);
            }
            catch (Exception error) { Result.TrySetException(error); }
            return 0;
        }
    }
}
