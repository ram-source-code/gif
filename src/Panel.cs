// GIF Live Wallpaper - the control panel. Follows the Windows light/dark theme and accent
// colour. Its styles are parsed only while the window is open; closing it frees everything.
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace GifWp
{
    public sealed class Panel : Window
    {
        const string Xaml = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Background='$BG' TextElement.Foreground='$FG' TextElement.FontSize='13'>
 <Border.Resources>
  <!-- Keyboard focus ring (shown only when navigating with the keyboard). -->
  <Style x:Key='Focus'><Setter Property='Control.Template'><Setter.Value><ControlTemplate>
   <Border Margin='-3' CornerRadius='8' BorderThickness='2' BorderBrush='$FG' SnapsToDevicePixels='True'/>
  </ControlTemplate></Setter.Value></Setter></Style>
  <Style TargetType='Button'>
   <Setter Property='Background' Value='$BTN'/><Setter Property='BorderBrush' Value='$LINE'/><Setter Property='Foreground' Value='$FG'/>
   <Setter Property='Padding' Value='14,6'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{StaticResource Focus}'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
    <Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='6' Padding='{TemplateBinding Padding}'>
     <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
    <ControlTemplate.Triggers>
     <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.86'/></Trigger>
     <Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.72'/></Trigger>
     <Trigger Property='IsEnabled' Value='False'><Setter TargetName='b' Property='Opacity' Value='0.45'/></Trigger>
    </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Accent' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
   <Setter Property='Background' Value='$ACCENT'/><Setter Property='BorderBrush' Value='$ACCENT'/>
   <Setter Property='Foreground' Value='$ONACCENT'/><Setter Property='FontWeight' Value='SemiBold'/>
  </Style>
  <Style x:Key='Link' TargetType='Button'>
   <Setter Property='Foreground' Value='$MUTED'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{StaticResource Focus}'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><ContentPresenter/></ControlTemplate></Setter.Value></Setter>
   <Style.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Foreground' Value='$FG'/></Trigger></Style.Triggers>
  </Style>
  <Style x:Key='Seg' TargetType='RadioButton'>
   <Setter Property='Foreground' Value='$MUTED'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{StaticResource Focus}'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RadioButton'>
    <Border x:Name='b' Background='Transparent' CornerRadius='5' Padding='11,4'><ContentPresenter HorizontalAlignment='Center'/></Border>
    <ControlTemplate.Triggers>
     <Trigger Property='IsMouseOver' Value='True'><Setter Property='Foreground' Value='$FG'/></Trigger>
     <Trigger Property='IsChecked' Value='True'><Setter TargetName='b' Property='Background' Value='$SEGON'/><Setter Property='Foreground' Value='$FG'/></Trigger>
    </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Switch' TargetType='CheckBox'>
   <Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{StaticResource Focus}'/><Setter Property='Margin' Value='0,6'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
    <DockPanel Background='Transparent'>
     <Border x:Name='track' DockPanel.Dock='Right' Width='40' Height='20' CornerRadius='10' BorderThickness='1' BorderBrush='$MUTED' Background='Transparent' Margin='12,0,0,0'>
      <Ellipse x:Name='thumb' Width='12' Height='12' Margin='3,0' HorizontalAlignment='Left' Fill='$MUTED'/></Border>
     <ContentPresenter VerticalAlignment='Center'/>
    </DockPanel>
    <ControlTemplate.Triggers>
     <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='track' Property='BorderBrush' Value='$FG'/><Setter TargetName='thumb' Property='Fill' Value='$FG'/></Trigger>
     <Trigger Property='IsChecked' Value='True'>
     <Setter TargetName='track' Property='Background' Value='$ACCENT'/><Setter TargetName='track' Property='BorderBrush' Value='$ACCENT'/>
     <Setter TargetName='thumb' Property='HorizontalAlignment' Value='Right'/><Setter TargetName='thumb' Property='Fill' Value='$ONACCENT'/>
    </Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Card' TargetType='Border'>
   <Setter Property='Background' Value='$CARD'/><Setter Property='BorderBrush' Value='$LINE'/><Setter Property='BorderThickness' Value='1'/>
   <Setter Property='CornerRadius' Value='8'/><Setter Property='Padding' Value='14,8'/><Setter Property='Margin' Value='0,12,0,0'/>
  </Style>
  <Style x:Key='Segs' TargetType='Border'>
   <Setter Property='Background' Value='$SEGBG'/><Setter Property='CornerRadius' Value='7'/><Setter Property='Padding' Value='2'/>
   <Setter Property='DockPanel.Dock' Value='Right'/>
  </Style>
  <Style x:Key='Row' TargetType='DockPanel'><Setter Property='Margin' Value='0,5'/></Style>
  <Style x:Key='Label' TargetType='TextBlock'><Setter Property='VerticalAlignment' Value='Center'/></Style>
 </Border.Resources>

 <StackPanel Margin='18,16,18,14'>
  <Border x:Name='Drop' Height='200' CornerRadius='10' Background='$PREVIEW' BorderBrush='$LINE' BorderThickness='1' Cursor='Hand' ToolTip='Click to choose a GIF, or drop one here'>
   <TextBlock x:Name='Hint' HorizontalAlignment='Center' VerticalAlignment='Center' TextAlignment='Center' Foreground='$MUTED'
              Text='Click to choose a GIF&#10;or drop one here'/>
  </Border>

  <DockPanel Margin='2,14,0,0'>
   <Button x:Name='Toggle' DockPanel.Dock='Right' Style='{StaticResource Accent}' MinWidth='84'/>
   <Button x:Name='Choose' DockPanel.Dock='Right' Margin='0,0,8,0' Content='Change GIF'/>
   <StackPanel VerticalAlignment='Center' Margin='0,0,12,0'>
    <DockPanel>
     <Ellipse x:Name='Dot' Width='8' Height='8' Margin='0,1,8,0' VerticalAlignment='Center'/>
     <TextBlock x:Name='Status' FontSize='15' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'/>
    </DockPanel>
    <TextBlock x:Name='Detail' Foreground='$MUTED' FontSize='12' Margin='16,2,0,0' TextTrimming='CharacterEllipsis'/>
   </StackPanel>
  </DockPanel>

  <!-- Inline notice: questions and problems appear here instead of pop-up dialogs. -->
  <Border x:Name='Notice' Visibility='Collapsed' Margin='0,12,0,0' Padding='12,8' CornerRadius='8' Background='$CARD' BorderBrush='$LINE' BorderThickness='1'>
   <DockPanel>
    <Button x:Name='NoticeDismiss' DockPanel.Dock='Right' Style='{StaticResource Link}' VerticalAlignment='Center' Margin='14,0,2,0'/>
    <Button x:Name='NoticeAction' DockPanel.Dock='Right' Padding='10,4' VerticalAlignment='Center' Margin='12,0,0,0'/>
    <TextBlock x:Name='NoticeText' TextWrapping='Wrap' VerticalAlignment='Center'/>
   </DockPanel>
  </Border>

  <Border Style='{StaticResource Card}'><StackPanel>
   <DockPanel Style='{StaticResource Row}'>
    <Border Style='{StaticResource Segs}'><StackPanel Orientation='Horizontal'>
     <RadioButton x:Name='MonPrimary' GroupName='mon' Style='{StaticResource Seg}' Content='Primary'/>
     <RadioButton x:Name='MonSpan' GroupName='mon' Style='{StaticResource Seg}' Content='Span all'/>
     <RadioButton x:Name='MonEach' GroupName='mon' Style='{StaticResource Seg}' Content='Each'/>
    </StackPanel></Border>
    <TextBlock Style='{StaticResource Label}' Text='Screens'/>
   </DockPanel>
   <DockPanel Style='{StaticResource Row}'>
    <Border Style='{StaticResource Segs}'><StackPanel Orientation='Horizontal'>
     <RadioButton x:Name='FitFill' GroupName='fit' Style='{StaticResource Seg}' Content='Fill'/>
     <RadioButton x:Name='FitFit' GroupName='fit' Style='{StaticResource Seg}' Content='Fit'/>
     <RadioButton x:Name='FitStretch' GroupName='fit' Style='{StaticResource Seg}' Content='Stretch'/>
     <RadioButton x:Name='FitCenter' GroupName='fit' Style='{StaticResource Seg}' Content='Center'/>
    </StackPanel></Border>
    <TextBlock Style='{StaticResource Label}' Text='Scaling'/>
   </DockPanel>
   <DockPanel Style='{StaticResource Row}'>
    <Border Style='{StaticResource Segs}'><StackPanel Orientation='Horizontal'>
     <RadioButton x:Name='Fps15' GroupName='fps' Style='{StaticResource Seg}' Content='15' AutomationProperties.Name='15 frames per second'/>
     <RadioButton x:Name='Fps24' GroupName='fps' Style='{StaticResource Seg}' Content='24' AutomationProperties.Name='24 frames per second'/>
     <RadioButton x:Name='Fps30' GroupName='fps' Style='{StaticResource Seg}' Content='30' AutomationProperties.Name='30 frames per second'/>
     <RadioButton x:Name='Fps60' GroupName='fps' Style='{StaticResource Seg}' Content='60' AutomationProperties.Name='60 frames per second'/>
    </StackPanel></Border>
    <TextBlock Style='{StaticResource Label}' Text='Max frame rate'/>
   </DockPanel>
   <DockPanel Style='{StaticResource Row}'>
    <Button x:Name='Bg' DockPanel.Dock='Right' Width='46' Height='24' Padding='0' AutomationProperties.Name='Background colour' ToolTip='Colour behind letterboxed or transparent areas'/>
    <TextBlock Style='{StaticResource Label}' Text='Background'/>
   </DockPanel>
  </StackPanel></Border>

  <Border Style='{StaticResource Card}'><StackPanel>
   <CheckBox x:Name='Cover' Style='{StaticResource Switch}' Content='Pause when a window covers the screen'/>
   <CheckBox x:Name='Battery' Style='{StaticResource Switch}' Content='Pause on low battery' ToolTip='When unplugged and at 20 % or less, or with Battery saver on'/>
   <DockPanel Style='{StaticResource Row}'>
    <Border Style='{StaticResource Segs}'><StackPanel Orientation='Horizontal'>
     <RadioButton x:Name='MemAuto' GroupName='mem' Style='{StaticResource Seg}' Content='Auto' ToolTip='Picks the lightest option for each GIF'/>
     <RadioButton x:Name='MemSpeed' GroupName='mem' Style='{StaticResource Seg}' Content='Less CPU' ToolTip='Keeps every frame ready on the GPU'/>
     <RadioButton x:Name='MemSaver' GroupName='mem' Style='{StaticResource Seg}' Content='Less RAM' ToolTip='Decodes frames while playing'/>
    </StackPanel></Border>
    <TextBlock Style='{StaticResource Label}' Text='Memory'/>
   </DockPanel>
   <CheckBox x:Name='AutoStart' Style='{StaticResource Switch}' Content='Start with Windows'/>
  </StackPanel></Border>

  <TextBlock Foreground='$MUTED' FontSize='12' Margin='2,12,0,0' Text='Closing this window keeps the wallpaper playing.'/>
  <DockPanel Margin='2,6,2,0'>
   <TextBlock x:Name='Version' DockPanel.Dock='Right' Foreground='$MUTED' FontSize='12'/>
   <StackPanel Orientation='Horizontal' TextElement.FontSize='12'>
    <Button x:Name='Log' Style='{StaticResource Link}' Content='Open log'/>
    <TextBlock Foreground='$MUTED' Text='  &#183;  ' />
    <Button x:Name='Remove' Style='{StaticResource Link}' Content='Remove from this PC'/>
   </StackPanel>
  </DockPanel>
 </StackPanel>
</Border>";

        static readonly string[] MonitorValues = { "Primary", "Span", "Each" };
        static readonly string[] FitValues = { "Fill", "Fit", "Stretch", "Center" };
        static readonly int[] FpsValues = { 15, 24, 30, 60 };
        static readonly string[] MemoryValues = { "Auto", "Speed", "Saver" };

        readonly FrameworkElement root;
        readonly Border drop;
        readonly TextBlock hint, status, detail;
        readonly Ellipse dot;
        readonly Button toggle, bgButton, choose, noticeAction, noticeDismiss;
        readonly Border notice;
        readonly TextBlock noticeText;
        Action noticeHandler;
        readonly RadioButton[] mon, fit, fps, mem;
        readonly CheckBox cover, battery, autostart;
        readonly DispatcherTimer debounce;
        readonly bool dark;
        readonly Brush line, accent, muted;
        string bgHex, previewPath;
        bool loading = true, removed;

        public Panel()
        {
            Settings s = Program.Settings;
            dark = IsDarkTheme();
            string acc = AccentColor();
            Color ac = (Color)ColorConverter.ConvertFromString(acc);
            bool lightAccent = 0.2126 * ac.R + 0.7152 * ac.G + 0.0722 * ac.B > 150;
            string xaml = Xaml
                .Replace("$BG", dark ? "#202020" : "#F3F3F3").Replace("$CARD", dark ? "#2B2B2B" : "#FFFFFF")
                .Replace("$LINE", dark ? "#3A3A3A" : "#E3E3E3").Replace("$FG", dark ? "#F3F3F3" : "#1B1B1B")
                .Replace("$MUTED", dark ? "#A6A6A6" : "#5F5F5F").Replace("$BTN", dark ? "#373737" : "#FBFBFB")
                .Replace("$SEGBG", dark ? "#1B1B1B" : "#EAEAEA").Replace("$SEGON", dark ? "#454545" : "#FFFFFF")
                .Replace("$PREVIEW", dark ? "#181818" : "#E9E9E9")
                .Replace("$ACCENT", acc).Replace("$ONACCENT", lightAccent ? "#000000" : "#FFFFFF");
            root = (FrameworkElement)XamlReader.Parse(xaml);

            Title = "GIF Live Wallpaper";
            Width = 480;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            UseLayoutRounding = true;
            AllowDrop = true;
            Background = ((Border)root).Background;
            Content = root;

            drop = (Border)Find("Drop");
            hint = (TextBlock)Find("Hint");
            status = (TextBlock)Find("Status");
            detail = (TextBlock)Find("Detail");
            dot = (Ellipse)Find("Dot");
            toggle = (Button)Find("Toggle");
            bgButton = (Button)Find("Bg");
            choose = (Button)Find("Choose");
            notice = (Border)Find("Notice");
            noticeText = (TextBlock)Find("NoticeText");
            noticeAction = (Button)Find("NoticeAction");
            noticeDismiss = (Button)Find("NoticeDismiss");
            mon = Radios("MonPrimary", "MonSpan", "MonEach");
            fit = Radios("FitFill", "FitFit", "FitStretch", "FitCenter");
            fps = Radios("Fps15", "Fps24", "Fps30", "Fps60");
            mem = Radios("MemAuto", "MemSpeed", "MemSaver");
            cover = (CheckBox)Find("Cover");
            battery = (CheckBox)Find("Battery");
            autostart = (CheckBox)Find("AutoStart");
            line = drop.BorderBrush;
            accent = Hex(acc);
            muted = detail.Foreground;
            ((TextBlock)Find("Version")).Text = "v" + Program.Version;

            // ---- current settings ----
            mon[Math.Max(0, Array.IndexOf(MonitorValues, s.MonitorMode))].IsChecked = true;
            fit[Math.Max(0, Array.IndexOf(FitValues, s.Fit))].IsChecked = true;
            int nearest = 0;
            for (int i = 1; i < FpsValues.Length; i++) if (Math.Abs(FpsValues[i] - s.MaxFps) < Math.Abs(FpsValues[nearest] - s.MaxFps)) nearest = i;
            fps[nearest].IsChecked = true;
            mem[Math.Max(0, Array.IndexOf(MemoryValues, s.Memory))].IsChecked = true;
            cover.IsChecked = s.PauseWhenCovered;
            battery.IsChecked = s.PauseOnLowBattery;
            autostart.IsChecked = AutoStart.IsOn;
            bgHex = s.Background;
            bgButton.Background = Hex(bgHex);

            // ---- behaviour: every change is saved and applied ~0.4 s later ----
            debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            debounce.Tick += delegate { debounce.Stop(); Apply(false); };
            RoutedEventHandler changed = delegate { Changed(); };
            foreach (RadioButton[] g in new[] { mon, fit, fps, mem }) foreach (RadioButton r in g) r.Checked += changed;
            cover.Checked += changed; cover.Unchecked += changed;
            battery.Checked += changed; battery.Unchecked += changed;
            autostart.Checked += delegate { SetAutoStart(true); };
            autostart.Unchecked += delegate { SetAutoStart(false); };

            toggle.Click += delegate { if (Program.Engine.Running) Program.Engine.Stop(); else Apply(true); };
            choose.Click += delegate { ChooseGif(); };
            drop.MouseLeftButtonUp += delegate { ChooseGif(); };
            drop.MouseEnter += delegate { drop.BorderBrush = muted; };   // the preview is clickable: show it
            drop.MouseLeave += delegate { drop.BorderBrush = line; };
            noticeAction.Click += delegate { Action a = noticeHandler; HideNotice(); if (a != null) a(); };
            noticeDismiss.Click += delegate { HideNotice(); };
            PreviewKeyDown += delegate(object o, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
            bgButton.Click += delegate { PickBackground(); };
            ((Button)Find("Log")).Click += delegate { OpenLog(); };
            ((Button)Find("Remove")).Click += delegate { Remove(); };

            DragEventHandler over = delegate(object o, DragEventArgs e)
            {
                bool ok = DroppedGif(e) != null;
                e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
                drop.BorderBrush = ok ? accent : line;
                e.Handled = true;
            };
            PreviewDragEnter += over;
            PreviewDragOver += over;
            PreviewDragLeave += delegate { drop.BorderBrush = line; };
            PreviewDrop += delegate(object o, DragEventArgs e)
            {
                drop.BorderBrush = line;
                string f = DroppedGif(e);
                e.Handled = true;
                if (f != null) Dispatcher.BeginInvoke(new Action(delegate { SetGif(f); }));   // let Explorer finish the drop first
            };
            SourceInitialized += delegate { Native.DarkTitleBar(new WindowInteropHelper(this).Handle, dark); };
            ContentRendered += delegate
            {
                if (!File.Exists(Program.Settings.GifPath)) Dispatcher.BeginInvoke(new Action(ChooseGif));   // first run
            };
            Closed += delegate { if (debounce.IsEnabled && !removed) { debounce.Stop(); Apply(false); } };

            UpdateStatus();
            loading = false;
        }

        // ---------------- actions ----------------

        void Changed()
        {
            if (loading) return;
            debounce.Stop();
            debounce.Start();
        }

        void ChooseGif()
        {
            OpenFileDialog dlg = new OpenFileDialog { Title = "Choose a GIF for your wallpaper", Filter = "GIF images (*.gif)|*.gif" };
            string cur = Program.Settings.GifPath;
            string samples = System.IO.Path.Combine(AppPaths.ExeDir, "gifs");
            if (File.Exists(cur)) { dlg.InitialDirectory = System.IO.Path.GetDirectoryName(cur); dlg.FileName = System.IO.Path.GetFileName(cur); }
            else if (Directory.Exists(samples)) dlg.InitialDirectory = samples;
            else dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (dlg.ShowDialog(this) == true) SetGif(dlg.FileName);
        }

        void SetGif(string file)
        {
            if (!File.Exists(file) || !IsGif(file))
            {
                ShowNotice("That file isn't a GIF. Pick a file ending in .gif.", "Choose GIF", ChooseGif, "Dismiss");
                return;
            }
            HideNotice();
            Program.Settings.GifPath = file;
            Apply(true);   // choosing a GIF always plays it right away

            // After the very first GIF plays, offer to keep it after a restart (asked once, inline).
            if (Program.Engine.Active && !Program.Settings.AskedAutoStart)
            {
                Program.Settings.AskedAutoStart = true;
                Program.SaveSettings();
                if (!AutoStart.IsOn)
                    ShowNotice("Keep this wallpaper after you restart your PC?", "Start with Windows",
                        delegate { autostart.IsChecked = true; }, "Not now");
            }
        }

        void Apply(bool start)
        {
            Settings s = Program.Settings;
            s.MonitorMode = MonitorValues[Checked(mon)];
            s.Fit = FitValues[Checked(fit)];
            s.MaxFps = FpsValues[Checked(fps)];
            s.Memory = MemoryValues[Checked(mem)];
            s.Background = bgHex;
            s.PauseWhenCovered = cover.IsChecked == true;
            s.PauseOnLowBattery = battery.IsChecked == true;
            Program.SaveSettings();

            if (start || Program.Engine.Running)
            {
                if (!File.Exists(s.GifPath)) { if (start) ChooseGif(); return; }
                Cursor = Cursors.Wait;
                try { Program.Engine.Start(s); } finally { Cursor = null; }
                if (!Program.Engine.Running && Program.Engine.LastError.Length > 0)
                    ShowNotice("This GIF couldn't be played. Try a different file.", "Choose GIF", ChooseGif, "Dismiss");
            }
            UpdateStatus();
        }

        void SetAutoStart(bool on)
        {
            if (loading) return;
            try { AutoStart.Set(on); }
            catch (Exception ex)
            {
                ShowNotice("Couldn't change Start with Windows: " + ex.Message, null, null, "Dismiss");
                loading = true; autostart.IsChecked = AutoStart.IsOn; loading = false;
            }
        }

        void PickBackground()
        {
            Color c = ((SolidColorBrush)Hex(bgHex)).Color;
            int r = c.R, g = c.G, b = c.B;
            if (!Native.PickColor(new WindowInteropHelper(this).Handle, ref r, ref g, ref b)) return;
            bgHex = string.Format("#{0:x2}{1:x2}{2:x2}", r, g, b);
            bgButton.Background = Hex(bgHex);
            Changed();
        }

        void OpenLog()
        {
            if (File.Exists(AppPaths.Log)) Process.Start("notepad.exe", "\"" + AppPaths.Log + "\"");
            else ShowNotice("Nothing has gone wrong so far, so there's no log yet.", null, null, "OK");
        }

        void ShowNotice(string text, string action, Action onAction, string dismiss)
        {
            noticeText.Text = text;
            noticeHandler = onAction;
            noticeAction.Content = action;
            noticeAction.Visibility = action == null ? Visibility.Collapsed : Visibility.Visible;
            noticeDismiss.Content = dismiss;
            notice.Visibility = Visibility.Visible;
        }

        void HideNotice()
        {
            notice.Visibility = Visibility.Collapsed;
            noticeHandler = null;
        }

        void Remove()
        {
            if (MessageBox.Show(this,
                    "Remove GIF Live Wallpaper from this PC?\n\n" +
                    "  • stops the wallpaper\n" +
                    "  • removes the Start with Windows entry\n" +
                    "  • deletes its hidden settings file\n\n" +
                    "Afterwards you can simply delete GifWallpaper.exe.",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            removed = true;
            debounce.Stop();
            Program.RemoveTraces();
            MessageBox.Show(this, "Done. Nothing is left on this PC except the exe itself:\n\n" + AppPaths.Exe, Title, MessageBoxButton.OK, MessageBoxImage.Information);
            Program.Quit();
        }

        public void UpdateStatus()
        {
            Engine e = Program.Engine;
            string gif = Program.Settings.GifPath;
            bool have = File.Exists(gif);
            status.Text = have ? System.IO.Path.GetFileName(gif) : "No GIF yet";
            if (e.Active)
            {
                dot.Fill = Hex("#2EA043");
                detail.Text = string.Format("{0} × {1} · {2} frame{3} · {4}", e.Width, e.Height, e.Frames, e.Frames == 1 ? "" : "s",
                    e.Cached ? "less CPU" : "less RAM");
                detail.ToolTip = "Playing. " + (e.Cached ? "Every frame is kept ready on the GPU, so it uses almost no CPU." : "Frames are decoded while playing, so it uses very little memory.");
            }
            else if (e.Retrying)
            {
                dot.Fill = Hex("#D29922");
                detail.Text = e.LastError;
            }
            else
            {
                dot.Fill = muted;
                detail.Text = e.LastError.Length > 0 ? "Couldn't play: " + e.LastError : have ? "Stopped" : "Choose a GIF and it starts playing";
            }
            toggle.Content = e.Running ? "Stop" : "Play";
            choose.Content = have ? "Change GIF" : "Choose GIF";
            toggle.IsEnabled = e.Running || have;
            ShowPreview(have ? gif : null);
        }

        // Static first frame, decoded small: the animation itself plays on the desktop.
        void ShowPreview(string file)
        {
            if (file == previewPath) return;
            previewPath = file;
            ImageBrush brush = null;
            if (file != null)
            {
                try
                {
                    BitmapImage bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(file);
                    bi.DecodePixelWidth = 880;
                    bi.CacheOption = BitmapCacheOption.OnLoad;   // reads the file once and closes it
                    bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bi.EndInit();
                    bi.Freeze();
                    brush = new ImageBrush(bi) { Stretch = Stretch.UniformToFill };
                    brush.Freeze();
                }
                catch { brush = null; }
            }
            drop.Background = brush ?? Hex(dark ? "#181818" : "#E9E9E9");
            hint.Visibility = brush != null ? Visibility.Collapsed : Visibility.Visible;
        }

        // ---------------- helpers ----------------

        object Find(string name) { return root.FindName(name); }

        RadioButton[] Radios(params string[] names)
        {
            RadioButton[] r = new RadioButton[names.Length];
            for (int i = 0; i < names.Length; i++) r[i] = (RadioButton)Find(names[i]);
            return r;
        }

        static int Checked(RadioButton[] group)
        {
            for (int i = 0; i < group.Length; i++) if (group[i].IsChecked == true) return i;
            return 0;
        }

        static bool IsGif(string f) { return string.Equals(System.IO.Path.GetExtension(f), ".gif", StringComparison.OrdinalIgnoreCase); }

        static string DroppedGif(DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            return files != null && files.Length > 0 && IsGif(files[0]) ? files[0] : null;
        }

        static Brush Hex(string hex)
        {
            Brush b;
            try { b = (Brush)new BrushConverter().ConvertFromString(hex); } catch { b = new SolidColorBrush(Colors.Black); }
            b.Freeze();
            return b;
        }

        static bool IsDarkTheme()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        // The Windows accent colour (stored as 0xAABBGGRR), or Windows blue.
        static string AccentColor()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    object v = k == null ? null : k.GetValue("AccentColor");
                    if (v is int)
                    {
                        int c = (int)v;
                        return string.Format("#{0:X2}{1:X2}{2:X2}", c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF);
                    }
                }
            }
            catch { }
            return "#0067C0";
        }
    }
}
