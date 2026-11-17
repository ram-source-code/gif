// GIF WinLive - the renderer: decodes the GIF and plays it inside the desktop layer.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using D = System.Drawing;
using DI = System.Drawing.Imaging;
using D2 = System.Drawing.Drawing2D;

namespace GifWp
{
    public sealed class HostException : Exception { public HostException(string m) : base(m) { } }

    sealed class Wall { public Window Win; public Image Img; public IntPtr Hwnd; public Int32Rect Rect; public bool Paused; }

    public sealed class Engine
    {
        readonly Action<string> log;
        readonly DispatcherTimer frameTimer, houseTimer;
        public event EventHandler StatusChanged;

        Settings s = new Settings();
        readonly List<Wall> walls = new List<Wall>();
        List<Int32Rect> rects = new List<Int32Rect>();
        IntPtr host = IntPtr.Zero, after = new IntPtr(1);
        string hostKind = "";

        // When all frames fit the memory budget they are pre-decoded into frozen
        // bitmaps that live on the GPU (playback = texture swap, ~0 CPU).
        // Otherwise one reusable surface is decoded into per frame (Memory saver).
        BitmapSource[] frames;
        WriteableBitmap target;
        D.Bitmap gdi, canvas;
        MemoryStream gdiStream;
        DI.FrameDimension dim;
        int[] delays = new int[0];
        int count, index, gifW, gifH, decW, decH, minDelay = 33;
        bool cached;

        bool running, active, retryable, onBattery, frameErrorLogged;
        int tick, retryAt, retryStep = 4, attachFails;
        string lastError = "";

        public Engine(Action<string> log)
        {
            this.log = log;
            frameTimer = new DispatcherTimer(DispatcherPriority.Normal);
            frameTimer.Tick += OnFrame;
            houseTimer = new DispatcherTimer(DispatcherPriority.Background);
            houseTimer.Interval = TimeSpan.FromMilliseconds(500);
            houseTimer.Tick += OnHouse;
        }

        // Status for the control panel.
        public bool Running { get { return running; } }            // playing, or waiting for the desktop
        public bool Active { get { return active; } }              // actually playing
        public bool Retrying { get { return running && !active; } }
        public string LastError { get { return lastError; } }
        public string GifPath { get { return s.GifPath; } }
        public int Width { get { return gifW; } }
        public int Height { get { return gifH; } }
        public int Frames { get { return count; } }
        public bool Cached { get { return cached; } }

        public bool Start(Settings settings)
        {
            s = settings.Clone();
            running = true;
            Clear();
            houseTimer.Start();
            try
            {
                Build(true);
                active = true; retryable = false; retryStep = 4; lastError = ""; attachFails = 0;
                Native.Trim();
                Changed();
                return true;
            }
            catch (Exception ex) { Fail(ex); return false; }
        }

        public void Stop()
        {
            running = false; active = false; lastError = "";
            houseTimer.Stop();
            Clear();
            Native.Trim();
            Changed();
        }

        void Clear()
        {
            frameTimer.Stop();
            ClearWalls();
            ClearGif();
        }

        void Changed() { EventHandler h = StatusChanged; if (h != null) h(this, EventArgs.Empty); }

        void Fail(Exception ex)
        {
            Clear();
            active = false;
            retryable = ex is HostException;
            if (retryable) { retryAt = tick + retryStep; retryStep = Math.Min(retryStep * 2, 60); }
            else { running = false; houseTimer.Stop(); }
            if (ex.Message != lastError) log("error: " + ex.Message);
            lastError = ex.Message;
            Changed();
        }

