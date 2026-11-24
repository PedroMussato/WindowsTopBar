using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// TopBar: a macOS-style bar docked at the top of every monitor, showing date,
// time and battery. It registers as a Windows AppBar, so the screen "shrinks":
// maximized windows start below the bar. Written in C# 5 so it compiles with
// the compiler that ships with Windows (no SDK install needed).
namespace TopBar
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            bool created;
            using (Mutex m = new Mutex(true, "TopBar_SingleInstance_Mutex", out created))
            {
                if (!created) return;
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new BarContext());
            }
        }
    }

    class Settings
    {
        public bool Use24Hour = true;
        public bool ShowTime = true;
        public bool ShowCamera = true;
        public bool Fresh;

        static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TopBar"); }
        }

        static string FilePath
        {
            get { return Path.Combine(Dir, "settings.ini"); }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) { s.Fresh = true; return s; }
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i < 1) continue;
                    string key = line.Substring(0, i).Trim();
                    bool val = line.Substring(i + 1).Trim().ToLowerInvariant() == "true";
                    if (key == "Use24Hour") s.Use24Hour = val;
                    else if (key == "ShowTime") s.ShowTime = val;
                    else if (key == "ShowCamera") s.ShowCamera = val;
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(FilePath, new string[] {
                    "Use24Hour=" + Use24Hour.ToString().ToLowerInvariant(),
                    "ShowTime=" + ShowTime.ToString().ToLowerInvariant(),
                    "ShowCamera=" + ShowCamera.ToString().ToLowerInvariant()
                });
            }
            catch { }
        }
    }

    static class Startup
    {
        const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "TopBar";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    return k != null && k.GetValue(ValueName) != null;
                }
            }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(ValueName, false);
                }
            }
            catch { }
        }
    }

    // One docked bar per monitor.
    class BarForm : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int left, top, right, bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [DllImport("shell32.dll")]
        static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        [DllImport("user32.dll")]
        static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        const uint ABM_NEW = 0;
        const uint ABM_REMOVE = 1;
        const uint ABM_QUERYPOS = 2;
        const uint ABM_SETPOS = 3;
        const uint ABM_ACTIVATE = 6;
        const uint ABM_WINDOWPOSCHANGED = 9;
        const uint ABE_TOP = 1;
        const int ABN_FULLSCREENAPP = 2;
        const int ABN_POSCHANGED = 1;

        const int WM_ACTIVATE = 0x0006;
        const int WM_WINDOWPOSCHANGED = 0x0047;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_NOACTIVATE = 0x08000000;

        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;

        static readonly uint CallbackMessage = RegisterWindowMessage("TopBar_AppBarMessage");

        readonly Screen screen;
        readonly Font font;
        readonly int barHeight;
        string text = "";
        bool registered;
        bool showCamera;
        bool cameraOn;

        public BarForm(Screen screen)
        {
            this.screen = screen;
            font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
            barHeight = font.Height + 10;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(24, 24, 24);
            DoubleBuffered = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // Tool window: not in Alt+Tab. No-activate: never steals focus.
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public void Start()
        {
            Show();
            RegisterBar();
        }

        public void SetText(string t)
        {
            if (t == text) return;
            text = t;
            Invalidate();
        }

        public void SetCamera(bool show, bool on)
        {
            if (show == showCamera && on == cameraOn) return;
            showCamera = show;
            cameraOn = on;
            Invalidate();
        }

        APPBARDATA NewData()
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = Handle;
            abd.uCallbackMessage = CallbackMessage;
            return abd;
        }

        void RegisterBar()
        {
            APPBARDATA abd = NewData();
            SHAppBarMessage(ABM_NEW, ref abd);
            registered = true;
            PositionBar();
        }

        void UnregisterBar()
        {
            if (!registered || !IsHandleCreated) return;
            APPBARDATA abd = NewData();
            SHAppBarMessage(ABM_REMOVE, ref abd);
            registered = false;
        }

        // Reserves a strip at the top edge of this monitor and moves the window into it.
        void PositionBar()
        {
            Rectangle b = screen.Bounds;
            APPBARDATA abd = NewData();
            abd.uEdge = ABE_TOP;
            abd.rc.left = b.Left;
            abd.rc.top = b.Top;
            abd.rc.right = b.Right;
            abd.rc.bottom = b.Top + barHeight;
            SHAppBarMessage(ABM_QUERYPOS, ref abd);
            abd.rc.bottom = abd.rc.top + barHeight;
            SHAppBarMessage(ABM_SETPOS, ref abd);
            SetBounds(abd.rc.left, abd.rc.top, abd.rc.right - abd.rc.left, abd.rc.bottom - abd.rc.top);
        }

        protected override void WndProc(ref Message m)
        {
            if (registered)
            {
                if (m.Msg == CallbackMessage)
                {
                    int n = m.WParam.ToInt32();
                    if (n == ABN_POSCHANGED)
                    {
                        PositionBar();
                    }
                    else if (n == ABN_FULLSCREENAPP)
                    {
                        // A full-screen app opened (lParam != 0) or closed: go behind it / come back.
                        bool full = m.LParam != IntPtr.Zero;
                        SetWindowPos(Handle, full ? HWND_BOTTOM : HWND_TOPMOST, 0, 0, 0, 0,
                                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    }
                }
                else if (m.Msg == WM_ACTIVATE)
                {
                    APPBARDATA abd = NewData();
                    SHAppBarMessage(ABM_ACTIVATE, ref abd);
                }
                else if (m.Msg == WM_WINDOWPOSCHANGED)
                {
                    APPBARDATA abd = NewData();
                    SHAppBarMessage(ABM_WINDOWPOSCHANGED, ref abd);
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
                sf.LineAlignment = StringAlignment.Center;

                // Layout: "<main text>  •  ● Camera", centered as one line.
                string left = showCamera ? text + "  \u2022  " : text;
                string label = "Camera";
                float leftW = g.MeasureString(left, font, 10000, sf).Width;
                float labelW = showCamera ? g.MeasureString(label, font, 10000, sf).Width : 0f;
                float dot = font.Height * 0.5f;
                float gap = font.Height * 0.3f;
                float total = leftW + (showCamera ? dot + gap + labelW : 0f);
                float x = (Width - total) / 2f;
                float h = Height - 1;

                using (Brush b = new SolidBrush(Color.FromArgb(240, 240, 240)))
                {
                    g.DrawString(left, font, b, new RectangleF(x, 0, leftW + 4, h), sf);
                    if (showCamera)
                    {
                        float dx = x + leftW;
                        float dy = (h - dot) / 2f;
                        Color c = cameraOn ? Color.FromArgb(52, 199, 89) : Color.FromArgb(255, 69, 58);
                        using (Brush cb = new SolidBrush(c))
                        {
                            g.FillEllipse(cb, dx, dy, dot, dot);
                        }
                        g.DrawString(label, font, b, new RectangleF(dx + dot + gap, 0, labelW + 4, h), sf);
                    }
                }
            }
            // Subtle separator line along the bottom edge.
            using (Pen p = new Pen(Color.FromArgb(60, 60, 60)))
            {
                g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnregisterBar();
                font.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    class BarContext : ApplicationContext
    {
        readonly Settings settings;
        readonly List<BarForm> bars = new List<BarForm>();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly NotifyIcon tray = new NotifyIcon();
        volatile bool needRebuild;

        ToolStripMenuItem item24, item12, itemShowTime, itemCamera, itemStartup;

        public BarContext()
        {
            settings = Settings.Load();
            if (settings.Fresh)
            {
                // First run: start with Windows by default.
                Startup.Set(true);
                settings.Save();
            }

            BuildTray();
            RebuildBars();

            SystemEvents.DisplaySettingsChanged += delegate { needRebuild = true; };
            SystemEvents.PowerModeChanged += delegate { needRebuild = true; };

            timer.Interval = 1000;
            timer.Tick += delegate { Tick(); };
            timer.Start();
        }

        void BuildTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            item24 = new ToolStripMenuItem("24-hour clock");
            item12 = new ToolStripMenuItem("12-hour clock");
            itemShowTime = new ToolStripMenuItem("Show time");
            itemCamera = new ToolStripMenuItem("Show camera status");
            itemStartup = new ToolStripMenuItem("Start with Windows");
            ToolStripMenuItem exit = new ToolStripMenuItem("Exit");

            item24.Click += delegate { settings.Use24Hour = true; SaveAndRefresh(); };
            item12.Click += delegate { settings.Use24Hour = false; SaveAndRefresh(); };
            itemShowTime.Click += delegate { settings.ShowTime = !settings.ShowTime; SaveAndRefresh(); };
            itemStartup.Click += delegate { Startup.Set(!Startup.IsEnabled()); SaveAndRefresh(); };
            itemCamera.Click += delegate { settings.ShowCamera = !settings.ShowCamera; SaveAndRefresh(); };
            exit.Click += delegate { ExitThread(); };

            menu.Items.Add(item24);
            menu.Items.Add(item12);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itemShowTime);
            menu.Items.Add(itemCamera);
            menu.Items.Add(itemStartup);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);

            tray.Icon = SystemIcons.Information;
            tray.Text = "TopBar";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;

            RefreshMenuChecks();
        }

        void RefreshMenuChecks()
        {
            item24.Checked = settings.Use24Hour;
            item12.Checked = !settings.Use24Hour;
            itemShowTime.Checked = settings.ShowTime;
            itemCamera.Checked = settings.ShowCamera;
            itemStartup.Checked = Startup.IsEnabled();
        }

        void SaveAndRefresh()
        {
            settings.Save();
            RefreshMenuChecks();
            Tick();
        }

        void RebuildBars()
        {
            foreach (BarForm b in bars) b.Dispose();
            bars.Clear();
            string t = BuildText();
            foreach (Screen s in Screen.AllScreens)
            {
                BarForm bar = new BarForm(s);
                bar.SetText(t);
                bar.SetCamera(settings.ShowCamera, settings.ShowCamera && CameraInUse());
                bars.Add(bar);
                bar.Start();
            }
        }

        string BuildText()
        {
            DateTime now = DateTime.Now;
            List<string> parts = new List<string>();
            parts.Add(now.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture));
            if (settings.ShowTime)
            {
                parts.Add(now.ToString(settings.Use24Hour ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture));
            }

            PowerStatus ps = SystemInformation.PowerStatus;
            bool hasBattery = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0
                              && ps.BatteryLifePercent <= 1.0f;
            if (hasBattery)
            {
                int pct = (int)Math.Round(ps.BatteryLifePercent * 100f);
                bool charging = ps.PowerLineStatus == PowerLineStatus.Online;
                parts.Add((charging ? "\u26A1 " : "") + pct + "%");
            }
            return string.Join("  \u2022  ", parts.ToArray());
        }

        // Windows records, per app, when it started and stopped using the camera
        // (the data behind its own camera privacy indicator). Start later than
        // stop means that app is using the camera right now.
        static bool CameraInUse()
        {
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam"))
                {
                    if (root == null) return false;
                    if (AnyAppUsing(root)) return true;
                    using (RegistryKey desktopApps = root.OpenSubKey("NonPackaged"))
                    {
                        if (desktopApps != null && AnyAppUsing(desktopApps)) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        static bool AnyAppUsing(RegistryKey parent)
        {
            foreach (string name in parent.GetSubKeyNames())
            {
                if (name == "NonPackaged") continue;
                try
                {
                    using (RegistryKey k = parent.OpenSubKey(name))
                    {
                        if (k == null) continue;
                        object start = k.GetValue("LastUsedTimeStart");
                        object stop = k.GetValue("LastUsedTimeStop");
                        if (start is long && stop is long && (long)start > 0 && (long)start > (long)stop)
                            return true;
                    }
                }
                catch { }
            }
            return false;
        }

        void Tick()
        {
            if (needRebuild)
            {
                needRebuild = false;
                RebuildBars();
                return;
            }
            string t = BuildText();
            bool cam = settings.ShowCamera && CameraInUse();
            foreach (BarForm b in bars)
            {
                b.SetText(t);
                b.SetCamera(settings.ShowCamera, cam);
            }
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            tray.Visible = false;
            tray.Dispose();
            foreach (BarForm b in bars) b.Dispose(); // also gives the reserved space back
            bars.Clear();
            base.ExitThreadCore();
        }
    }
}
