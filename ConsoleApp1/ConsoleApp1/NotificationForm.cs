using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;

namespace ConsoleApp1;

public enum NotificationType { Info, Warning, Error, Permission }

public class NotificationForm : Form
{
    private static readonly Font SystemFont = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

    private readonly string _title = null!;
    private readonly string _message = null!;
    private readonly NotificationType _type;
    private readonly int _timeoutSeconds;
    private readonly string[] _actions = null!;
    private readonly bool _playSound;

    // Controls
    private Label _titleLabel = null!;
    private Label _messageLabel = null!;
    private Button[] _actionButtons = [];
    private Label _closeLabel = null!;
    private Panel _accentPanel = null!;

    // Timers
    private readonly System.Windows.Forms.Timer _closeTimer = new();
    private readonly System.Windows.Forms.Timer _slideTimer = new();
    private readonly System.Windows.Forms.Timer _fadeTimer = new();

    // Animation state
    private int _slideStep;
    private int _slideStartX;
    private int _slideTargetX;
    private int _fadeStep;
    private bool _isClosing;
    private bool _mouseInside;
    private bool _actionClicked;
    private readonly ToolTip _toolTip = new();

    // Sizing
    private const int FormWidth = 380;
    private const int FormHeight = 148;
    private const int MinFormWidth = 320;
    private const int MaxFormWidth = 600;
    private const int EdgeMargin = 16;
    private const int AccentWidth = 6;
    private const int LayoutPad = 14;
    private const int CloseBtnSize = 18;
    private int _formWidth = FormWidth;
    private string? _resultFilePath;

    // Colors per notification type
    private static readonly Dictionary<NotificationType, Color> ThemeColor = new()
    {
        [NotificationType.Info] = Color.FromArgb(0x00, 0x78, 0xD4),
        [NotificationType.Warning] = Color.FromArgb(0xFF, 0x8C, 0x00),
        [NotificationType.Error] = Color.FromArgb(0xE8, 0x11, 0x23),
        [NotificationType.Permission] = Color.FromArgb(0x8C, 0x4B, 0x9E),
    };

    // Unicode icons per type
    private static readonly Dictionary<NotificationType, (char Icon, string Label)> TypeIcon = new()
    {
        [NotificationType.Info] = ('i', "信息"),
        [NotificationType.Warning] = ('!', "警告"),
        [NotificationType.Error] = ('✖', "错误"),
        [NotificationType.Permission] = ('⚿', "权限"),
    };

    public NotificationForm(string title, string message, NotificationType type,
        int timeoutSeconds, string[]? actions, bool playSound, string? resultFile) : this()
    {
        _title = title;
        _message = message;
        _type = type;
        _timeoutSeconds = timeoutSeconds;
        _actions = actions ?? ["查看", "忽略"];
        _playSound = playSound;
        _resultFilePath = resultFile;

        InitializeForm();
        SetupControls();
        PositionAtBottomRight();
        StartSlideIn();

        if (_playSound) PlayNotificationSound();
    }