        void Build(bool loadGif)
        {
            if (string.IsNullOrWhiteSpace(s.GifPath))
                throw new InvalidOperationException("No GIF selected yet.");
            if (!File.Exists(s.GifPath))
                throw new FileNotFoundException("The GIF file was not found: " + s.GifPath);

            rects = Desktop.Targets(s.MonitorMode);
            if (host == IntPtr.Zero || !Native.IsWindow(host))
            {
                host = Desktop.FindHost(out after, out hostKind);
                if (host == IntPtr.Zero)
                    throw new HostException(Desktop.ExplorerRunning()
                        ? "Waiting for the Windows desktop to be ready (retrying automatically)."
                        : "Windows Explorer is not running. The wallpaper appears as soon as it starts.");
            }
            if (loadGif) LoadGif();

            minDelay = Math.Max(10, (int)Math.Round(1000.0 / Math.Max(1, Math.Min(60, s.MaxFps))));
            foreach (Int32Rect r in rects) { Wall w = CreateWall(r); walls.Add(w); Attach(w); }
            index = 0;
            ShowFrame(0);
            if (count > 1)
            {
                frameTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(minDelay, delays[0]));
                frameTimer.Start();
            }
        }

        // ---------------- GIF decoding (GDI+ composites, WPF draws on the GPU) ----------------

        void DecodeSize(List<Int32Rect> targets, out int w, out int h)
        {
            double scale = 1.0;
            if (s.Fit != "Center")
            {
                int mw = 1, mh = 1;
                foreach (Int32Rect r in targets) { mw = Math.Max(mw, r.Width); mh = Math.Max(mh, r.Height); }
                double need = Math.Max(mw / (double)gifW, mh / (double)gifH);
                if (need < 1.0) scale = need;   // never decode bigger than the screen needs
            }
            w = Math.Max(1, (int)Math.Ceiling(gifW * scale));
            h = Math.Max(1, (int)Math.Ceiling(gifH * scale));
        }

        void LoadGif()
        {
            byte[] bytes = File.ReadAllBytes(s.GifPath);
            MemoryStream ms = new MemoryStream(bytes, 0, bytes.Length, false, true);
            D.Bitmap img;
            try { img = new D.Bitmap(ms); }
            catch { ms.Dispose(); throw new InvalidDataException("This file is not a readable GIF image: " + Path.GetFileName(s.GifPath)); }
            gdi = img; gdiStream = ms;

            dim = new DI.FrameDimension(img.FrameDimensionsList[0]);
            count = Math.Max(1, img.GetFrameCount(dim));
            gifW = img.Width; gifH = img.Height;

            byte[] delayBytes = null;   // per-frame delays, property 0x5100, unit 1/100 s
            foreach (int id in img.PropertyIdList) if (id == 0x5100) { delayBytes = img.GetPropertyItem(0x5100).Value; break; }
            delays = new int[count];
            for (int i = 0; i < count; i++)
            {
                int d = (delayBytes != null && delayBytes.Length >= (i + 1) * 4) ? BitConverter.ToInt32(delayBytes, i * 4) * 10 : 100;
                if (d <= 10) d = 100;          // same rule browsers use for 0/1 cs frames
                if (d > 5000) d = 5000;
                delays[i] = d;
            }

            DecodeSize(rects, out decW, out decH);
            if (decW != gifW || decH != gifH) canvas = new D.Bitmap(decW, decH, DI.PixelFormat.Format32bppPArgb);

            // Keep every frame decoded (GPU texture swap, ~0 CPU) or decode while playing (tiny RAM).
            // Auto picks whichever is cheaper overall: few/small frames -> keep, many frames -> decode live.
            long total = (long)decW * decH * 4 * count;
            const long MB = 1024 * 1024;
            if (s.Memory == "Speed") cached = count == 1 || total <= 512 * MB;
            else if (s.Memory == "Saver") cached = count == 1;
            else cached = count == 1 || total <= 48 * MB || (count <= 12 && total <= 256 * MB);
            if (cached)
            {
                frames = new BitmapSource[count];
                for (int i = 0; i < count; i++) frames[i] = Decode(i);
                ReleaseDecoder();   // the file and GDI+ objects are no longer needed
            }
            else
            {
                target = new WriteableBitmap(decW, decH, 96, 96, PixelFormats.Pbgra32, null);
            }
        }

