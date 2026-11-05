using System.Collections.Concurrent;
using System.Net.Sockets;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace SkiaBgRepro;

/// Simulira vzorec iz prave aplikacije: N kamer, vsaka ima SKCanvasView,
/// časovnik v ozadju menjuje SKBitmap in sproža InvalidateSurface().
public static class CameraSim
{
    public const int CameraCount = 8;

    /// true  = vzorec iz crasha (lock samo v paint, dispose brez locka,
    ///         InvalidateSurface iz delovne niti, brez gating-a ob prehodu v ozadje)
    /// false = popravljena varianta
    public static volatile bool UnsafeMode = true;
    public static volatile bool RenderingEnabled = true;

    static readonly ConcurrentDictionary<int, SKBitmap> Bitmaps = new();
    static readonly ConcurrentDictionary<int, object> Locks = new();
    public static readonly ConcurrentDictionary<int, SKCanvasView> Views = new();
    static CancellationTokenSource? _cts;

    public static long Paints, Swaps, Caught;
    public static string LastEvent = "-";
    public static event Action? Changed;

    static object L(int id) => Locks.GetOrAdd(id, _ => new object());

    public static void OnLifecycle(string name, bool active)
    {
        LastEvent = name;
        Preferences.Set("last_event", name);          // po crashu vidiš, kje se je končalo
        if (!UnsafeMode)
        {
            RenderingEnabled = active;                // Safe: takoj ustavi risanje
        }
        Changed?.Invoke();
    }

    public static void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        for (int i = 0; i < CameraCount; i++)
        {
            int id = i;
            _ = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(4));
                var rnd = new Random(id);
                try
                {
                    while (await timer.WaitForNextTickAsync(ct))
                    {
                        if (!UnsafeMode && !RenderingEnabled) continue;
                        Tick(id, rnd);
                    }
                }
                catch (OperationCanceledException) { }
            }, ct);
        }
    }

    public static void Stop() { _cts?.Cancel(); _cts = null; }

    static void Tick(int id, Random rnd)
    {
        // "Prenesena slika" iz kamere
        var bmp = new SKBitmap(640, 480);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(new SKColor((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256)));
            using var p = new SKPaint { Color = SKColors.White };
            using var font = new SKFont { Size = 60 };
            c.DrawText($"cam {id} #{Swaps}", 20, 100, SKTextAlign.Left, font, p);
        }
        Interlocked.Increment(ref Swaps);

        if (UnsafeMode)
        {
            // Dispose brez locka -> paint lahko riše bitmapo, ki jo druga nit sprošča
            var old = Bitmaps.AddOrUpdate(id, bmp, (_, o) => { o.Dispose(); return bmp; });
            if (Views.TryGetValue(id, out var v))
                v.InvalidateSurface();               // iz delovne niti, brez preverjanja življenjskega cikla
        }
        else
        {
            SKBitmap? old;
            lock (L(id)) { Bitmaps.TryRemove(id, out old); Bitmaps[id] = bmp; old?.Dispose(); }
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!RenderingEnabled) return;
                if (Views.TryGetValue(id, out var v) && v.Handler is not null)
                    v.InvalidateSurface();
            });
        }
    }

    public static void OnPaint(int id, SKPaintSurfaceEventArgs e)
    {
        if (!UnsafeMode && !RenderingEnabled) return;
        try
        {
            lock (L(id))   // isti lock kot v tvoji kodi: ščiti samo paint
            {
                var canvas = e.Surface.Canvas;
                canvas.Clear(SKColors.Black);
                if (Bitmaps.TryGetValue(id, out var bmp))
                {
                    if (UnsafeMode) Thread.SpinWait(2000); // podaljša okno races (kot počasen izris na GPU)
                    canvas.DrawBitmap(bmp, new SKRect(0, 0, e.Info.Width, e.Info.Height));
                }
                Interlocked.Increment(ref Paints);
            }
        }
        catch (Exception) { Interlocked.Increment(ref Caught); } // ujame le managed izjeme, ne EXC_BAD_ACCESS
    }

    // ===================== HTTP kamera, ki ne odgovori =====================
    // 192.0.2.1 = TEST-NET-1 (dokumentacijski naslov), paketi se običajno tiho zavržejo,
    // zato zahteva visi do timeouta. V pravi aplikaciji je to kamera, ki ne dela.
    public static string CameraUrl = "http://192.0.2.1/cam.jpg";
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(20); // v produkciji 100 s
    static readonly HttpClient Http = new() { Timeout = HttpTimeout };
    public static volatile bool HttpMode;

    /// true = isti vzorec kot v tvoji kodi: TcpClient.ConnectAsync pred GetAsync,
    /// brez using, brez timeouta in brez CancellationToken.
    public static volatile bool TcpPrecheck = true;
    public const string CameraHost = "192.0.2.1";
    public const int CameraPort = 80;

    /// false = catch samo za HttpRequestException (SocketException in TaskCanceledException uideta -> crash)
    /// true  = catch (Exception) (aplikacija preživi)
    public static volatile bool HttpCatchTimeout;
    public static long HttpHandled;
    static int _httpGeneration;

    public static void SetHttpMode(bool on)
    {
        HttpMode = on;
        Interlocked.Increment(ref _httpGeneration);   // stari loopi se ustavijo
        if (!on) return;
        int gen = _httpGeneration;
        for (int i = 0; i < CameraCount; i++)
        {
            int id = i;
            MainThread.BeginInvokeOnMainThread(() => PollCamera(id, gen));
        }
    }

    // async void + brez CancellationToken: vzorec iz prave aplikacije
    static async void PollCamera(int id, int gen)
    {
        while (gen == _httpGeneration)
        {
            try
            {
                if (TcpPrecheck)
                {
                    // Kot v tvoji kodi: ni using, ni timeouta. SocketException, če ni dosegljiva.
                    TcpClient tcpClient = new();
                    await tcpClient.ConnectAsync(CameraHost, CameraPort);
                }

                var bytes = await Http.GetByteArrayAsync(CameraUrl);   // po 20 s: TaskCanceledException
                _ = bytes;
            }
            catch (Exception ex) when (HttpCatchTimeout || ex is HttpRequestException)
            {
                Interlocked.Increment(ref HttpHandled);
                await Task.Delay(1000);
            }
            // Če HttpCatchTimeout == false, TaskCanceledException uide iz async void -> crash
        }
    }
}
