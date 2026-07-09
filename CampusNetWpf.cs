// CampusNetWpf.cs
// 苏大校园网自动登录器 —— 纯代码 WPF 单文件源码
//
// 编译示例（.NET Framework 4.x，使用 csc.exe）：
// csc.exe CampusNetWpf.cs /target:winexe /out:CampusNet.exe ^
//   /win32icon:app.ico /resource:suda-logo.png ^
//   /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\PresentationCore.dll" ^
//   /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\PresentationFramework.dll" ^
//   /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WindowsBase.dll" ^
//   /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll" ^
//   /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Windows.Forms.dll" ^
//   /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Drawing.dll"
//
// 运行时仅需同目录文件：accounts.txt、settings.txt（自动创建）
// suda-logo.png 和 app.ico 已嵌入 exe，无需外部文件

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace CampusNetWpf
{
    public class Program
    {
        private static Mutex _appMutex;

        [STAThread]
        public static void Main()
        {
            bool createdNew;
            _appMutex = new Mutex(true, "CampusNet_SingleInstance", out createdNew);
            if (!createdNew)
            {
                // 已有实例运行，静默退出，不弹窗打扰用户
                return;
            }

            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                MainWindow window = new MainWindow();
                window.Show();
                app.Run();
            }
            finally
            {
                if (_appMutex != null)
                    _appMutex.ReleaseMutex();
            }
        }
    }

    public class Account
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public bool Enabled { get; set; }

        public string MaskedUsername
        {
            get
            {
                if (string.IsNullOrEmpty(Username))
                    return "";
                // 学号格式如 2023000001@zgyd，取 @ 前部分的最后两位数字
                string id = Username.Split('@')[0];
                if (id.Length <= 2)
                    return id;
                return id.Substring(id.Length - 2);
            }
        }
    }

    public class TimedWebClient : WebClient
    {
        protected override WebRequest GetWebRequest(Uri address)
        {
            WebRequest request = base.GetWebRequest(address);
            request.Timeout = 5000;
            return request;
        }
    }

    public class ToggleSwitch : ContentControl
    {
        private Border _track;
        private Ellipse _thumb;
        private TranslateTransform _thumbTransform;

        public ToggleSwitch()
        {
            this.Width = 40;
            this.Height = 22;
            this.Background = Brushes.Transparent;
            this.Cursor = System.Windows.Input.Cursors.Hand;
            BuildVisual();
            this.Loaded += (s, e) => UpdateVisual(false);
            this.MouseLeftButtonDown += ToggleSwitch_MouseLeftButtonDown;
        }

        public bool IsOn
        {
            get { return (bool)GetValue(IsOnProperty); }
            set { SetValue(IsOnProperty, value); }
        }

        public static readonly DependencyProperty IsOnProperty =
            DependencyProperty.Register(
                "IsOn",
                typeof(bool),
                typeof(ToggleSwitch),
                new PropertyMetadata(false, OnIsOnChanged));

        public Brush OnBrush
        {
            get { return (Brush)GetValue(OnBrushProperty); }
            set { SetValue(OnBrushProperty, value); }
        }

        public static readonly DependencyProperty OnBrushProperty =
            DependencyProperty.Register(
                "OnBrush",
                typeof(Brush),
                typeof(ToggleSwitch),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(30, 142, 62)), OnBrushChanged));

        public Brush OffBrush
        {
            get { return (Brush)GetValue(OffBrushProperty); }
            set { SetValue(OffBrushProperty, value); }
        }

        public static readonly DependencyProperty OffBrushProperty =
            DependencyProperty.Register(
                "OffBrush",
                typeof(Brush),
                typeof(ToggleSwitch),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(205, 206, 209)), OnBrushChanged));

        public event EventHandler Toggled;

        private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ToggleSwitch sw = (ToggleSwitch)d;
            sw.UpdateVisual(true);
            if (sw.Toggled != null)
                sw.Toggled(sw, EventArgs.Empty);
        }

        private static void OnBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ToggleSwitch sw = (ToggleSwitch)d;
            sw.UpdateVisual(false);
        }

        private void BuildVisual()
        {
            _track = new Border
            {
                Width = 40,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = OffBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            _thumb = new Ellipse
            {
                Width = 18,
                Height = 18,
                Fill = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            _thumbTransform = new TranslateTransform(2, 0);
            _thumb.RenderTransform = _thumbTransform;

            Grid grid = new Grid();
            grid.Children.Add(_track);
            grid.Children.Add(_thumb);

            this.Content = grid;
        }

        private void UpdateVisual(bool animate)
        {
            if (_track == null || _thumbTransform == null)
                return;

            _track.Background = IsOn ? OnBrush : OffBrush;

            double targetX = IsOn ? 20 : 2;
            if (animate && this.IsLoaded)
            {
                DoubleAnimation anim = new DoubleAnimation(
                    _thumbTransform.X,
                    targetX,
                    new Duration(TimeSpan.FromMilliseconds(150)));
                _thumbTransform.BeginAnimation(TranslateTransform.XProperty, anim);
            }
            else
            {
                _thumbTransform.BeginAnimation(TranslateTransform.XProperty, null);
                _thumbTransform.X = targetX;
            }
        }

        private void ToggleSwitch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            IsOn = !IsOn;
        }
    }

    public partial class MainWindow : Window
    {
        // Paths
        private string AccountsFile
        {
            get { return IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "accounts.txt"); }
        }

        private string CurrentFile
        {
            get { return IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "current.txt"); }
        }

        private string SettingsFile
        {
            get { return IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt"); }
        }

        // Config & state
        private const string DefaultGateway = "10.9.1.3";
        private const int MaxLogLines = 200;

        private bool _isConnected = false;
        private bool _autoReconnect = true;
        private bool _autoHotspot = false;
        private bool _autoStart = false;
        private int _intervalSeconds = 3;
        private string _gateway = DefaultGateway;
        private bool _allowClose = false;
        private List<Account> _accounts = new List<Account>();
        private Account _currentAccount = null;

        // Runtime flags
        private bool _syncingPass = false;

        // Timers / workers
        private DispatcherTimer _timer;
        private DispatcherTimer _clockTimer;
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private BackgroundWorker _detectWorker;
        private BackgroundWorker _loginWorker;

        // Main UI
        private Border _mainPanel;
        private Border _accountPanel;

        private Border _statusCard;
        private TextBlock _statusTitle;
        private TextBlock _statusSubtitle;
        private Ellipse _statusIconBg;
        private System.Windows.Shapes.Path _statusIconPath;
        private Border _accountCapsule;
        private TextBlock _capsuleAccountText;
        private Button _statusActionButton;

        private ToggleSwitch _reconnectToggle;
        private ToggleSwitch _hotspotToggle;
        private ToggleSwitch _autoStartToggle;
        private TextBox _intervalInput;

        private TextBox _logTextBox;
        private ScrollViewer _logScroll;
        private TextBlock _footerTime;

        // Account page UI
        private StackPanel _accountListPanel;
        private TextBox _accUserInput;
        private PasswordBox _accPassInput;

        public MainWindow()
        {
            InitializeWindow();
            LoadSettings();
            LoadAccounts();
            BuildUI();
            BuildTrayIcon();
            SetupTimer();

            _reconnectToggle.IsOn = _autoReconnect;
            _hotspotToggle.IsOn = _autoHotspot;
            _autoStartToggle.IsOn = _autoStart;
            _intervalInput.Text = _intervalSeconds.ToString();

            UpdateUIState();
            AddLog("程序启动");
            RunDetection();
        }

        private void InitializeWindow()
        {
            this.Width = 380;
            this.Height = 580;
            this.MinWidth = 380;
            this.MinHeight = 580;
            this.MaxWidth = 380;
            this.MaxHeight = 580;
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.ResizeMode = ResizeMode.NoResize;
            this.Background = Brushes.Transparent;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Title = "苏大校园网自动登录器";
            this.Closing += MainWindow_Closing;
        }

        #region Build UI

        private void BuildUI()
        {
            Border outerBorder = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromRgb(242, 243, 245)),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 30,
                    ShadowDepth = 0,
                    Opacity = 0.25
                }
            };

            Grid rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });

            // 显式裁剪，让标题栏（红色）和底部也按圆角裁切
            // 窗口固定 380x580，立即设置裁剪避免首帧渲染时四角外露
            rootGrid.Clip = new RectangleGeometry
            {
                RadiusX = 16,
                RadiusY = 16,
                Rect = new Rect(0, 0, 380, 580)
            };
            rootGrid.SizeChanged += (s, e) =>
            {
                rootGrid.Clip = new RectangleGeometry
                {
                    RadiusX = 16,
                    RadiusY = 16,
                    Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height)
                };
            };

            // Title bar
            Grid titleBar = BuildTitleBar();
            rootGrid.Children.Add(titleBar);

            // Main content (no page scrolling, only log scroll)
            _mainPanel = BuildMainPanel();
            Grid.SetRow(_mainPanel, 1);
            rootGrid.Children.Add(_mainPanel);

            // Account panel overlay
            _accountPanel = BuildAccountPanel();
            _accountPanel.Visibility = Visibility.Collapsed;
            Grid.SetRow(_accountPanel, 1);
            rootGrid.Children.Add(_accountPanel);

            // Footer
            Border footer = BuildFooter();
            Grid.SetRow(footer, 2);
            rootGrid.Children.Add(footer);

            outerBorder.Child = rootGrid;
            this.Content = outerBorder;
        }

        private Grid BuildTitleBar()
        {
            Grid grid = new Grid();
            grid.Background = new SolidColorBrush(Color.FromRgb(139, 26, 26));
            grid.MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: logo + texts
            StackPanel leftPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };

            Ellipse logoEllipse = new Ellipse { Width = 24, Height = 24 };
            RenderOptions.SetBitmapScalingMode(logoEllipse, BitmapScalingMode.HighQuality);
            try
            {
                // 从嵌入资源加载校徽
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                Stream logoStream = GetEmbeddedResource("suda-logo.png");
                if (logoStream != null)
                {
                    bi.StreamSource = logoStream;
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.DecodePixelWidth = 96;
                    bi.EndInit();
                    bi.Freeze();
                    logoEllipse.Fill = new ImageBrush(bi) { Stretch = Stretch.UniformToFill };
                }
                else
                {
                    logoEllipse.Fill = new SolidColorBrush(Color.FromRgb(200, 200, 200));
                }
            }
            catch
            {
                logoEllipse.Fill = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            }
            leftPanel.Children.Add(logoEllipse);

            StackPanel textPanel = new StackPanel
            {
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock titleText = new TextBlock
            {
                Text = "苏大校园网",
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.Bold
            };
            TextBlock subText = new TextBlock
            {
                Text = "自动登录器",
                Foreground = new SolidColorBrush(Color.FromArgb(178, 255, 255, 255)),
                FontSize = 11
            };
            textPanel.Children.Add(titleText);
            textPanel.Children.Add(subText);
            leftPanel.Children.Add(textPanel);

            grid.Children.Add(leftPanel);

            // Right buttons: minimize to tray (—) + exit (✕)
            StackPanel rightPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            Button closeBtn = CreateCircleButton(
                "✕", 26,
                new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
                Brushes.White, 13);
            closeBtn.ToolTip = "最小化到托盘";
            closeBtn.Click += (s, e) => this.Hide();
            rightPanel.Children.Add(closeBtn);

            Grid.SetColumn(rightPanel, 1);
            grid.Children.Add(rightPanel);

            return grid;
        }

        private Button CreateCircleButton(string text, double size, Brush bg, Brush fg, double fontSize)
        {
            Button btn = new Button
            {
                Width = size,
                Height = size,
                Background = bg,
                Foreground = fg,
                BorderThickness = new Thickness(0),
                FontSize = fontSize,
                Content = text,
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            btn.Template = CreateCircleButtonTemplate();
            return btn;
        }

        private ControlTemplate CreateCircleButtonTemplate()
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(100));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0));

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            template.VisualTree = border;
            return template;
        }

        private Border BuildMainPanel()
        {
            Border panel = new Border { Background = Brushes.Transparent };
            StackPanel sp = new StackPanel();
            sp.Children.Add(BuildStatusCard());
            sp.Children.Add(BuildSettingsCard());
            sp.Children.Add(BuildLogCard());
            panel.Child = sp;
            return panel;
        }

        private Border BuildStatusCard()
        {
            _statusCard = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = Brushes.White,
                Padding = new Thickness(16, 16, 16, 16),
                Margin = new Thickness(16, 8, 16, 0)
            };

            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Top row
            Grid topGrid = new Grid();
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel leftStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid iconGrid = new Grid { Width = 44, Height = 44, Margin = new Thickness(0, 0, 12, 0) };
            _statusIconBg = new Ellipse { Width = 44, Height = 44 };
            iconGrid.Children.Add(_statusIconBg);
            _statusIconPath = new System.Windows.Shapes.Path
            {
                Width = 24,
                Height = 24,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconGrid.Children.Add(_statusIconPath);
            leftStack.Children.Add(iconGrid);

            StackPanel textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _statusTitle = new TextBlock
            {
                FontSize = 20,
                FontWeight = FontWeights.Bold
            };
            _statusSubtitle = new TextBlock
            {
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0)
            };
            textStack.Children.Add(_statusTitle);
            textStack.Children.Add(_statusSubtitle);
            leftStack.Children.Add(textStack);

            topGrid.Children.Add(leftStack);

            // Account capsule
            _accountCapsule = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 10, 4),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            _capsuleAccountText = new TextBlock
            {
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                LineHeight = 14
            };
            _accountCapsule.Child = _capsuleAccountText;
            _accountCapsule.MouseLeftButtonDown += (s, e) => ShowAccountPage();
            Grid.SetColumn(_accountCapsule, 1);
            topGrid.Children.Add(_accountCapsule);

            grid.Children.Add(topGrid);

            // Action button
            _statusActionButton = new Button
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Height = 34,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _statusActionButton.Click += StatusActionButton_Click;
            Grid.SetRow(_statusActionButton, 2);
            grid.Children.Add(_statusActionButton);

            _statusCard.Child = grid;
            return _statusCard;
        }

        private Border BuildSettingsCard()
        {
            Border card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(14),
                BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 227)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 16, 16, 16),
                Margin = new Thickness(16, 8, 16, 0)
            };

            StackPanel sp = new StackPanel();

            Grid titleRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock title = new TextBlock
            {
                Text = "⚙ 设置",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
                VerticalAlignment = VerticalAlignment.Center
            };
            titleRow.Children.Add(title);

            Border manageLink = new Border
            {
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(4, 2, 4, 2),
                CornerRadius = new CornerRadius(4)
            };
            TextBlock manageText = new TextBlock
            {
                Text = "管理账号 ›",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(139, 26, 26)),
                VerticalAlignment = VerticalAlignment.Center
            };
            manageLink.Child = manageText;
            manageLink.MouseLeftButtonDown += (s, e) => ShowAccountPage();
            Grid.SetColumn(manageLink, 1);
            titleRow.Children.Add(manageLink);

            sp.Children.Add(titleRow);

            sp.Children.Add(BuildSettingRow("自动重连", out _reconnectToggle));
            _reconnectToggle.Toggled += (s, e) =>
            {
                _autoReconnect = _reconnectToggle.IsOn;
                SaveSettings();
            };

            sp.Children.Add(BuildSettingRow("自动热点", out _hotspotToggle));
            _hotspotToggle.Toggled += (s, e) =>
            {
                _autoHotspot = _hotspotToggle.IsOn;
                SaveSettings();
            };

            sp.Children.Add(BuildSettingRow("开机自启", out _autoStartToggle));
            _autoStartToggle.Toggled += (s, e) =>
            {
                _autoStart = _autoStartToggle.IsOn;
                ApplyAutoStart();
                SaveSettings();
            };

            // 检测间隔
            Grid row3 = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            row3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row3.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock label = new TextBlock
            {
                Text = "检测间隔（秒）",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
                VerticalAlignment = VerticalAlignment.Center
            };
            _intervalInput = new TextBox
            {
                Width = 60,
                Height = 28,
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 227)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(248, 249, 250))
            };
            _intervalInput.TextChanged += IntervalInput_TextChanged;

            row3.Children.Add(label);
            Grid.SetColumn(_intervalInput, 1);
            row3.Children.Add(_intervalInput);
            sp.Children.Add(row3);

            card.Child = sp;
            return card;
        }

        private UIElement BuildSettingRow(string label, out ToggleSwitch toggle)
        {
            Grid row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock tb = new TextBlock
            {
                Text = label,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(60, 64, 67)),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(tb);

            // toggle 颜色固定：开启=绿色，关闭=浅灰
            toggle = new ToggleSwitch();
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);

            return row;
        }

        private Border BuildLogCard()
        {
            Border card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12),
                Margin = new Thickness(16, 8, 16, 8)
            };

            StackPanel sp = new StackPanel();

            Grid titleRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock title = new TextBlock
            {
                Text = "运行日志",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
                VerticalAlignment = VerticalAlignment.Center
            };
            titleRow.Children.Add(title);

            Border clearLink = new Border
            {
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(4, 2, 4, 2),
                CornerRadius = new CornerRadius(4)
            };
            TextBlock clearText = new TextBlock
            {
                Text = "清空 ›",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(139, 26, 26)),
                VerticalAlignment = VerticalAlignment.Center
            };
            clearLink.Child = clearText;
            clearLink.MouseLeftButtonDown += (s, e) =>
            {
                _logTextBox.Text = "";
            };
            Grid.SetColumn(clearLink, 1);
            titleRow.Children.Add(clearLink);

            sp.Children.Add(titleRow);

            Border logBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Height = 110,
                BorderBrush = new SolidColorBrush(Color.FromRgb(234, 236, 238)),
                BorderThickness = new Thickness(1)
            };

            _logScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            // 使用只读 TextBox 替代 TextBlock，支持选中复制
            _logTextBox = new TextBox
            {
                FontFamily = new FontFamily("Consolas, Microsoft YaHei Mono, monospace"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
                TextWrapping = TextWrapping.Wrap,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _logScroll.Content = _logTextBox;
            logBorder.Child = _logScroll;
            sp.Children.Add(logBorder);

            card.Child = sp;
            return card;
        }

        private Border BuildFooter()
        {
            Border footer = new Border
            {
                Height = 32,
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(234, 236, 238)),
                Background = new SolidColorBrush(Color.FromRgb(242, 243, 245))
            };
            _footerTime = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(154, 160, 166)),
                FontFamily = new FontFamily("Consolas, monospace"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            footer.Child = _footerTime;
            return footer;
        }

        private Border BuildAccountPanel()
        {
            Border panel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(242, 243, 245)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(0)
            };

            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Title bar
            Border titleBar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(139, 26, 26)),
                Height = 40
            };
            Grid titleGrid = new Grid();
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Button backBtn = CreateCircleButton(
                "←", 26,
                new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
                Brushes.White, 13);
            backBtn.Margin = new Thickness(8, 0, 0, 0);
            backBtn.HorizontalAlignment = HorizontalAlignment.Left;
            backBtn.Click += (s, e) => ShowMainPage();
            titleGrid.Children.Add(backBtn);

            TextBlock title = new TextBlock
            {
                Text = "账号管理",
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(title, 1);
            titleGrid.Children.Add(title);
            titleBar.Child = titleGrid;

            grid.Children.Add(titleBar);

            // Content
            ScrollViewer sv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(16, 12, 16, 12)
            };
            StackPanel sp = new StackPanel();

            // Add section
            Border addCard = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };
            StackPanel addSp = new StackPanel();

            TextBlock addTitle = new TextBlock
            {
                Text = "添加/编辑账号",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            addSp.Children.Add(addTitle);

            _accUserInput = new TextBox
            {
                Height = 32,
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 13,
                Padding = new Thickness(6, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 227)),
                BorderThickness = new Thickness(1)
            };
            addSp.Children.Add(_accUserInput);

            Grid passGrid = new Grid { Height = 32, Margin = new Thickness(0, 0, 0, 10) };
            passGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            passGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _accPassInput = new PasswordBox
            {
                Height = 32,
                FontSize = 13,
                Padding = new Thickness(6, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 227)),
                BorderThickness = new Thickness(1)
            };
            Grid.SetColumn(_accPassInput, 0);
            passGrid.Children.Add(_accPassInput);

            TextBox passVisibleBox = new TextBox
            {
                Height = 32,
                FontSize = 13,
                Padding = new Thickness(6, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 227)),
                BorderThickness = new Thickness(1),
                Visibility = Visibility.Collapsed
            };
            Grid.SetColumn(passVisibleBox, 0);
            passGrid.Children.Add(passVisibleBox);

            Button showPassBtn = new Button
            {
                Content = "显示",
                Width = 44,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 227)),
                BorderThickness = new Thickness(1),
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            Grid.SetColumn(showPassBtn, 1);
            showPassBtn.Click += (s, e) =>
            {
                if (passVisibleBox.Visibility == Visibility.Collapsed)
                {
                    passVisibleBox.Text = _accPassInput.Password;
                    passVisibleBox.Visibility = Visibility.Visible;
                    _accPassInput.Visibility = Visibility.Collapsed;
                    showPassBtn.Content = "隐藏";
                }
                else
                {
                    _accPassInput.Password = passVisibleBox.Text;
                    passVisibleBox.Visibility = Visibility.Collapsed;
                    _accPassInput.Visibility = Visibility.Visible;
                    showPassBtn.Content = "显示";
                }
            };
            // 双向同步：使用 _syncingPass 防止递归触发导致光标跳动
            _accPassInput.PasswordChanged += (s, e) =>
            {
                if (_syncingPass) return;
                _syncingPass = true;
                passVisibleBox.Text = _accPassInput.Password;
                _syncingPass = false;
            };
            passVisibleBox.TextChanged += (s, e) =>
            {
                if (_syncingPass) return;
                _syncingPass = true;
                _accPassInput.Password = passVisibleBox.Text;
                _syncingPass = false;
            };
            passGrid.Children.Add(showPassBtn);
            addSp.Children.Add(passGrid);

            Button addBtn = new Button
            {
                Content = "保存账号",
                Height = 34,
                Background = new SolidColorBrush(Color.FromRgb(139, 26, 26)),
                Foreground = Brushes.White,
                FontSize = 13,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            addBtn.Click += AddAccount_Click;
            addSp.Children.Add(addBtn);

            addCard.Child = addSp;
            sp.Children.Add(addCard);

            // List section
            Border listCard = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16)
            };
            StackPanel listSp = new StackPanel();
            TextBlock listTitle = new TextBlock
            {
                Text = "账号列表",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            listSp.Children.Add(listTitle);
            _accountListPanel = new StackPanel();
            listSp.Children.Add(_accountListPanel);
            listCard.Child = listSp;
            sp.Children.Add(listCard);

            sv.Content = sp;
            Grid.SetRow(sv, 1);
            grid.Children.Add(sv);

            panel.Child = grid;
            return panel;
        }

        #endregion

        #region Interaction

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                this.Hide();
            }
        }

        private void StatusActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected)
                RunDetection();
            else
                DoLogin();
        }

        private void IntervalInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            int val;
            if (int.TryParse(_intervalInput.Text, out val) && val >= 1 && val <= 3600)
            {
                _intervalSeconds = val;
                if (_timer != null)
                    _timer.Interval = TimeSpan.FromSeconds(val);
                SaveSettings();
            }
        }

        private void ShowAccountPage()
        {
            _mainPanel.Visibility = Visibility.Collapsed;
            _accountPanel.Visibility = Visibility.Visible;
            RefreshAccountList();
        }

        private void ShowMainPage()
        {
            _accountPanel.Visibility = Visibility.Collapsed;
            _mainPanel.Visibility = Visibility.Visible;
            UpdateUIState();
        }

        private void AddAccount_Click(object sender, RoutedEventArgs e)
        {
            string user = _accUserInput.Text.Trim();
            string pass = _accPassInput.Password;
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                System.Windows.MessageBox.Show("请输入账号和密码");
                return;
            }

            Account existing = _accounts.Find(a => a.Username == user);
            if (existing != null)
            {
                existing.Password = pass;
            }
            else
            {
                _accounts.Add(new Account { Username = user, Password = pass, Enabled = true });
                if (_currentAccount == null)
                    _currentAccount = _accounts[0];
            }

            SaveAccounts();
            _accUserInput.Clear();
            _accPassInput.Clear();
            RefreshAccountList();
            UpdateUIState();
        }

        private void RefreshAccountList()
        {
            _accountListPanel.Children.Clear();
            foreach (Account acc in _accounts)
            {
                Border itemBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8)
            };

                StackPanel itemSp = new StackPanel();

                // Row 1: username + buttons
                Grid itemGrid = new Grid();
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                StackPanel left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                TextBlock userText = new TextBlock
                {
                    Text = acc.Username,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold
                };
                TextBlock statusText = new TextBlock
                {
                    Text = acc == _currentAccount ? "当前账号" : "",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(30, 142, 62))
                };
                left.Children.Add(userText);
                left.Children.Add(statusText);
                itemGrid.Children.Add(left);

                StackPanel right = new StackPanel { Orientation = Orientation.Horizontal };

                Button setBtn = new Button
                {
                    Content = "设为当前",
                    FontSize = 11,
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = Brushes.Transparent,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(139, 26, 26)),
                    BorderThickness = new Thickness(1),
                    Foreground = new SolidColorBrush(Color.FromRgb(139, 26, 26)),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                Account captured = acc;
                setBtn.Click += (s, ev) =>
                {
                    _currentAccount = captured;
                    SaveAccounts();
                    RefreshAccountList();
                    UpdateUIState();
                };
                right.Children.Add(setBtn);

                Button delBtn = new Button
                {
                    Content = "删除",
                    FontSize = 11,
                    Padding = new Thickness(8, 2, 8, 2),
                    Background = new SolidColorBrush(Color.FromRgb(139, 26, 26)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                delBtn.Click += (s, ev) =>
                {
                    _accounts.Remove(captured);
                    if (_currentAccount == captured)
                        _currentAccount = null;
                    if (_currentAccount == null && _accounts.Count > 0)
                        _currentAccount = _accounts[0];
                    SaveAccounts();
                    RefreshAccountList();
                    UpdateUIState();
                };
                right.Children.Add(delBtn);

                Grid.SetColumn(right, 1);
                itemGrid.Children.Add(right);
                itemSp.Children.Add(itemGrid);

                // Row 2: password visibility toggle
                Grid passRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                passRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                passRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                TextBlock passLabel = new TextBlock
                {
                    Text = "密码: " + new string('\u2022', Math.Min(acc.Password.Length, 8)),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                passRow.Children.Add(passLabel);

                TextBlock passVisible = new TextBlock
                {
                    Text = "密码: " + acc.Password,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                passRow.Children.Add(passVisible);

                Button togglePassBtn = new Button
                {
                    Content = "显示密码",
                    FontSize = 10,
                    Padding = new Thickness(6, 1, 6, 1),
                    Background = Brushes.Transparent,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                    BorderThickness = new Thickness(1),
                    Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                Grid.SetColumn(togglePassBtn, 1);
                togglePassBtn.Click += (s, ev) =>
                {
                    if (passVisible.Visibility == Visibility.Collapsed)
                    {
                        passLabel.Visibility = Visibility.Collapsed;
                        passVisible.Visibility = Visibility.Visible;
                        togglePassBtn.Content = "隐藏密码";
                    }
                    else
                    {
                        passLabel.Visibility = Visibility.Visible;
                        passVisible.Visibility = Visibility.Collapsed;
                        togglePassBtn.Content = "显示密码";
                    }
                };
                passRow.Children.Add(togglePassBtn);
                itemSp.Children.Add(passRow);

                itemBorder.Child = itemSp;
                _accountListPanel.Children.Add(itemBorder);
            }

            if (_accounts.Count == 0)
            {
                TextBlock empty = new TextBlock
                {
                    Text = "暂无账号，请在上方添加",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(154, 160, 166)),
                    Margin = new Thickness(0, 8, 0, 0)
                };
                _accountListPanel.Children.Add(empty);
            }
        }

        #endregion

        #region State & Visuals

        private void UpdateUIState()
        {
            if (_isConnected)
            {
                _statusCard.Background = Brushes.White;

                _statusTitle.Text = "已连接";
                _statusTitle.Foreground = new SolidColorBrush(Color.FromRgb(30, 142, 62));
                _statusSubtitle.Text = "网络已连接";
                _statusSubtitle.Foreground = new SolidColorBrush(Color.FromRgb(154, 160, 166));

                _statusIconBg.Fill = new SolidColorBrush(Color.FromRgb(230, 244, 234));
                _statusIconPath.Fill = new SolidColorBrush(Color.FromRgb(30, 142, 62));
                _statusIconPath.Stroke = null;
                _statusIconPath.StrokeThickness = 0;
                _statusIconPath.Data = Geometry.Parse(
                    "M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z");

                _accountCapsule.Background = new SolidColorBrush(Color.FromRgb(248, 249, 250));
                _accountCapsule.BorderBrush = new SolidColorBrush(Color.FromRgb(234, 236, 238));
                _capsuleAccountText.Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104));

                _statusActionButton.Content = "重新连接";
                _statusActionButton.Background = Brushes.Transparent;
                _statusActionButton.Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104));
                _statusActionButton.BorderBrush = new SolidColorBrush(Color.FromRgb(205, 206, 209));
                _statusActionButton.BorderThickness = new Thickness(1);
            }
            else
            {
                // 统一使用 (139,26,26) 作为断开状态主色
                _statusCard.Background = new SolidColorBrush(Color.FromRgb(139, 26, 26));

                _statusTitle.Text = "已断开";
                _statusTitle.Foreground = Brushes.White;
                _statusSubtitle.Text = "网络连接已断开";
                _statusSubtitle.Foreground = new SolidColorBrush(Color.FromArgb(166, 255, 255, 255));

                _statusIconBg.Fill = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                _statusIconPath.Fill = Brushes.Transparent;
                _statusIconPath.Stroke = new SolidColorBrush(Color.FromRgb(232, 163, 23));
                _statusIconPath.StrokeThickness = 2;
                _statusIconPath.Data = Geometry.Parse("M12 2L2 22h20L12 2z");

                _accountCapsule.Background = new SolidColorBrush(Color.FromArgb(76, 255, 255, 255));
                _accountCapsule.BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255));
                _capsuleAccountText.Foreground = Brushes.White;

                _statusActionButton.Content = "立即连接";
                _statusActionButton.Background = Brushes.White;
                _statusActionButton.Foreground = new SolidColorBrush(Color.FromRgb(139, 26, 26));
                _statusActionButton.BorderBrush = Brushes.White;
                _statusActionButton.BorderThickness = new Thickness(1);
            }

            if (_currentAccount != null)
                _capsuleAccountText.Text = string.Format("当前账号\n**{0}", _currentAccount.MaskedUsername);
            else
                _capsuleAccountText.Text = "当前账号\n未设置";

            // toggle 颜色固定为绿色，不随连接状态变化
        }

        #endregion

        #region Data persistence

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFile))
                    return;

                foreach (string line in File.ReadAllLines(SettingsFile))
                {
                    string[] parts = line.Split('=');
                    if (parts.Length != 2)
                        continue;

                    string key = parts[0].Trim();
                    string val = parts[1].Trim();
                    if (key == "AutoReconnect")
                        bool.TryParse(val, out _autoReconnect);
                    else if (key == "AutoHotspot")
                        bool.TryParse(val, out _autoHotspot);
                    else if (key == "AutoStart")
                        bool.TryParse(val, out _autoStart);
                    else if (key == "Interval")
                    {
                        int tmp;
                        if (int.TryParse(val, out tmp) && tmp >= 1 && tmp <= 3600)
                            _intervalSeconds = tmp;
                    }
                    else if (key == "Gateway")
                    {
                        if (!string.IsNullOrWhiteSpace(val))
                            _gateway = val.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("[设置] 读取失败: " + ex.Message);
            }
        }

        private void SaveSettings()
        {
            try
            {
                List<string> lines = new List<string>();
                lines.Add(string.Format("AutoReconnect={0}", _autoReconnect));
                lines.Add(string.Format("AutoHotspot={0}", _autoHotspot));
                lines.Add(string.Format("AutoStart={0}", _autoStart));
                lines.Add(string.Format("Interval={0}", _intervalSeconds));
                lines.Add(string.Format("Gateway={0}", _gateway));
                File.WriteAllLines(SettingsFile, lines);
            }
            catch (Exception ex)
            {
                AddLog("[设置] 保存失败: " + ex.Message);
            }
        }

        private void LoadAccounts()
        {
            try
            {
                _accounts.Clear();
                if (File.Exists(AccountsFile))
                {
                    foreach (string line in File.ReadAllLines(AccountsFile))
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed))
                            continue;

                        string[] parts = trimmed.Split('|');
                        if (parts.Length >= 2)
                        {
                            _accounts.Add(new Account
                            {
                                Username = parts[0],
                                Password = parts[1],
                                Enabled = true
                            });
                        }
                    }
                }

                string currentUser = "";
                if (File.Exists(CurrentFile))
                    currentUser = File.ReadAllText(CurrentFile).Trim();

                _currentAccount = _accounts.Find(a => a.Username == currentUser);
                if (_currentAccount == null && _accounts.Count > 0)
                    _currentAccount = _accounts[0];
            }
            catch (Exception ex)
            {
                AddLog("[账号] 读取失败: " + ex.Message);
            }
        }

        private void SaveAccounts()
        {
            try
            {
                List<string> lines = new List<string>();
                foreach (Account acc in _accounts)
                {
                    lines.Add(string.Format("{0}|{1}", acc.Username, acc.Password));
                }
                File.WriteAllLines(AccountsFile, lines);

                if (_currentAccount != null)
                    File.WriteAllText(CurrentFile, _currentAccount.Username);
                else
                    File.WriteAllText(CurrentFile, "");
            }
            catch (Exception ex)
            {
                AddLog("[账号] 保存失败: " + ex.Message);
            }
        }

        private void ApplyAutoStart()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    if (_autoStart)
                    {
                        string exePath = Assembly.GetEntryAssembly().Location;
                        if (!string.IsNullOrEmpty(exePath))
                            key.SetValue("CampusNetWpf", exePath);
                    }
                    else
                    {
                        key.DeleteValue("CampusNetWpf", false);
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("[自启] 设置失败: " + ex.Message);
            }
        }

        #endregion

        #region Network logic

        private void SetupTimer()
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(_intervalSeconds);
            _timer.Tick += (s, e) =>
            {
                RunDetection();
                UpdateFooterTime();
            };
            _timer.Start();

            _clockTimer = new DispatcherTimer();
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += (s, e) => UpdateFooterTime();
            _clockTimer.Start();
        }

        private void UpdateFooterTime()
        {
            _footerTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        // 网络检测：访问 http://10.9.1.3/?isReback=1，提取 uid 判断登录状态
        // 修复：StreamReader.ReadToEnd() 完整读取 + KeepAlive=false + 双引号兼容
        private bool Online()
        {
            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(
                    string.Format("http://{0}/?isReback=1", _gateway));
                r.Timeout = 5000;
                r.KeepAlive = false;
                using (HttpWebResponse x = (HttpWebResponse)r.GetResponse())
                using (StreamReader reader = new StreamReader(x.GetResponseStream(), Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    // 兼容单引号和双引号：uid='xxx' 或 uid="xxx"
                    Match uidMatch = Regex.Match(body, "uid=['\"]([^'\"]*)['\"]");
                    if (uidMatch.Success)
                    {
                        string uid = uidMatch.Groups[1].Value;
                        return !(uid == "0" || uid == "");
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                AddLog("[检测异常] " + ex.Message);
                return false;
            }
        }

        // 返回 null = 成功，否则返回错误信息
        // 修复：StreamReader.ReadToEnd() 完整读取响应
        private string TryLogin(string u, string p)
        {
            try
            {
                string data = "DDDDD=" + Uri.EscapeDataString(u)
                    + "&upass=" + Uri.EscapeDataString(p)
                    + "&R1=0&R2=0&R6=0&para=00&0MKKey=";
                byte[] bt = Encoding.ASCII.GetBytes(data);
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(
                    string.Format("http://{0}:801/eportal/?c=ACSetting&a=Login", _gateway));
                r.Method = "POST";
                r.ContentType = "application/x-www-form-urlencoded";
                r.Timeout = 10000;
                r.ContentLength = bt.Length;
                using (Stream s2 = r.GetRequestStream())
                    s2.Write(bt, 0, bt.Length);

                using (HttpWebResponse x = (HttpWebResponse)r.GetResponse())
                using (StreamReader reader = new StreamReader(x.GetResponseStream(), Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    if (body.Contains("Dr.COMWebLoginID_3"))
                        return null;
                    Match msgMatch = Regex.Match(body, "Msg=(\\d+);");
                    if (msgMatch.Success)
                    {
                        string code = msgMatch.Groups[1].Value;
                        switch (code)
                        {
                            case "01": return "认证失败";
                            case "02": return "设备数量超限";
                            case "03": return "账号欠费";
                            case "04": return "账号已停用";
                            case "05": return "密码错误";
                            case "09": return "MAC绑定错误";
                            default: return "错误码:" + code;
                        }
                    }
                    return "登录失败";
                }
            }
            catch (WebException wex)
            {
                if (wex.Response != null)
                {
                    try
                    {
                        using (StreamReader reader = new StreamReader(
                            wex.Response.GetResponseStream(), Encoding.UTF8))
                        {
                            string body = reader.ReadToEnd();
                            if (body.Contains("Dr.COMWebLoginID_3"))
                                return null;
                        }
                    }
                    catch { }
                }
                return "登录失败";
            }
            catch
            {
                return "登录失败";
            }
        }

        private bool _logging;

        private void RunDetection()
        {
            if (_logging)
                return;
            if (_detectWorker != null && _detectWorker.IsBusy)
                return;

            _detectWorker = new BackgroundWorker();
            _detectWorker.DoWork += (s, e) =>
            {
                e.Result = Online();
            };
            _detectWorker.RunWorkerCompleted += (s, e) =>
            {
                bool connected = false;
                if (e.Error == null && e.Result != null)
                    connected = (bool)e.Result;

                bool previous = _isConnected;
                _isConnected = connected;

                if (!previous && connected && _autoHotspot)
                {
                    AddLog("网络恢复，启动热点...");
                    ThreadPool.QueueUserWorkItem(_ => StartHotspot());
                }

                if (!connected && _autoReconnect)
                {
                    AddLog("检测到断网!");
                    UpdateUIState();
                    DoLogin();
                }

                UpdateUIState();

                if (previous && !connected)
                {
                    if (_notifyIcon != null)
                    {
                        _notifyIcon.ShowBalloonTip(
                            3000, "苏大校园网", "网络已断开，正在尝试重连...",
                            System.Windows.Forms.ToolTipIcon.Warning);
                    }
                }
                else if (!previous && connected)
                    AddLog("[检测] 网络已恢复");
            };
            _detectWorker.RunWorkerAsync();
        }

        // 遍历所有启用账号，当前选中账号优先，每个试完等 2 秒再 Online() 检查
        private void DoLogin()
        {
            if (_logging) return;

            List<int> enabledIdx = new List<int>();
            int currentIdx = -1;
            for (int i = 0; i < _accounts.Count; i++)
            {
                if (!_accounts[i].Enabled)
                    continue;
                if (_currentAccount != null && _accounts[i].Username == _currentAccount.Username)
                    currentIdx = i;
                else
                    enabledIdx.Add(i);
            }
            if (currentIdx >= 0)
                enabledIdx.Insert(0, currentIdx);

            if (enabledIdx.Count == 0)
            {
                AddLog("没有已启用的账号!");
                return;
            }

            _logging = true;

            _loginWorker = new BackgroundWorker();
            _loginWorker.DoWork += (s, e) =>
            {
                bool success = false;
                int lastIdx = enabledIdx[enabledIdx.Count - 1];
                foreach (int idx in enabledIdx)
                {
                    Account acc = _accounts[idx];
                    AddLog(string.Format("尝试 {0}...", acc.MaskedUsername));
                    string result = TryLogin(acc.Username, acc.Password);
                    if (result == null)
                    {
                        AddLog("登录成功");
                        success = true;
                        break;
                    }
                    else
                    {
                        AddLog(string.Format("失败: {0}", result));
                        if (idx != lastIdx)
                            AddLog(" → 换下一个");
                    }
                    System.Threading.Thread.Sleep(2000);
                    if (Online())
                    {
                        success = true;
                        break;
                    }
                }
                e.Result = success;
            };
            _loginWorker.RunWorkerCompleted += (s, e) =>
            {
                bool success = e.Result != null && (bool)e.Result;
                if (success)
                {
                    _isConnected = true;
                    UpdateUIState();
                    AddLog("已连接!");
                }
                else
                {
                    _isConnected = false;
                    UpdateUIState();
                    AddLog("全部账号尝试失败");
                }
                _logging = false;
            };
            _loginWorker.RunWorkerAsync();
        }

        private void StartHotspot()
        {
            try
            {
                AddLog("[热点] 正在尝试开启移动热点...");

                string psCmd =
                    "$con = [Windows.Networking.Connectivity.NetworkInformation,Windows.Networking.Connectivity,ContentType=WindowsRuntime]::GetInternetConnectionProfile();" +
                    "if ($con -eq $null) { exit 1 }" +
                    "$mgr = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager,Windows.Networking.NetworkOperators,ContentType=WindowsRuntime]::CreateFromConnectionProfile($con);" +
                    "$mgr.StartTetheringAsync() | Out-Null";

                ProcessStartInfo psi = new ProcessStartInfo(
                    "powershell.exe", "-NoProfile -WindowStyle Hidden -Command " + psCmd);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
                AddLog("[热点] 已请求开启");
            }
            catch (Exception ex)
            {
                AddLog(string.Format("[热点] 启动失败: {0}", ex.Message));
            }
        }

        #endregion

        #region Tray icon

        private void BuildTrayIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Text = "苏大校园网自动登录器";

            // 从 exe 自身提取嵌入的图标
            try
            {
                _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    Assembly.GetExecutingAssembly().Location);
            }
            catch
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            System.Windows.Forms.ContextMenuStrip menu = new System.Windows.Forms.ContextMenuStrip();
            System.Windows.Forms.ToolStripMenuItem showItem = new System.Windows.Forms.ToolStripMenuItem("显示");
            showItem.Click += (s, e) => ShowFromTray();
            System.Windows.Forms.ToolStripMenuItem exitItem = new System.Windows.Forms.ToolStripMenuItem("退出");
            exitItem.Click += (s, e) => ExitApp();
            menu.Items.Add(showItem);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (s, e) => ShowFromTray();
            _notifyIcon.Visible = true;
        }

        private void ShowFromTray()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void ExitApp()
        {
            _allowClose = true;
            if (_notifyIcon != null)
                _notifyIcon.Visible = false;
            System.Windows.Application.Current.Shutdown();
        }

        #endregion

        #region Logging

        // 从嵌入资源加载流，兼容不同命名空间前缀
        private static Stream GetEmbeddedResource(string fileName)
        {
            string[] names = Assembly.GetExecutingAssembly().GetManifestResourceNames();
            foreach (string name in names)
            {
                if (name.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase)
                    || name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
                }
            }
            return null;
        }

        // 从登录返回的 HTML 中提取关键错误信息，过滤 HTML 标签和网址
        private string ExtractLoginFailReason(string body)
        {
            if (string.IsNullOrEmpty(body))
                return "无返回内容";

            // 处理 Dr.COM 纯文本格式响应（如 01;time=...;Msg=...）
            // 先尝试提取 Msg= 获取可读错误信息
            int msgIdx = body.IndexOf("Msg=", StringComparison.OrdinalIgnoreCase);
            if (msgIdx >= 0)
            {
                int start = msgIdx + 4;
                int end = body.IndexOfAny(new[] { ';', '&', '"', '\'', '<', '\n', '\r' }, start);
                if (end < 0) end = body.Length;
                string msg = body.Substring(start, end - start).Trim();
                if (!string.IsNullOrEmpty(msg))
                    return msg;
            }

            // 过滤掉 01;time=... 这种无意义的前缀，只保留有用信息
            string cleaned = body;
            // 去掉 01;time=xxx; 这类时间戳前缀
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"^\d+;time=[^;]*;?", "");
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\d+;time=[^;]*;?", "");
            // 尝试提取 result= 字段
            int resIdx = cleaned.IndexOf("result=", StringComparison.OrdinalIgnoreCase);
            if (resIdx >= 0)
            {
                int start = resIdx + 7;
                int end = cleaned.IndexOfAny(new[] { ';', '&', '"', '\'', '<', '\n', '\r' }, start);
                if (end < 0) end = cleaned.Length;
                string res = cleaned.Substring(start, end - start).Trim();
                if (!string.IsNullOrEmpty(res))
                    return "result=" + res;
            }

            // 过滤 HTML 标签和网址
            string text = cleaned;
            int doctype = text.IndexOf("<!DOCTYPE", StringComparison.OrdinalIgnoreCase);
            if (doctype >= 0)
            {
                int dEnd = text.IndexOf('>', doctype);
                if (dEnd >= 0) text = text.Remove(doctype, dEnd - doctype + 1);
            }
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", " ");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"https?://\S+", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

            if (string.IsNullOrEmpty(text))
                return "服务器返回无有效文本";

            return text.Length > 80 ? text.Substring(0, 80) + "..." : text;
        }

        private void AddLog(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _logTextBox.AppendText(string.Format(
                    "[{0}] {1}\r\n",
                    DateTime.Now.ToString("HH:mm:ss"),
                    message));

                // 限制日志条数，避免无限增长
                string text = _logTextBox.Text;
                int idx = -1;
                int count = 0;
                for (int i = text.Length - 1; i >= 0; i--)
                {
                    if (text[i] == '\n')
                    {
                        count++;
                        if (count >= MaxLogLines)
                        {
                            idx = i + 1;
                            break;
                        }
                    }
                }
                if (idx > 0)
                    _logTextBox.Text = text.Substring(idx);

                _logScroll.ScrollToEnd();
            }));
        }

        #endregion
    }
}