        BitmapSource Decode(int i)
        {
            if (count > 1) gdi.SelectActiveFrame(dim, i);
            D.Bitmap src = gdi;
            if (canvas != null)
            {
                using (D.Graphics g = D.Graphics.FromImage(canvas))
                using (DI.ImageAttributes ia = new DI.ImageAttributes())
                {
                    g.CompositingMode = D2.CompositingMode.SourceCopy;
                    g.InterpolationMode = cached ? D2.InterpolationMode.HighQualityBicubic : D2.InterpolationMode.Bilinear;
                    g.PixelOffsetMode = D2.PixelOffsetMode.HighQuality;
                    ia.SetWrapMode(D2.WrapMode.TileFlipXY);
                    g.DrawImage(gdi, new D.Rectangle(0, 0, decW, decH), 0, 0, gifW, gifH, D.GraphicsUnit.Pixel, ia);
                }
                src = canvas;
            }
            // Premultiplied BGRA is WPF's native format: no conversion when drawing.
            DI.BitmapData bd = src.LockBits(new D.Rectangle(0, 0, decW, decH), DI.ImageLockMode.ReadOnly, DI.PixelFormat.Format32bppPArgb);
            try
            {
                int size = bd.Stride * decH;
                if (target != null)
                {
                    target.WritePixels(new Int32Rect(0, 0, decW, decH), bd.Scan0, size, bd.Stride);
                    return target;
                }
                BitmapSource bs = BitmapSource.Create(decW, decH, 96, 96, PixelFormats.Pbgra32, null, bd.Scan0, size, bd.Stride);
                bs.Freeze();
                return bs;
            }
            finally { src.UnlockBits(bd); }
        }

        void ShowFrame(int i)
        {
            BitmapSource src = cached ? frames[i] : Decode(i);
            foreach (Wall w in walls)
                if (!w.Paused && !ReferenceEquals(w.Img.Source, src)) w.Img.Source = src;
        }

        void ReleaseDecoder()
        {
            if (canvas != null) { canvas.Dispose(); canvas = null; }
            if (gdi != null) { gdi.Dispose(); gdi = null; }
            if (gdiStream != null) { gdiStream.Dispose(); gdiStream = null; }
        }

        void ClearGif()
        {
            bool had = frames != null || target != null || gdi != null;
            ReleaseDecoder();
            frames = null; target = null; count = 0; delays = new int[0]; frameErrorLogged = false;
            if (had) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        }

        // ---------------- wallpaper windows ----------------

        Wall CreateWall(Int32Rect r)
        {
            Window win = new Window();
            win.Title = "GIF WinLive - desktop";
            win.WindowStyle = WindowStyle.None;
            win.ResizeMode = ResizeMode.NoResize;
            win.ShowInTaskbar = false;
            win.ShowActivated = false;
            win.Focusable = false;
            win.WindowStartupLocation = WindowStartupLocation.Manual;
            win.Left = -32000; win.Top = -32000;   // off-screen until it is inside the desktop layer
            win.Width = Math.Max(16, r.Width); win.Height = Math.Max(16, r.Height);
            win.SnapsToDevicePixels = true;
            win.UseLayoutRounding = true;

            Brush bg;
            try { bg = (Brush)new BrushConverter().ConvertFromString(s.Background); } catch { bg = Brushes.Black; }
            if (bg.CanFreeze) bg.Freeze();
            Grid grid = new Grid();
            grid.Background = bg;
            grid.ClipToBounds = true;

            Image img = new Image();
            img.HorizontalAlignment = HorizontalAlignment.Center;
            img.VerticalAlignment = VerticalAlignment.Center;
            img.Stretch = s.Fit == "Fit" ? Stretch.Uniform : s.Fit == "Stretch" ? Stretch.Fill : s.Fit == "Center" ? Stretch.Fill : Stretch.UniformToFill;
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.Linear);
            grid.Children.Add(img);
            win.Content = grid;
            win.Show();

