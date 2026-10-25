using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using RealforceApcMouse.Mouse;
using RealforceApcMouse.Protocol;

namespace RealforceApcMouse.App;

/// <summary>
/// Draws the acceleration curve: X axis = key press depth (0 = released, 1 =
/// fully pressed), Y axis = resulting mouse speed in pixels per poll tick. It
/// reads a live <see cref="AccelerationCurve"/>, so redrawing after a slider
/// moves instantly reflects the new shape.
/// </summary>
public sealed class CurveChart : Panel
{
    private readonly AccelerationCurve _curve;
    public double PressDepth { get; set; }

    public CurveChart(AccelerationCurve curve)
    {
        _curve = curve;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(45, 45, 45);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width < 250 || ClientSize.Height < 190)
            return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        const int marginLeft = 54, marginRight = 16, marginTop = 16, marginBottom = 40;
        int plotW = Math.Max(1, ClientSize.Width - marginLeft - marginRight);
        int plotH = Math.Max(1, ClientSize.Height - marginTop - marginBottom);
        if (plotW <= 10 || plotH <= 10)
            return;

        int x0 = marginLeft, y0 = marginTop + plotH; // bottom-left origin
        double maxSpeed = Math.Max(1.0, _curve.MaxSpeed);

        using var gridPen = new Pen(Color.FromArgb(90, 90, 90));
        using var axisPen = new Pen(Color.FromArgb(160, 160, 160));
        using var curvePen = new Pen(Color.FromArgb(235, 35, 42), 2f);
        using var deadzoneBrush = new SolidBrush(Color.FromArgb(40, 235, 35, 42));
        using var textBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
        using var font = new Font(FontFamily.GenericSansSerif, 8f);

        // Grid + axis ticks.
        for (int i = 0; i <= 4; i++)
        {
            int gx = x0 + (int)(plotW * (i / 4.0));
            g.DrawLine(gridPen, gx, marginTop, gx, y0);
            string xl = (i / 4.0).ToString("0.00");
            var xs = g.MeasureString(xl, font);
            g.DrawString(xl, font, textBrush, gx - xs.Width / 2, y0 + 4);

            int gy = y0 - (int)(plotH * (i / 4.0));
            g.DrawLine(gridPen, x0, gy, x0 + plotW, gy);
            string yl = (maxSpeed * i / 4.0).ToString("0.#");
            var ys = g.MeasureString(yl, font);
            g.DrawString(yl, font, textBrush, x0 - ys.Width - 4, gy - ys.Height / 2);
        }

        // Deadzone band.
        double dz = Math.Clamp(_curve.Deadzone, 0.0, 1.0);
        if (dz > 0)
            g.FillRectangle(deadzoneBrush, x0, marginTop, (float)(plotW * dz), plotH);

        // Axes.
        g.DrawLine(axisPen, x0, marginTop, x0, y0);
        g.DrawLine(axisPen, x0, y0, x0 + plotW, y0);

        // The curve itself.
        PointF? prev = null;
        for (int px = 0; px <= plotW; px++)
        {
            double depth = px / (double)plotW;
            double speed = _curve.Evaluate(depth);
            float fy = y0 - (float)(plotH * (speed / maxSpeed));
            var pt = new PointF(x0 + px, fy);
            if (prev is PointF p)
                g.DrawLine(curvePen, p, pt);
            prev = pt;
        }

        // Live key-depth marker.
        double pressDepth = Math.Clamp(PressDepth, 0.0, 1.0);
        int markerX = x0 + (int)(plotW * pressDepth);
        using var markerPen = new Pen(Color.FromArgb(60, 220, 120), 2f);
        g.DrawLine(markerPen, markerX, marginTop, markerX, y0);

        // Axis titles.
        using var titleFont = new Font(FontFamily.GenericSansSerif, 8.5f, FontStyle.Bold);
        string xTitle = "Key press depth";
        var xts = g.MeasureString(xTitle, titleFont);
        g.DrawString(xTitle, titleFont, textBrush, x0 + (plotW - xts.Width) / 2, y0 + 20);

        string yTitle = "Mouse speed (px/tick)";
        var state = g.Save();
        g.TranslateTransform(14, marginTop + plotH / 2f);
        g.RotateTransform(-90);
        var yts = g.MeasureString(yTitle, titleFont);
        g.DrawString(yTitle, titleFont, textBrush, -yts.Width / 2, 0);
        g.Restore(state);
    }
}

public sealed class KeyStrokeMeter : Panel
{
    private double _depth;
    public double Depth { get => _depth; set { _depth = Math.Clamp(value, 0, 1); Invalidate(); } }