    // ── Win32: no activation, tool window, drop shadow ──
    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_NOACTIVATE = 0x08000000;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_TOPMOST = 0x00000008;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    // ── Form-level double-buffering ──
    public NotificationForm()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
    }

    private void InitializeForm()
    {
        Text = "Claude Code Notification";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        _formWidth = FormWidth;
        Size = new Size(_formWidth, FormHeight);
        MaximumSize = new Size(MaxFormWidth, FormHeight);
        BackColor = Color.White;
        ShowInTaskbar = false;
        TopMost = true;
        Cursor = Cursors.Default;

        // Drop shadow is handled via CreateParams override (CS_DROPSHADOW = 0x00020000)

        // Close timer
        if (_timeoutSeconds > 0)
        {
            _closeTimer.Interval = 1000;
            _closeTimer.Tick += CloseTimerTick;
            _closeTimer.Start();
        }

        // Mouse hover tracking
        MouseEnter += (_, _) => { _mouseInside = true; };
        MouseLeave += (_, _) => { _mouseInside = false; };
    }

    private void SetupControls()
    {
        var themeColor = ThemeColor.GetValueOrDefault(_type, ThemeColor[NotificationType.Info]);

        // ── Calculate required form width from button text ──
        var measureFont = new Font(SystemFont.FontFamily, 9);
        int totalBtnWidth = 0;
        int maxBtnWidth = 0;
        var btnSizes = new (string Label, string Value, int Width)[_actions.Length];
        for (int i = 0; i < _actions.Length; i++)
        {
            string label = _actions[i], value = _actions[i];
            int pipeIdx = _actions[i].IndexOf('|');
            if (pipeIdx >= 0)
            {
                label = _actions[i].Substring(0, pipeIdx).Trim();
                value = _actions[i].Substring(pipeIdx + 1).Trim();
            }
            int w = Math.Max(72, TextRenderer.MeasureText(label, measureFont).Width + 20);
            btnSizes[i] = (label, value, w);
            totalBtnWidth += w;
            if (w > maxBtnWidth) maxBtnWidth = w;
        }
        // Cap very wide buttons at 160px; extra width is distributed
        for (int i = 0; i < btnSizes.Length; i++)
        {
            if (btnSizes[i].Width > 160)
                btnSizes[i] = (btnSizes[i].Label, btnSizes[i].Value, 160);
        }
        int neededWidth = AccentWidth + LayoutPad + totalBtnWidth + (_actions.Length - 1) * 8 + LayoutPad + 4;
        _formWidth = Math.Clamp(neededWidth, MinFormWidth, MaxFormWidth);
        Size = new Size(_formWidth, FormHeight);

        // ── Rounded corners (rebuild with new width) ──
        using var path = new GraphicsPath();
        path.AddArc(0, 0, 16, 16, 180, 90);
        path.AddArc(_formWidth - 16, 0, 16, 16, 270, 90);
        path.AddArc(_formWidth - 16, FormHeight - 16, 16, 16, 0, 90);
        path.AddArc(0, FormHeight - 16, 16, 16, 90, 90);
        path.CloseFigure();
        Region = new Region(path);

        // ── Accent bar (left side) ──
        _accentPanel = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(AccentWidth, FormHeight),
            BackColor = themeColor,
        };
        Controls.Add(_accentPanel);

        // ── Close button ──
        _closeLabel = new Label
        {
            Text = "✕",
            Location = new Point(_formWidth - CloseBtnSize - 10, 8),
            Size = new Size(CloseBtnSize, CloseBtnSize),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(SystemFont.FontFamily, 10, FontStyle.Regular),
            ForeColor = Color.FromArgb(0x99, 0x99, 0x99),
            Cursor = Cursors.Hand,
            BackColor = Color.Transparent,
        };
        _closeLabel.MouseEnter += (_, _) => _closeLabel.ForeColor = Color.FromArgb(0x33, 0x33, 0x33);
        _closeLabel.MouseLeave += (_, _) => _closeLabel.ForeColor = Color.FromArgb(0x99, 0x99, 0x99);
        _closeLabel.Click += (_, _) => BeginClose();
        _closeLabel.MouseDown += (_, _) => { };
        Controls.Add(_closeLabel);

        // ── Icon circle ──
        var (iconChar, _) = TypeIcon.GetValueOrDefault(_type, ('i', "info"));
        var iconLabel = new Label
        {
            Text = iconChar.ToString(),
            Location = new Point(AccentWidth + LayoutPad, LayoutPad),
            Size = new Size(32, 32),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(SystemFont.FontFamily, 16, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = themeColor,
            FlatStyle = FlatStyle.Flat,
        };
        using var iconPath = new GraphicsPath();
        iconPath.AddEllipse(0, 0, 31, 31);
        iconLabel.Region = new Region(iconPath);
        Controls.Add(iconLabel);

        // ── Title ──
        _titleLabel = new Label
        {
            Text = _title,
            Location = new Point(AccentWidth + LayoutPad + 32 + 10, LayoutPad),
            Size = new Size(_formWidth - AccentWidth - LayoutPad - 32 - 10 - CloseBtnSize - 20, 22),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(SystemFont.FontFamily, 12, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(0x22, 0x22, 0x22),
            AutoSize = false,
            UseCompatibleTextRendering = false,
        };
        Controls.Add(_titleLabel);

        // ── Message ──
        int msgTop = LayoutPad + 22 + 6;
        int msgWidth = _formWidth - AccentWidth - LayoutPad * 2 - 10;
        _messageLabel = new Label
        {
            Text = _message,
            Location = new Point(AccentWidth + LayoutPad, msgTop),
            Size = new Size(msgWidth, 48),
            TextAlign = ContentAlignment.TopLeft,
            Font = new Font(SystemFont.FontFamily, 9.5f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(0x55, 0x55, 0x55),
            AutoSize = false,
            UseCompatibleTextRendering = false,
        };
        Controls.Add(_messageLabel);

        // ── Action buttons ──
        int btnY = FormHeight - LayoutPad - 30;
        int btnX = _formWidth - LayoutPad;
        var btnList = new List<Button>();

        for (int i = btnSizes.Length - 1; i >= 0; i--)
        {
            var (label, value, width) = btnSizes[i];
            bool isPrimary = i == btnSizes.Length - 1;

            // Truncate label if still too wide for button
            string displayText = label;
            if (width >= 160)
            {
                var font = new Font(SystemFont.FontFamily, 9);
                while (TextRenderer.MeasureText(displayText + "…", font).Width + 20 > 160 && displayText.Length > 3)
                    displayText = displayText[..^1];
                if (displayText != label)
                    displayText += "…";
            }

            var btn = new Button
            {
                Text = displayText,
                Size = new Size(width, 30),
                FlatStyle = FlatStyle.Flat,
                Font = new Font(SystemFont.FontFamily, 9, isPrimary ? FontStyle.Bold : FontStyle.Regular),
                Cursor = Cursors.Hand,
                BackColor = isPrimary ? themeColor : Color.White,
                ForeColor = isPrimary ? Color.White : themeColor,
                TextAlign = ContentAlignment.MiddleCenter,
                UseVisualStyleBackColor = false,
                FlatAppearance =
                {
                    BorderColor = themeColor,
                    BorderSize = isPrimary ? 0 : 1,
                },
                Tag = value,
            };
            btn.FlatAppearance.MouseOverBackColor = isPrimary
                ? Lighten(themeColor, -0.15f)
                : Color.FromArgb(0xF0, 0xF0, 0xF0);
            // Show full label in tooltip if truncated
            if (displayText != label)
                _toolTip.SetToolTip(btn, label);
            btn.MouseDown += ActionButtonClick;

            btnX -= btn.Width + 8;
            btn.Location = new Point(btnX, btnY);
            btnList.Add(btn);
            Controls.Add(btn);
        }

        _actionButtons = btnList.ToArray();
    }

    private void PositionAtBottomRight()
    {
        var screen = Screen.PrimaryScreen;
        var workingArea = screen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        _slideTargetX = workingArea.Right - FormWidth - EdgeMargin;
        int y = workingArea.Bottom - FormHeight - EdgeMargin;
        // Start off-screen to the right
        _slideStartX = workingArea.Right + 20;
        Location = new Point(_slideStartX, y);
    }

    // ── Slide-in animation ──
    private void StartSlideIn()
    {
        _slideStep = 0;
        _slideTimer.Interval = 20; // ~50fps
        _slideTimer.Tick += SlideTick;
        _slideTimer.Start();
    }

    private void SlideTick(object? sender, EventArgs e)
    {
        const int totalSteps = 15;
        _slideStep++;
        if (_slideStep > totalSteps)
        {
            _slideTimer.Stop();
            Location = new Point(_slideTargetX, Location.Y);
            StartFadeIn();
            return;
        }

        // Ease-out: t^2
        double t = (double)_slideStep / totalSteps;
        double eased = t * t; // ease-out quad
        int x = (int)(_slideStartX + (_slideTargetX - _slideStartX) * eased);
        Location = new Point(x, Location.Y);
    }

    // ── Fade-in animation ──
    private void StartFadeIn()
    {
        Opacity = 0;
        _fadeStep = 0;
        _fadeTimer.Interval = 30;
        _fadeTimer.Tick += FadeInTick;
        _fadeTimer.Start();
    }

    private void FadeInTick(object? sender, EventArgs e)
    {
        const int totalSteps = 8;
        _fadeStep++;
        Opacity = Math.Min(1.0, (double)_fadeStep / totalSteps);
        if (_fadeStep >= totalSteps)
        {
            _fadeTimer.Stop();
            Opacity = 1.0;
        }
    }

    // ── Close animation ──
    private void BeginClose()
    {
        if (_isClosing) return;
        _isClosing = true;
        _closeTimer.Stop();
        _slideTimer.Stop();
        _fadeTimer.Stop();

        _fadeStep = 0;
        _fadeTimer.Tick -= FadeInTick;
        _fadeTimer.Tick += FadeOutTick;
        _fadeTimer.Interval = 25;
        _fadeTimer.Start();
    }

    private void FadeOutTick(object? sender, EventArgs e)
    {
        const int totalSteps = 10;
        _fadeStep++;
        Opacity = Math.Max(0, 1.0 - (double)_fadeStep / totalSteps);
        if (_fadeStep >= totalSteps)
        {
            _fadeTimer.Stop();
            Close();
        }
    }

    // ── Auto-close timer ──
    private int _secondsRemaining;

    private void CloseTimerTick(object? sender, EventArgs e)
    {
        if (_mouseInside) return; // Pause when mouse is over

        _secondsRemaining++;
        if (_secondsRemaining >= _timeoutSeconds)
        {
            BeginClose();
        }
    }

    // ── Result of user's action selection ──
    public string? SelectedActionValue { get; private set; }
    public bool ActionClicked => _actionClicked;

    // ── Button click ──
    // Uses GetAsyncKeyState to verify the left mouse button is physically pressed.
    // This is the only reliable way to filter out synthetic click events that
    // WS_EX_NOACTIVATE windows can generate (full MouseEnter→MouseClick sequences).
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    private const int VK_LBUTTON = 0x01;

    private void ActionButtonClick(object? sender, MouseEventArgs e)
    {
        // Only respond to left-click
        if (e.Button != MouseButtons.Left) return;
        // Guard: ignore clicks during/after close
        if (_isClosing) return;
        if (sender is not Button btn || btn.Tag is not string actionValue) return;

        // CRITICAL: verify the left mouse button is physically pressed right now.
        // This is the only reliable way to filter out synthetic click events that
        // WS_EX_NOACTIVATE windows can generate.
        if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0) return;

        SelectedActionValue = actionValue;
        _actionClicked = true;

        // Handle "view" action: bring terminal to front, don't write result
        if (actionValue is "view" or "查看" or "View")
        {
            BringTerminalToFront();
            // Don't write result file → hook returns nothing → Claude Code shows terminal prompt
        }
        else
        {
            // Write result file for allow/deny responses
            if (_resultFilePath != null)
            {
                try { File.WriteAllText(_resultFilePath, actionValue, Encoding.UTF8); }
                catch { /* best-effort */ }
            }
        }

        BeginClose();
    }

    private static void BringTerminalToFront()
    {
        // Try common terminal processes (order: most likely first)
        // Also try to find the console host (conhost) which wraps cmd/powershell
        string[] terminalNames = [
            "WindowsTerminal", "Windowsterminal", "wt",
            "pwsh", "powershell", "powershell_ise",
            "cmd", "conhost", "bash",
        ];
        foreach (var name in terminalNames)
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    var hwnd = proc.MainWindowHandle;
                    if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
                    {
                        ShowWindow(hwnd, SW_RESTORE); // Restore if minimized
                        SetForegroundWindow(hwnd);
                        return;
                    }
                }
            }
            catch { /* skip */ }
        }
    }

    // ── Win32: window visibility and restore ──
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;

    // ── Sound ──
    private static void PlayNotificationSound()
    {
        try
        {
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch { /* silent fallback */ }
    }

    // ── Helpers ──
    private static Color Lighten(Color color, float factor)
    {
        int r = Math.Clamp((int)(color.R * (1 + factor)), 0, 255);
        int g = Math.Clamp((int)(color.G * (1 + factor)), 0, 255);
        int b = Math.Clamp((int)(color.B * (1 + factor)), 0, 255);
        return Color.FromArgb(r, g, b);
    }

    // ── Debug logging for unexpected clicks ──
    private static void DebugLog(string msg)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "ClaudeCodeNotify_debug.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
        }
        catch { }
    }

    // ── Win32 API ──
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
