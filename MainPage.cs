using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
namespace SkiaBgRepro;

public class MainPage : ContentPage
{
    readonly Grid _grid = new() { ColumnSpacing = 2, RowSpacing = 2 };
    readonly Label _status = new() { FontSize = 12 };
    readonly Label _mode = new() { FontAttributes = FontAttributes.Bold };
    CancellationTokenSource? _churn;

    public MainPage()
    {
        for (int r = 0; r < 4; r++) _grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        for (int c = 0; c < 2; c++) _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        BuildCanvases();

        var modeBtn = new Button { Text = "Preklopi Unsafe / Safe" };
        modeBtn.Clicked += (_, _) =>
        {
            CameraSim.UnsafeMode = !CameraSim.UnsafeMode;
            CameraSim.RenderingEnabled = true;
            UpdateStatus();
        };
        var httpBtn = new Button { Text = "Kamera HTTP (timeout): vklopi/izklopi" };
        httpBtn.Clicked += (_, _) => CameraSim.SetHttpMode(!CameraSim.HttpMode);
        var catchBtn = new Button { Text = "catch (Exception) za HTTP: vklopi/izklopi" };
        catchBtn.Clicked += (_, _) => { CameraSim.HttpCatchTimeout = !CameraSim.HttpCatchTimeout; UpdateStatus(); };
        var tcpBtn = new Button { Text = "TCP pre-check (kot v moji kodi): vklopi/izklopi" };
        tcpBtn.Clicked += (_, _) => { CameraSim.TcpPrecheck = !CameraSim.TcpPrecheck; UpdateStatus(); };
        var churnBtn = new Button { Text = "Stres: odstranjuj/dodajaj canvase (simulator)" };
        churnBtn.Clicked += (_, _) => ToggleChurn();

        var prev = Preferences.Get("last_event", "(prvi zagon)");
        var header = new VerticalStackLayout
        {
            Padding = 8,
            Children =
            {
                new Label { Text = $"Prejšnja seja se je končala po: {prev}", FontSize = 11 },
                new Label { Text = "Zadnja izjema: " + Preferences.Get("last_exception", "(ni)"), FontSize = 10, MaxLines = 4 },
                _mode, _status, modeBtn, httpBtn, catchBtn, tcpBtn, churnBtn,
                new Label { Text = "Unsafe: pritiskaj Home / odpiraj App Switcher.", FontSize = 11 }
            }
        };

        var root = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        root.Add(header, 0, 0);
        root.Add(_grid, 0, 1);
        Content = new SafeAreaWrapper(root);

        CameraSim.Changed += () => MainThread.BeginInvokeOnMainThread(UpdateStatus);
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(500), () => { UpdateStatus(); return true; });
        CameraSim.Start();
        UpdateStatus();
    }

    void BuildCanvases()
    {
        _grid.Children.Clear();
        for (int i = 0; i < CameraSim.CameraCount; i++)
        {
            int id = i;
            var v = new SKCanvasView();
            v.PaintSurface += (_, e) => CameraSim.OnPaint(id, e);
            CameraSim.Views[id] = v;
            _grid.Add(v, id % 2, id / 2);
        }
    }

    void ToggleChurn()
    {
        if (_churn is not null) { _churn.Cancel(); _churn = null; return; }
        _churn = new CancellationTokenSource();
        var ct = _churn.Token;
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(60), () =>
        {
            if (ct.IsCancellationRequested) return false;
            BuildCanvases();   // stari native surface-i se uničijo, timerji še tečejo
            return true;
        });
    }

    void UpdateStatus()
    {
        _mode.Text = CameraSim.UnsafeMode ? "NAČIN: UNSAFE (vzorec iz crasha)" : "NAČIN: SAFE (popravek)";
        _mode.TextColor = CameraSim.UnsafeMode ? Colors.Red : Colors.Green;
        _status.Text = $"paints={CameraSim.Paints} swaps={CameraSim.Swaps} caught={CameraSim.Caught} last={CameraSim.LastEvent} http={CameraSim.HttpMode} catchAll={CameraSim.HttpCatchTimeout} tcpPre={CameraSim.TcpPrecheck} httpHandled={CameraSim.HttpHandled}";
    }

    sealed class SafeAreaWrapper : ContentView
    {
        public SafeAreaWrapper(View inner) { Content = inner; Padding = new Thickness(0, 40, 0, 0); }
    }
}