    public KeyStrokeMeter() { DoubleBuffered = true; BackColor = Color.FromArgb(20, 20, 20); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var text = new SolidBrush(Color.FromArgb(220, 220, 220));
        using var font = new Font("Segoe UI", 9f);
        e.Graphics.DrawString("Key Stroke Meter", font, text, 8, 6);
        string[] labels = { "0.0mm", "0.8mm", "1.5mm", "2.2mm", "3.0mm", "Full" };
        int top = 34;
        for (int i = 0; i < labels.Length; i++)
        {
            e.Graphics.DrawString(labels[i], font, text, 10, top + i * 25);
            int width = Math.Min(150, ClientSize.Width - 90);
            int barY = top + 4 + i * 25;
            if (width > 0 && barY >= 0 && barY < ClientSize.Height)
            {
                var rect = Rectangle.Intersect(new Rectangle(82, barY, width, 7), ClientRectangle);
                if (rect.Width > 0 && rect.Height > 0)
                {
                    try
                    {
                        using var background = new SolidBrush(Color.FromArgb(55, 55, 55));
                        using var gradient = new LinearGradientBrush(
                            rect,
                            Color.FromArgb(60, 120, 220),
                            Color.FromArgb(235, 35, 42),
                            LinearGradientMode.Horizontal);
                        e.Graphics.FillRectangle(background, rect);
                        int activeLevel = (int)Math.Round(_depth * (labels.Length - 1));
                        int level = i;
                        if (level <= activeLevel)
                        {
                            double fraction = activeLevel == 0 ? 1 : 1.0 - (double)level / (labels.Length - 1);
                            int alpha = (int)(90 + fraction * 165);
                            using var levelBrush = new SolidBrush(Color.FromArgb(alpha, 235, 35, 42));
                            e.Graphics.FillRectangle(levelBrush, rect);
                        }
                    }
                    catch (ArgumentException) { }
                }
            }
        }
        e.Graphics.DrawString(_depth > 0 ? $"{_depth * 100:0}%" : "OFF", font, text, 240, top + 2);
    }
}

public sealed class LedPreview : Panel
{
    public Func<(Color Color, bool Active)> StateProvider { get; set; } = () => (Color.Red, true);
    public bool Blink { get; set; }
    public int DurationMs { get; set; } = 1000;
    private readonly System.Windows.Forms.Timer _timer;
    private double _phase;

    public LedPreview()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        _timer = new System.Windows.Forms.Timer { Interval = 40 };
        _timer.Tick += (_, _) => { _phase += 40.0 / Math.Max(100, DurationMs); if (_phase >= 1) _phase -= 1; Invalidate(); };
        _timer.Start();
        Disposed += (_, _) => _timer.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var state = StateProvider();
        double glow = !state.Active ? 0.12 : Blink ? (_phase < 0.5 ? 1 : 0.08) : 0.25 + 0.75 * ((Math.Sin(_phase * Math.PI * 2) + 1) / 2);
        var c = Color.FromArgb((int)(state.Color.R * glow), (int)(state.Color.G * glow), (int)(state.Color.B * glow));
        using var housingPen = new Pen(Color.FromArgb(150, 150, 150), 2f);
        int size = Math.Min(Math.Max(1, Width - 40), Math.Max(1, Height - 40));
        int x = (Width - size) / 2;
        int y = (Height - size) / 2;
        e.Graphics.DrawEllipse(housingPen, x, y, size, size);
        using var brush = new SolidBrush(c);
        e.Graphics.FillEllipse(brush, x + 3, y + 3, Math.Max(1, size - 6), Math.Max(1, size - 6));
        using var pen = new Pen(Color.FromArgb(100, state.Color), 1);
        e.Graphics.DrawEllipse(pen, 20, 20, Math.Max(1, Width - 40), Math.Max(1, Height - 40));
    }
}

/// <summary>
/// The mouse-settings window: sliders for speed, acceleration curve, deadzone,
/// scroll speed, and LED effects, with a live chart of press-depth vs mouse-
/// speed. Edits the live <see cref="AccelerationCurve"/>/<see cref="MouseModeOptions"/>
/// so a running session updates immediately, and Save persists to settings.json.
/// </summary>
public sealed class SettingsForm : Form
{
    private static readonly Color PageColor = Color.FromArgb(48, 48, 48);
    private static readonly Color PanelColor = Color.FromArgb(20, 20, 20);
    private static readonly Color CardColor = Color.FromArgb(62, 62, 62);
    private static readonly Color TextColor = Color.FromArgb(235, 235, 235);
    private static readonly Color MutedColor = Color.FromArgb(175, 175, 175);
    private static readonly Color AccentColor = Color.FromArgb(235, 35, 42);

    private readonly MouseSettings _settings;
    private readonly AccelerationCurve _curve;
    private readonly MouseModeOptions? _options;
    private readonly Action<MouseSettings>? _onSaved;
    private Panel? _contentPanel;
    private Button[]? _navButtons;
    private readonly Panel _mousePage;
    private readonly Panel _behaviorPage;
    private readonly Panel _keyPage;
    private readonly Panel _ledPage;
    private readonly CurveChart _chart;
    private readonly KeyStrokeMeter _strokeMeter;
    private LedPreview? _ledPreview;
    private readonly R3MemoryMapClient? _client;
    private readonly System.Windows.Forms.Timer? _strokeTimer;