            // Size the window (and a centered image) in real pixels, whatever the DPI scaling.
            PresentationSource ps = PresentationSource.FromVisual(win);
            if (ps != null && ps.CompositionTarget != null)
            {
                Matrix m = ps.CompositionTarget.TransformToDevice;
                if (m.M11 > 0 && m.M22 > 0)
                {
                    win.Width = Math.Max(16, r.Width) / m.M11;
                    win.Height = Math.Max(16, r.Height) / m.M22;
                    if (s.Fit == "Center") { img.Width = decW / m.M11; img.Height = decH / m.M22; }
                }
            }
            Wall w = new Wall();
            w.Win = win; w.Img = img; w.Rect = r; w.Hwnd = new WindowInteropHelper(win).Handle;
            return w;
        }

        bool Place(Wall w, IntPtr h, IntPtr insertAfter)
        {
            Native.SetParent(w.Hwnd, h);
            if (s.UseChildStyle)
            {
                int st = Native.GetWindowLong(w.Hwnd, -16);
                st &= ~unchecked((int)0x80000000);                         // WS_POPUP
                st &= ~(0x00C00000 | 0x00040000 | 0x00800000 | 0x00080000); // caption, frame, border, sysmenu
                st |= 0x40000000;                                          // WS_CHILD
                Native.SetWindowLong(w.Hwnd, -16, st);
                int ex = Native.GetWindowLong(w.Hwnd, -20);
                ex &= ~(0x00040000 | 0x00000008);                          // APPWINDOW, TOPMOST
                ex |= 0x08000000 | 0x00000080;                             // NOACTIVATE, TOOLWINDOW
                Native.SetWindowLong(w.Hwnd, -20, ex);
            }
            POINT p = new POINT(); p.X = w.Rect.X; p.Y = w.Rect.Y;
            Native.ScreenToClient(h, ref p);
            Native.SetWindowPos(w.Hwnd, insertAfter, p.X, p.Y, w.Rect.Width, w.Rect.Height, 0x0070); // NOACTIVATE|FRAMECHANGED|SHOWWINDOW
            if (!Native.IsWindowVisible(w.Hwnd)) Native.ShowWindow(w.Hwnd, 4);
            return Native.GetAncestor(w.Hwnd, 1) == h;
        }

        bool Attach(Wall w)
        {
            if (Place(w, host, after)) return true;
            IntPtr pm = Native.FindWindow("Progman", null);
            if (pm == IntPtr.Zero || pm == host) return false;
            host = pm; after = new IntPtr(1); hostKind = "Progman (fallback)";
            log("the desktop layer refused the window - using Progman");
            return Place(w, host, after);
        }

        void ClearWalls()
        {
            lastFg = IntPtr.Zero;
            foreach (Wall w in walls)
            {
                try { w.Img.Source = null; } catch { }
                try { w.Win.Close(); } catch { }
            }
            walls.Clear();
        }

        // ---------------- timers ----------------

        void OnFrame(object sender, EventArgs e)
        {
            try
            {
                if (count <= 1) { frameTimer.Stop(); return; }
                index = (index + 1) % count;
                ShowFrame(index);
                TimeSpan iv = TimeSpan.FromMilliseconds(Math.Max(minDelay, delays[index]));
                if (frameTimer.Interval != iv) frameTimer.Interval = iv;
            }
            catch (Exception ex)
            {
                if (!frameErrorLogged) { frameErrorLogged = true; log("frame error: " + ex.Message); }
            }
        }

        void OnHouse(object sender, EventArgs e)
        {
            try
            {
                tick++;
                if (!active)
                {
                    if (running && retryable && tick >= retryAt) Start(s);
                    return;
                }
                if (tick % 4 == 0)   // heavier checks every 2 seconds
                {
                    onBattery = s.PauseOnLowBattery && LowBattery();
                    CheckDesktop();
                    if (!active) return;
                }
                UpdatePause();
            }
            catch (Exception ex) { log("housekeeping error: " + ex.Message); }
        }

        // Unplugged and either at 20 % or less, or Battery saver is on.
        static bool LowBattery()
        {
            POWERSTATUS p;
            if (!Native.GetSystemPowerStatus(out p) || p.ACLineStatus != 0) return false;
            return p.SystemStatusFlag == 1 || p.BatteryLifePercent <= 20;   // 255 = unknown, never "low"
        }

        void UpdatePause()
        {
            bool all = onBattery;
            int q;
            if (!all && Native.SHQueryUserNotificationState(out q) == 0)
            {
                if (q == 1) all = true;                                       // locked / screen saver / switched user
                else if (s.PauseWhenCovered && (q == 3 || q == 4)) all = true; // full-screen D3D game / presentation mode
            }

            RECT fg = new RECT();
            bool haveFg = false;
            if (!all && s.PauseWhenCovered)
            {
                IntPtr f = Native.GetForegroundWindow();
                if (f != IntPtr.Zero && !IsShellOrOurs(f) && Native.GetWindowRect(f, out fg)) haveFg = true;
            }

            bool any = false;
            foreach (Wall w in walls)
            {
                bool p = all;
                if (!p && haveFg)
                {
                    long iw = Math.Max(0, Math.Min(fg.Right, w.Rect.X + w.Rect.Width) - Math.Max(fg.Left, w.Rect.X));
                    long ih = Math.Max(0, Math.Min(fg.Bottom, w.Rect.Y + w.Rect.Height) - Math.Max(fg.Top, w.Rect.Y));
                    p = iw * ih * 100 >= (long)w.Rect.Width * w.Rect.Height * 92;   // >= 92 % covered
                }
                w.Paused = p;
                if (!p) any = true;
            }
            if (count > 1)
            {
                if (any && !frameTimer.IsEnabled) frameTimer.Start();
                else if (!any && frameTimer.IsEnabled) frameTimer.Stop();
            }

            // Fully paused for 30 s (game, video, locked PC, low battery): hand the
            // working set back to Windows once. Pages return on demand when it resumes.
            if (any) { pausedSince = -1; trimmedThisPause = false; }
            else if (pausedSince < 0) pausedSince = tick;
            else if (!trimmedThisPause && tick - pausedSince >= 60) { trimmedThisPause = true; Native.Trim(); }
        }

        int pausedSince = -1;
        bool trimmedThisPause;

        IntPtr lastFg;
        bool lastFgIsShell;

        bool IsShellOrOurs(IntPtr h)
        {
            if (h == lastFg) return lastFgIsShell;   // the foreground window rarely changes: skip the lookup
            bool shell = false;
            foreach (Wall w in walls) if (w.Hwnd == h) shell = true;
            if (!shell)
            {
                string c = Native.ClassOf(h);
                shell = c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd";
            }
            lastFg = h; lastFgIsShell = shell;
            return shell;
        }

        void CheckDesktop()
        {
            List<Int32Rect> now = Desktop.Targets(s.MonitorMode);
            bool layout = now.Count != rects.Count;
            for (int i = 0; !layout && i < now.Count; i++) layout = !now[i].Equals(rects[i]);

            bool lost = !Native.IsWindow(host);
            foreach (Wall w in walls)
                if (!Native.IsWindow(w.Hwnd) || Native.GetAncestor(w.Hwnd, 1) != host) lost = true;

            if (layout || lost)
            {
                // Explorer restarted (our windows died with it) or the screens changed.
                if (!layout && Native.FindWindow("Progman", null) == IntPtr.Zero) return; // Explorer still starting
                log(layout ? "display layout changed - rebuilding" : "desktop layer changed - re-attaching");
                Rebuild(now, lost);
                return;
            }

            foreach (Wall w in walls)
            {
                RECT r;
                if (Native.GetWindowRect(w.Hwnd, out r) &&
                    (Math.Abs(r.Left - w.Rect.X) > 2 || Math.Abs(r.Top - w.Rect.Y) > 2 ||
                     Math.Abs(r.Right - r.Left - w.Rect.Width) > 2 || Math.Abs(r.Bottom - r.Top - w.Rect.Height) > 2))
                {
                    if (!Attach(w) && ++attachFails <= 3) log("could not re-place the wallpaper window");
                }
            }
        }

        void Rebuild(List<Int32Rect> now, bool newHost)
        {
            try
            {
                frameTimer.Stop();
                ClearWalls();
                if (newHost) host = IntPtr.Zero;
                int w, h;
                bool reload = frames == null && target == null;
                if (!reload) { DecodeSize(now, out w, out h); reload = (w != decW || h != decH); }
                if (reload) ClearGif();
                Build(reload);   // reuses the decoded frames when the size still fits
                Native.Trim();
                Changed();
            }
            catch (Exception ex) { Fail(ex); }
        }
    }
}
