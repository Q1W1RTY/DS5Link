using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DS5Link;

// Regression checks for review findings.
// No production pipe, keyboard injection, service or user settings are touched.
internal static class AdversarialReview
{
    internal static async Task Run()
    {
        var name = "DS5Link.Review." + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var send = typeof(BridgeClient).GetMethod("SendPayloadAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(TouchInputBatch));
        var cursor = new InputEventCursor();
        cursor.Read(new TouchInputBatch { Epoch = "review", Sequence = 1 });
        var age = Stopwatch.StartNew();
        var pending = (Task<TouchInputBatch>)send.Invoke(null,
            new object?[] { "getInputEvents", name, null, 1L, 0, 0L, null, null })!;
        await server.WaitForConnectionAsync();
        using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, true);
        using var request = JsonDocument.Parse((await reader.ReadLineAsync())!);
        var response = JsonSerializer.Serialize(new {
            protocolVersion = 1, id = request.RootElement.GetProperty("id").GetString(), ok = true,
            payload = new TouchInputBatch { Epoch = "review", Sequence = 2, Enabled = true,
                Events = new[] { new TouchInputEvent { Sequence = 2, Action = "tap", AgeMs = 0 } } }
        });
        await Task.Delay(350); // Response already sampled; delivery is delayed.
        await server.WriteAsync(Encoding.UTF8.GetBytes(response + "\n"));
        var batch = await pending;
        var action = cursor.Read(batch).Single();
        var foreground = new IntPtr(1);
        if (MainWindow.IsTouchEventCurrent(action,age.ElapsedMilliseconds,foreground,foreground) ||
            MainWindow.IsTouchEventCurrent(action,20,foreground,new IntPtr(2)) ||
            !MainWindow.IsTouchEventCurrent(action,20,foreground,foreground))
            throw new Exception("Touch freshness or foreground guard failed.");
        Console.WriteLine("Delayed touch and foreground change checks passed.");

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var folder = Path.Combine(root, "artifacts", "adversarial-review");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"corrupt-profiles-{Guid.NewGuid():N}.json");
        if (File.Exists(path)) throw new IOException("Refusing to overwrite existing review fixture.");
        try {
            const string damaged = "{\"SchemaVersion\":1,\"Games\":null}";
            File.WriteAllText(path, damaged);
            try { GameProfiles.Load(path, new(), new()); throw new Exception("Malformed profile accepted."); }
            catch (IOException) { }
            new GameProfiles().Save(path, preservePrevious:true);
            var saved = Directory.GetFiles(folder,Path.GetFileName(path)+".corrupt-*").Single();
            try {
                if (File.ReadAllText(saved)!=damaged || !GameProfiles.Read(path).Valid)
                    throw new Exception("Profile recovery lost the damaged original or failed to save the replacement.");
            }
            finally { File.Delete(saved); }
            Console.WriteLine("Profile recovery preserved the original and installed the replacement.");
        }
        finally { File.Delete(path); }
    }
}