    private readonly TrackBar _maxSpeed;
    private readonly TrackBar _curveExp;
    private readonly TrackBar _deadzone;
    private readonly TrackBar _scroll;
    private readonly ComboBox _curveShape;
    private CheckBox? _disableLed;
    private readonly Label _maxSpeedVal;
    private readonly Label _curveExpVal;
    private readonly Label _deadzoneVal;
    private readonly Label _scrollVal;
    private readonly Label _hint;
    private readonly Button _save;
    private readonly Button _close;
    private Button? _captureButton;

    public SettingsForm(
        MouseSettings settings,
        AccelerationCurve curve,
        MouseModeOptions? options,
        Action<MouseSettings>? onSaved,
        R3MemoryMapClient? client = null)
    {
        _settings = settings;
        _curve = curve;
        _options = options;
        _onSaved = onSaved;
        _client = client;

        Text = "FAKEFORCE | Mouse Mode";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(780, 500);
        ClientSize = new Size(1100, 720);
        BackColor = PageColor;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 10f);
        KeyPreview = true;

        var header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = PanelColor };
        var brand = new Label
        {
            Text = "REAL",
            ForeColor = TextColor,
            Font = new Font("Segoe UI", 16f, FontStyle.Strikeout),
            Location = new Point(28, 18),
            AutoSize = true,
        };
        header.Controls.Add(brand);
        header.Controls.Add(new Label
        {
            Text = "REAL",
            ForeColor = TextColor,
            Font = new Font("Segoe UI", 16f, FontStyle.Strikeout),
            Location = new Point(28, 18),
            AutoSize = true,
        });
        header.Controls.Add(new Label
        {
            Text = "FaKe",
            ForeColor = TextColor,
            Font = new Font("Comic Sans MS", 16f, FontStyle.Bold),
            Location = new Point(91, 18),
            AutoSize = true,
        });
        header.Controls.Add(new Label
        {
            Text = "FORCE  |  MOUSE MODE",
            ForeColor = TextColor,
            Font = new Font("Segoe UI", 16f, FontStyle.Regular),
            Location = new Point(151, 18),
            AutoSize = true,
        });
        Controls.Add(header);

        _contentPanel = new Panel
        {
            Location = new Point(205, 66),
            Size = new Size(895, 606),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = PageColor,
        };
        Controls.Add(_contentPanel);

        _mousePage = new Panel { BackColor = PageColor, Dock = DockStyle.Fill, Visible = true };
        _behaviorPage = new Panel { BackColor = PageColor, Dock = DockStyle.Fill, Visible = false };
        _keyPage = new Panel { BackColor = PageColor, Dock = DockStyle.Fill, Visible = false };
        _ledPage = new Panel { BackColor = PageColor, Dock = DockStyle.Fill, Visible = false };

        _contentPanel.Controls.Add(_mousePage);
        _contentPanel.Controls.Add(_behaviorPage);
        _contentPanel.Controls.Add(_keyPage);
        _contentPanel.Controls.Add(_ledPage);
        _mousePage.BringToFront();
        UpdateNavForPages();

        var navigation = new Panel { Dock = DockStyle.Left, Width = 205, BackColor = PanelColor };
        var navMouse = CreateNavButton("MOUSE MODE", 0, true);
        var navSens = CreateNavButton("SENSITIVITY", 1, false);
        var navKeys = CreateNavButton("KEY ASSIGNMENT", 2, false);
        var navLed = CreateNavButton("LED EFFECTS", 3, false);
        navMouse.Click += (_, _) => { _mousePage.Visible = true; _behaviorPage.Visible = false; _keyPage.Visible = false; _ledPage.Visible = false; UpdateNavForPages(); };
        navSens.Click += (_, _) => { _mousePage.Visible = false; _behaviorPage.Visible = true; _keyPage.Visible = false; _ledPage.Visible = false; _behaviorPage.BringToFront(); LayoutBehaviorPage(); UpdateNavForPages(); };
        navKeys.Click += (_, _) => { _mousePage.Visible = false; _behaviorPage.Visible = false; _keyPage.Visible = true; _ledPage.Visible = false; UpdateNavForPages(); };
        navLed.Click += (_, _) => { _mousePage.Visible = false; _behaviorPage.Visible = false; _keyPage.Visible = false; _ledPage.Visible = true; UpdateNavForPages(); };
        navigation.Controls.AddRange(new Control[] { navMouse, navSens, navKeys, navLed });
        Controls.Add(navigation);
        _navButtons = new[] { navMouse, navSens, navKeys, navLed };
        UpdateNavForPages();

        _chart = new CurveChart(_curve)
        {
            Location = new Point(400, 16),
            Size = new Size(444, 388),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = CardColor,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
        _behaviorPage.Controls.Add(_chart);
        _chart.Location = new Point(400, 16);
        _chart.Margin = Padding.Empty;
        _chart.BringToFront();
        _strokeMeter = new KeyStrokeMeter { Location = new Point(400, 420), Size = new Size(300, 190), Anchor = AnchorStyles.Top | AnchorStyles.Left };
        _behaviorPage.Controls.Add(_strokeMeter);
        if (_client is not null)
        {
            _strokeTimer = new System.Windows.Forms.Timer { Interval = 40 };
            _strokeTimer.Tick += (_, _) =>
            {
                try
                {
                    double depth = KeyStatusReader.Read(_client, 31).NormalizedDepth();
                    _strokeMeter.Depth = depth;
                    _chart.PressDepth = depth;
                    _chart.Invalidate();
                }
                catch { _strokeMeter.Depth = 0; _chart.PressDepth = 0; _chart.Invalidate(); }
            };
            _strokeTimer.Start();
        }

        int y = 20;
        (_maxSpeed, _maxSpeedVal) = AddSlider("Max speed (px/tick)", 1, 60,
            (int)Math.Round(_settings.MaxSpeed), ref y, OnMaxSpeed);
        (_curveExp, _curveExpVal) = AddSlider("Acceleration curve", 20, 1000,
            (int)Math.Round(_settings.CurveExponent * 100), ref y, OnCurve);
        (_deadzone, _deadzoneVal) = AddSlider("Deadzone", 0, 50,
            (int)Math.Round(_settings.Deadzone * 100), ref y, OnDeadzone);
        (_scroll, _scrollVal) = AddSlider("Scroll speed (notch/s)", 1, 60,
            (int)Math.Round(_settings.ScrollSpeed), ref y, OnScroll);

        _behaviorPage.Controls.Add(new Label
        {
            Text = "CURVE SHAPE",
            Location = new Point(20, y + 4),
            AutoSize = true,
            ForeColor = MutedColor,
        });
        _curveShape = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(150, y),
            Width = 130,
            BackColor = CardColor,
            ForeColor = TextColor,
            FlatStyle = FlatStyle.Flat,
        };
        _curveShape.Items.AddRange(new object[] { "Power", "S-curve" });
        _curveShape.SelectedIndex = _settings.UseSCurve ? 1 : 0;
        _curveShape.SelectedIndexChanged += (_, _) =>
        {
            _settings.UseSCurve = _curveShape.SelectedIndex == 1;
            _curve.UseSCurve = _settings.UseSCurve;
            _chart.Invalidate();
        };
        _behaviorPage.Controls.Add(_curveShape);
        _curve.UseSCurve = _settings.UseSCurve;
        _hint = new Label
        {
            ForeColor = MutedColor,
            Text = "POWER: exponent controls slow-start / fast-finish.\n" +
                   "S-curve: exponent controls the middle ramp.\n" +
                   "Curve < 1.0 = fast start. Deadzone ignores light\n" +
                   "touches so the cursor doesn't drift.",
        };
        _behaviorPage.Controls.Add(_hint);

        BuildKeyAssignmentPage();
        BuildLedEffectsPage();
        BuildMouseModeInfoPage();

        _save = new Button
        {
            Text = "SAVE",
            Size = new Size(120, 34),
            BackColor = AccentColor,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        _save.Click += (_, _) => SaveSettings();
        Controls.Add(_save);

        _close = new Button
        {
            Text = "CLOSE",
            Size = new Size(120, 34),
            BackColor = CardColor,
            ForeColor = TextColor,
            FlatStyle = FlatStyle.Flat,
        };
        _close.Click += (_, _) => Close();
        Controls.Add(_close);

        KeyDown += CaptureKeyDown;
        KeyPreview = true;
        KeyUp += (_, e) => { if (e.KeyCode == Keys.S) _strokeMeter.Depth = 0; };
        KeyPress += (_, e) => { if (e.KeyChar is 's' or 'S') _strokeMeter.Depth = 0.5; };
        Resize += (_, _) => LayoutControls();
        Shown += (_, _) =>
        {
            BeginInvoke(LayoutControls);
        };
        LayoutControls();

        RefreshLabels();
    }

    private Button CreateNavButton(string text, int index, bool selected)
        => new()
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0, MouseOverBackColor = Color.Transparent, MouseDownBackColor = Color.Transparent },
            BackColor = PanelColor,
            ForeColor = selected ? AccentColor : TextColor,
            Font = new Font("Segoe UI", 9.5f, selected ? FontStyle.Bold : FontStyle.Regular),
            Location = new Point(10, 36 + index * 46),
            Size = new Size(185, 36),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(18, 0, 0, 0),
            Cursor = Cursors.Hand,
            TabStop = false,
        };

    private void UpdateNavForPages()
    {
        if (_navButtons is null) return;
        int idx = 0;
        if (_mousePage?.Visible == true) idx = 0;
        else if (_behaviorPage?.Visible == true) idx = 1;
        else if (_keyPage?.Visible == true) idx = 2;
        else if (_ledPage?.Visible == true) idx = 3;
        for (int i = 0; i < _navButtons.Length; i++)
        {
            _navButtons[i].ForeColor = i == idx ? AccentColor : TextColor;
            _navButtons[i].Font = new Font("Segoe UI", 9.5f, i == idx ? FontStyle.Bold : FontStyle.Regular);
        }
    }

    private (TrackBar, Label) AddSlider(string caption, int min, int max, int value,
        ref int y, EventHandler onChanged)
    {
        var title = new Label { Text = caption.ToUpperInvariant(), Location = new Point(20, y), AutoSize = true, ForeColor = MutedColor };
        _behaviorPage.Controls.Add(title);

        var valueLabel = new Label
        {
            Location = new Point(210, y),
            Size = new Size(70, 18),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = TextColor,
        };
        _behaviorPage.Controls.Add(valueLabel);

        var bar = new TrackBar
        {
            Location = new Point(16, y + 20),
            Size = new Size(268, 45),
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            TickStyle = TickStyle.None,
            BackColor = PageColor,
        };
        bar.Scroll += onChanged;
        _behaviorPage.Controls.Add(bar);

        y += 74;
        return (bar, valueLabel);
    }

    private void OnMaxSpeed(object? s, EventArgs e)
    {
        _settings.MaxSpeed = _maxSpeed.Value;
        _curve.MaxSpeed = _settings.MaxSpeed;
        RefreshLabels();
    }

    private void OnCurve(object? s, EventArgs e)
    {
        _settings.CurveExponent = _curveExp.Value / 100.0;
        _curve.CurveExponent = _settings.CurveExponent;
        RefreshLabels();
    }

    private void OnDeadzone(object? s, EventArgs e)
    {
        _settings.Deadzone = _deadzone.Value / 100.0;
        _curve.Deadzone = _settings.Deadzone;
        RefreshLabels();
    }

    private void OnScroll(object? s, EventArgs e)
    {
        _settings.ScrollSpeed = _scroll.Value;
        if (_options is not null)
            _options.ScrollNotchesPerSecond = _settings.ScrollSpeed;
        RefreshLabels();
    }

    private void RefreshLabels()
    {
        _maxSpeedVal.Text = _settings.MaxSpeed.ToString("0");
        _curveExpVal.Text = _settings.CurveExponent.ToString("0.00");
        _deadzoneVal.Text = _settings.Deadzone.ToString("0.00");
        _scrollVal.Text = _settings.ScrollSpeed.ToString("0");
        _chart.Invalidate();
    }

    private void LayoutControls()
    {
        int left = 205;
        int top = 66;
        int footerHeight = 48;
        int footerY = ClientSize.Height - footerHeight;
        int rightMargin = 16;
        int width = Math.Max(200, ClientSize.Width - left - rightMargin);
        int height = Math.Max(200, footerY - top - 8);
        if (Controls.OfType<Panel>().FirstOrDefault(p => p.Dock == DockStyle.Left && p.Width == 205) is Panel nav)
        {
            // find content panel
        }
        if (_contentPanel is not null)
            _contentPanel.Size = new Size(width, height);
        int buttonsWidth = _save.Width + 10 + _close.Width;
        int buttonsLeft = (ClientSize.Width - buttonsWidth) / 2;
        _save.Location = new Point(buttonsLeft, footerY + 7);
        _close.Location = new Point(_save.Right + 10, footerY + 7);
        LayoutBehaviorPage();
    }

    private void LayoutBehaviorPage()
    {
        if (!_behaviorPage.Visible)
            return;
        const int margin = 16;
        int pageWidth = _behaviorPage.ClientSize.Width;
        int pageHeight = _behaviorPage.ClientSize.Height;
        int leftWidth = 380;
        int chartLeft = 400;

        _chart.Location = new Point(chartLeft, 16);
        _chart.Size = new Size(400, 390);
        _chart.Anchor = AnchorStyles.None;
        _chart.Dock = DockStyle.None;

        foreach (var bar in new[] { _maxSpeed, _curveExp, _deadzone, _scroll })
            bar.Width = leftWidth - 2 * margin;

        foreach (var label in new[] { _maxSpeedVal, _curveExpVal, _deadzoneVal, _scrollVal })
            label.Left = leftWidth - margin - label.Width;

        int hintHeight = Math.Max(60, _hint.PreferredHeight);
        _hint.Size = new Size(leftWidth - 2 * margin, hintHeight);
        _hint.Location = new Point(margin, 390);
    }

    private void BuildKeyAssignmentPage()
    {
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(14),
        };
        _keyPage.Controls.Add(content);

        var movement = CreateGroup("Analog movement", 790, 142);
        AddMovementRow(movement, "Up", _settings.Up, _settings.UpKey,
            slot => { _settings.Up = slot; if (_options is not null) _options.UpIndex = slot; },
            key => { _settings.UpKey = key; if (_options is not null) _options.UpKey = key; });
        AddMovementRow(movement, "Down", _settings.Down, _settings.DownKey,
            slot => { _settings.Down = slot; if (_options is not null) _options.DownIndex = slot; },
            key => { _settings.DownKey = key; if (_options is not null) _options.DownKey = key; });
        AddMovementRow(movement, "Left", _settings.Left, _settings.LeftKey,
            slot => { _settings.Left = slot; if (_options is not null) _options.LeftIndex = slot; },
            key => { _settings.LeftKey = key; if (_options is not null) _options.LeftKey = key; });
        AddMovementRow(movement, "Right", _settings.Right, _settings.RightKey,
            slot => { _settings.Right = slot; if (_options is not null) _options.RightIndex = slot; },
            key => { _settings.RightKey = key; if (_options is not null) _options.RightKey = key; });
        content.Controls.Add(movement);

        var actions = CreateGroup("Mouse actions", 790, 142);
        AddKeyRow(actions, "Left click", _settings.LeftClickKey,
            key => { _settings.LeftClickKey = key; if (_options is not null) _options.LeftClickKey = key; }, 8);
        AddKeyRow(actions, "Right click", _settings.RightClickKey,
            key => { _settings.RightClickKey = key; if (_options is not null) _options.RightClickKey = key; }, 38);
        AddKeyRow(actions, "Scroll up", _settings.ScrollUpKey,
            key => { _settings.ScrollUpKey = key; if (_options is not null) _options.ScrollUpKey = key; }, 68);
        AddKeyRow(actions, "Scroll down", _settings.ScrollDownKey,
            key => { _settings.ScrollDownKey = key; if (_options is not null) _options.ScrollDownKey = key; }, 98);
        content.Controls.Add(actions);

        var activation = CreateGroup("Activation", 790, 174);
        var mode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(150, 22),
            Width = 220,
        };
        mode.Items.AddRange(new object[] { "Press once to toggle", "Hold to activate" });
        mode.SelectedIndex = _settings.HoldToActivate ? 1 : 0;
        mode.SelectedIndexChanged += (_, _) =>
        {
            _settings.HoldToActivate = mode.SelectedIndex == 1;
            if (_options is not null) _options.HoldToActivate = _settings.HoldToActivate;
        };
        activation.Controls.Add(new Label { Text = "Activation behavior", Location = new Point(12, 26), AutoSize = true });
        activation.Controls.Add(mode);

        var useFn = new CheckBox
        {
            Text = "Require Fn analog slot",
            Checked = _settings.UseFnActivation,
            Location = new Point(12, 60),
            AutoSize = true,
        };
        useFn.CheckedChanged += (_, _) =>
        {
            _settings.UseFnActivation = useFn.Checked;
            if (_options is not null) _options.UseFnActivation = useFn.Checked;
        };
        activation.Controls.Add(useFn);

        var fnSlot = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 127,
            Value = _settings.Fn ?? 69,
            Location = new Point(150, 58),
            Width = 72,
        };
        fnSlot.ValueChanged += (_, _) =>
        {
            _settings.Fn = (int)fnSlot.Value;
            if (_options is not null) _options.FnIndex = _settings.Fn;
        };
        activation.Controls.Add(new Label { Text = "Fn slot", Location = new Point(230, 62), AutoSize = true });
        activation.Controls.Add(fnSlot);

        AddKeyRow(activation, "Activation key 1", _settings.ActivationKey1,
            key => { _settings.ActivationKey1 = key; if (_options is not null) _options.ActivationKey1 = key; }, 92);
        AddKeyRow(activation, "Activation key 2", _settings.ActivationKey2,
            key => { _settings.ActivationKey2 = key; if (_options is not null) _options.ActivationKey2 = key; }, 122,
            allowNone: true);
        content.Controls.Add(activation);

        content.Controls.Add(new Label
        {
            Text = "Movement uses the analog slot number and the captured keyboard key must be the same physical key. " +
                   "Use slot 0-127 from `scan`; leave Activation key 2 empty for a single-key trigger.",
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = Color.DimGray,
            Margin = new Padding(4, 8, 4, 10),
        });
    }

    private static GroupBox CreateGroup(string title, int width, int height)
        => new() { Text = title, Width = width, Height = height, Margin = new Padding(0, 0, 0, 10) };

    private void AddMovementRow(GroupBox group, string caption, int slot, int key,
        Action<int> slotChanged, Action<int> keyChanged)
    {
        int y = 24 + group.Controls.OfType<Label>().Count() * 28;
        group.Controls.Add(new Label { Text = caption, Location = new Point(12, y + 4), Width = 90 });
        var slotBox = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 127,
            Value = Math.Clamp(slot, 0, 127),
            Location = new Point(110, y),
            Width = 72,
        };
        slotBox.ValueChanged += (_, _) => slotChanged((int)slotBox.Value);
        group.Controls.Add(slotBox);
        AddCaptureButton(group, key, keyChanged, new Point(200, y - 1));
    }

    private void AddKeyRow(GroupBox group, string caption, int key, Action<int> changed, int y, bool allowNone = false)
    {
        group.Controls.Add(new Label { Text = caption, Location = new Point(12, y + 4), Width = 130 });
        AddCaptureButton(group, key, changed, new Point(150, y - 1), allowNone);
    }

    private void AddCaptureButton(Control parent, int key, Action<int> changed, Point location, bool allowNone = false)
    {
        var button = new Button
        {
            Text = key == 0 && allowNone ? "None" : KeyName(key),
            Location = location,
            Size = new Size(150, 26),
            Tag = (key, allowNone, changed),
        };
        button.Click += (_, _) =>
        {
            _captureButton = button;
            button.Text = "Press a key...";
            Activate();
        };
        parent.Controls.Add(button);
    }

    private void CaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (_captureButton?.Tag is not ValueTuple<int, bool, Action<int>> capture)
            return;

        int key = e.KeyCode == Keys.Escape && capture.Item2 ? 0 : GetCapturedVirtualKey(e.KeyCode);
        if (key == 0 && !capture.Item2)
            return;

        capture.Item3(key);
        _captureButton.Text = key == 0 ? "None" : KeyName(key);
        _captureButton = null;
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private static int GetCapturedVirtualKey(Keys key)
    {
        if (key is Keys.ShiftKey or Keys.ControlKey or Keys.Menu)
        {
            int left = key switch { Keys.ShiftKey => 0xA0, Keys.ControlKey => 0xA2, _ => 0xA4 };
            int right = key switch { Keys.ShiftKey => 0xA1, Keys.ControlKey => 0xA3, _ => 0xA5 };
            if ((GetAsyncKeyState(right) & 0x8000) != 0) return right;
            if ((GetAsyncKeyState(left) & 0x8000) != 0) return left;
        }
        return (int)key;
    }

    private static string KeyName(int key)
        => Enum.IsDefined(typeof(Keys), key) ? ((Keys)key).ToString() : $"VK {key:X2}";

    private void BuildMouseModeInfoPage()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = PageColor,
            Padding = new Padding(40),
        };
        var title = new Label
        {
            Text = "MOUSE MODE",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = TextColor,
            Location = new Point(40, 40),
            AutoSize = true,
        };
        var desc = new Label
        {
            Text = "Use the analog keys to control mouse movement. Configure sensitivity, curve, and key bindings in the other tabs. Use the tray icon to toggle mouse mode.",
            Location = new Point(40, 90),
            Size = new Size(700, 60),
            ForeColor = MutedColor,
        };
        panel.Controls.Add(title);
        panel.Controls.Add(desc);
        _mousePage.Controls.Add(panel);
    }

    private void BuildLedEffectsPage()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = PageColor,
            AutoScroll = true,
            Padding = new Padding(40),
        };
        var title = new Label
        {
            Text = "LED EFFECTS",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = TextColor,
            Location = new Point(40, 40),
            AutoSize = true,
        };
        _disableLed = new CheckBox
        {
            Text = "Enable runtime LED effects",
            Checked = !_settings.DisableLedEffects,
            Location = new Point(40, 95),
            AutoSize = true,
            ForeColor = TextColor,
        };
        _disableLed.CheckedChanged += (_, _) => _settings.DisableLedEffects = !_disableLed.Checked;

        int y = 140;
        var capsGroup = new GroupBox { Text = "Caps Lock", Location = new Point(40, y), Size = new Size(700, 145), ForeColor = TextColor, BackColor = PageColor };
        var capsR = AddColorRow(capsGroup, "Red", 24, (v) => _settings.CapsLockLedR = (byte)v);
        var capsG = AddColorRow(capsGroup, "Green", 50, (v) => _settings.CapsLockLedG = (byte)v);
        var capsB = AddColorRow(capsGroup, "Blue", 76, (v) => _settings.CapsLockLedB = (byte)v);
        capsR.Value = _settings.CapsLockLedR;
        capsG.Value = _settings.CapsLockLedG;
        capsB.Value = _settings.CapsLockLedB;
        var capsDur = new NumericUpDown { Location = new Point(380, 28), Size = new Size(70, 20), Minimum = 50, Maximum = 2000, Value = _settings.CapsLockBlinkDurationMs };
        capsGroup.Controls.Add(new Label { Text = "Blink duration (ms)", Location = new Point(230, 30), AutoSize = true, ForeColor = TextColor });
        capsGroup.Controls.Add(capsDur);
        panel.Controls.Add(capsGroup);
        y += 165;

        var mouseGroup = new GroupBox { Text = "Mouse Mode", Location = new Point(40, y), Size = new Size(700, 165), ForeColor = TextColor, BackColor = PageColor };
        var pattern = new ComboBox { Location = new Point(150, 28), Size = new Size(120, 20), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = CardColor, ForeColor = TextColor };
        pattern.Items.AddRange(new object[] { "Cycle", "Static" });
        pattern.SelectedItem = _settings.MouseModeLedPattern;
        pattern.SelectedIndexChanged += (_, _) => _settings.MouseModeLedPattern = pattern.SelectedItem?.ToString() ?? "Cycle";
        mouseGroup.Controls.Add(new Label { Text = "Pattern", Location = new Point(24, 30), AutoSize = true, ForeColor = TextColor });
        mouseGroup.Controls.Add(pattern);
        var mouseR = AddColorRow(mouseGroup, "Red", 70, (v) => _settings.MouseModeLedR = (byte)v);
        var mouseG = AddColorRow(mouseGroup, "Green", 96, (v) => _settings.MouseModeLedG = (byte)v);
        var mouseB = AddColorRow(mouseGroup, "Blue", 122, (v) => _settings.MouseModeLedB = (byte)v);
        mouseR.Value = _settings.MouseModeLedR;
        mouseG.Value = _settings.MouseModeLedG;
        mouseB.Value = _settings.MouseModeLedB;
        var mouseDur = new NumericUpDown { Location = new Point(380, 74), Size = new Size(70, 20), Minimum = 1000, Maximum = 20000, Value = _settings.MouseModeLedDurationMs };
        mouseGroup.Controls.Add(new Label { Text = "Cycle duration (ms)", Location = new Point(230, 76), AutoSize = true, ForeColor = TextColor });
        mouseGroup.Controls.Add(mouseDur);
        panel.Controls.Add(mouseGroup);

        var capsPreview = new LedPreview { Location = new Point(570, 36), Size = new Size(90, 90) };
        capsPreview.StateProvider = () =>
        {
            var c = Color.FromArgb(_settings.CapsLockLedR * 255 / 100, _settings.CapsLockLedG * 255 / 100, _settings.CapsLockLedB * 255 / 100);
            return (c, !_settings.DisableLedEffects);
        };
        capsPreview.Blink = true;
        capsPreview.DurationMs = _settings.CapsLockBlinkDurationMs;
        var mousePreview = new LedPreview { Location = new Point(570, 46), Size = new Size(90, 90) };
        mousePreview.StateProvider = () =>
        {
            var c = Color.FromArgb(_settings.MouseModeLedR * 255 / 100, _settings.MouseModeLedG * 255 / 100, _settings.MouseModeLedB * 255 / 100);
            return (c, !_settings.DisableLedEffects);
        };
        mousePreview.DurationMs = _settings.MouseModeLedDurationMs;
        var capsPreviewTitle = new Label { Text = "CAPS LOCK PREVIEW", Location = new Point(540, 18), AutoSize = true, ForeColor = TextColor, BackColor = PageColor };
        var mousePreviewTitle = new Label { Text = "MOUSE MODE PREVIEW", Location = new Point(525, 18), AutoSize = true, ForeColor = TextColor, BackColor = PageColor };
        capsGroup.Controls.Add(capsPreviewTitle);
        mouseGroup.Controls.Add(mousePreviewTitle);
        capsGroup.Controls.Add(capsPreview);
        mouseGroup.Controls.Add(mousePreview);
        capsGroup.Controls.SetChildIndex(capsPreviewTitle, 0);
        mouseGroup.Controls.SetChildIndex(mousePreviewTitle, 0);
        void UpdatePreview()
        {
            _settings.CapsLockBlinkDurationMs = (int)capsDur.Value;
            _settings.MouseModeLedDurationMs = (int)mouseDur.Value;
            capsPreview.DurationMs = _settings.CapsLockBlinkDurationMs;
            mousePreview.DurationMs = _settings.MouseModeLedDurationMs;
            mousePreview.Blink = false;
            capsPreview.Invalidate();
            mousePreview.Invalidate();
        }
        capsDur.ValueChanged += (_, _) => UpdatePreview();
        mouseDur.ValueChanged += (_, _) => UpdatePreview();
        pattern.SelectedIndexChanged += (_, _) => UpdatePreview();
        mouseDur.ValueChanged += (_, _) => UpdatePreview();
        mouseR.ValueChanged += (_, _) => UpdatePreview();
        mouseG.ValueChanged += (_, _) => UpdatePreview();
        mouseB.ValueChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        panel.Controls.Add(title);
        panel.Controls.Add(_disableLed);
        _ledPage.Controls.Add(panel);
    }

    private NumericUpDown AddColorRow(GroupBox group, string label, int y, Action<int> set)
    {
        group.Controls.Add(new Label { Text = label, Location = new Point(24, y + 2), AutoSize = true, ForeColor = TextColor });
        var num = new NumericUpDown { Location = new Point(90, y), Size = new Size(60, 20), Minimum = 0, Maximum = 100 };
        num.ValueChanged += (_, _) => set((int)num.Value);
        group.Controls.Add(num);
        return num;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private void SaveSettings()
    {
        try
        {
            SettingsStore.Save(_settings);
            _onSaved?.Invoke(_settings);
                MessageBox.Show(this, $"Saved to:\n{SettingsStore.FilePath}", "FAKEFORCE",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save settings:\n{SettingsStore.FilePath}", "FAKEFORCE",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
