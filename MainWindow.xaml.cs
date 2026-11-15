using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace StickIt;

public partial class MainWindow : Window
{
    private const string VersionLabel = "v1.07.03";
    private const int WmNcHitTest = 0x0084;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const double ResizeBorderSize = 8;
    private const int MinimumFadeOpacityPercent = 10;
    private const int MaximumFadeOpacityPercent = 90;
    private const int FadeOpacityStepPercent = 10;
    private const int DwmaUseImmersiveDarkMode = 20;
    private const int DwmaUseImmersiveDarkModeLegacy = 19;
    private readonly string _appFolder = ResolveAppFolder();
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickIt", "settings.json");
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _feedbackTimer;
    private FileSystemWatcher? _watcher;
    private string? _selectedFilePath;
    private string? _preferredFileName;
    private string? _contextFilePath;
    private string? _recentlyImportedFilePath;
    private string _selectedFileStatus = "";
    private bool _wrapButtons;
    private bool _fadeWindow = true;
    private int _fadeOpacityPercent = 50;
    private bool _isBubbleMode;
    private bool _copyFeedbackActive;
    private double _savedWidth;
    private double _savedHeight;
    private double _savedLeft;
    private double _savedTop;
    private double _topBarAnchorOffsetX;
    private double _topBarAnchorOffsetY;
    private double _savedMinWidth;
    private double _savedMinHeight;
    private ResizeMode _savedResizeMode;

    private static string ResolveAppFolder()
    {
        var executableFolder = AppContext.BaseDirectory;
        for (DirectoryInfo? directory = new(executableFolder); directory is not null; directory = directory.Parent)
        {
            if (string.Equals(directory.Name, "bin", StringComparison.OrdinalIgnoreCase) &&
                directory.Parent is not null && File.Exists(Path.Combine(directory.Parent.FullName, "StickIt.csproj")))
            {
                return directory.Parent.FullName;
            }
        }

        return executableFolder;
    }

    public MainWindow()
    {
        InitializeComponent();
        RestoreSettings();
        UpdateFadeOpacityControls();
        VersionText.Text = VersionLabel;
        SourceInitialized += (_, _) =>
        {
            EnableDarkTitleBar();
            InstallResizeHitTest();
        };
        Activated += (_, _) => ApplyFadeAppearance();
        Deactivated += (_, _) => ApplyFadeAppearance();
        UpdateFadeButtonAppearance();
        ApplyFadeAppearance();
        SizeChanged += (_, _) => UpdateWindowSize();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            LoadTextFiles();
        };

        _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _feedbackTimer.Tick += (_, _) =>
        {
            _feedbackTimer.Stop();
            StatusText.Text = _selectedFileStatus;
            _copyFeedbackActive = false;
            ApplyFadeAppearance();
        };

        Loaded += (_, _) =>
        {
            UpdateWindowSize();
            LoadTextFiles();
            StartWatchingFolder();
        };
        Closed += (_, _) =>
        {
            SaveSettings();
            _watcher?.Dispose();
        };
    }

    private void RestoreSettings()
    {
        AppSettings? settings;
        try
        {
            settings = File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            settings = null;
        }

        if (settings is null)
            return;

        _preferredFileName = settings.SelectedFileName;
        _wrapButtons = settings.WrapButtons;
        _fadeWindow = settings.FadeWindow;
        _fadeOpacityPercent = Math.Clamp(settings.FadeOpacityPercent,
            MinimumFadeOpacityPercent, MaximumFadeOpacityPercent);
        WrapButton.IsChecked = _wrapButtons;
        FadeButton.IsChecked = _fadeWindow;
        UpdateFadeButtonAppearance();

        if (IsFiniteAtLeast(settings.Width, MinWidth) && IsFiniteAtLeast(settings.Height, MinHeight))
        {
            Width = settings.Width;
            Height = settings.Height;
        }

        if (settings.Left is double savedLeft && settings.Top is double savedTop &&
            double.IsFinite(savedLeft) && double.IsFinite(savedTop) &&
            IntersectsVirtualScreen(savedLeft, savedTop, Width, Height))
        {
            Left = savedLeft;
            Top = savedTop;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        if (double.IsFinite(settings.LeftPanelWidth) && settings.LeftPanelWidth >= 66)
            ((Grid)FilePanelBorder.Parent).ColumnDefinitions[0].Width = new GridLength(settings.LeftPanelWidth);
    }

    private static bool IsFiniteAtLeast(double value, double minimum) => double.IsFinite(value) && value >= minimum;

    private static bool IntersectsVirtualScreen(double left, double top, double width, double height)
    {
        const double visibleAmount = 60;
        return left + width > SystemParameters.VirtualScreenLeft + visibleAmount &&
               left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - visibleAmount &&
               top + height > SystemParameters.VirtualScreenTop + visibleAmount &&
               top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - visibleAmount;
    }

    private void SaveSettings()
    {
        var inBubble = _isBubbleMode;
        var settings = new AppSettings
        {
            SelectedFileName = inBubble ? _preferredFileName : _selectedFilePath is null
                ? _preferredFileName
                : Path.GetFileName(_selectedFilePath),
            WrapButtons = _wrapButtons,
            FadeWindow = _fadeWindow,
            FadeOpacityPercent = _fadeOpacityPercent,
            Width = inBubble ? _savedWidth : Width,
            Height = inBubble ? _savedHeight : Height,
            Left = inBubble ? _savedLeft : Left,
            Top = inBubble ? _savedTop : Top,
            LeftPanelWidth = ((Grid)FilePanelBorder.Parent).ColumnDefinitions[0].ActualWidth
        };

        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; a failed save should not prevent the app from closing.
        }
    }

    private void EnableDarkTitleBar()
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        var result = DwmSetWindowAttribute(windowHandle, DwmaUseImmersiveDarkMode, ref enabled, sizeof(int));
        if (result != 0)
            _ = DwmSetWindowAttribute(windowHandle, DwmaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    private void InstallResizeHitTest()
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(windowHandle)?.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmNcHitTest || _isBubbleMode || ResizeMode == ResizeMode.NoResize ||
            WindowState != WindowState.Normal)
            return IntPtr.Zero;

        var packedPoint = lParam.ToInt64();
        var screenX = unchecked((short)(packedPoint & 0xFFFF));
        var screenY = unchecked((short)((packedPoint >> 16) & 0xFFFF));
        var point = PointFromScreen(new Point(screenX, screenY));
        var isLeft = point.X >= 0 && point.X < ResizeBorderSize;
        var isRight = point.X < ActualWidth && point.X >= ActualWidth - ResizeBorderSize;
        var isTop = point.Y >= 0 && point.Y < ResizeBorderSize;
        var isBottom = point.Y < ActualHeight && point.Y >= ActualHeight - ResizeBorderSize;

        var hitTest = (isLeft, isRight, isTop, isBottom) switch
        {
            (true, _, true, _) => HtTopLeft,
            (_, true, true, _) => HtTopRight,
            (true, _, _, true) => HtBottomLeft,
            (_, true, _, true) => HtBottomRight,
            (true, _, _, _) => HtLeft,
            (_, true, _, _) => HtRight,
            (_, _, true, _) => HtTop,
            (_, _, _, true) => HtBottom,
            _ => 0
        };

        if (hitTest == 0)
            return IntPtr.Zero;

        handled = true;
        return new IntPtr(hitTest);
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (RefreshButton.IsMouseOver || SettingsButton.IsMouseOver ||
            MinimizeButton.IsMouseOver || CloseButton.IsMouseOver)
            return;

        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateFadeButtonAppearance()
    {
        FadeButton.Background = Brush(_fadeWindow ? "#FF267447" : "#FF252A33");
        FadeButton.BorderBrush = Brush(_fadeWindow ? "#FF45A96B" : "#FF383E49");
        FadeButton.Foreground = Brush(_fadeWindow ? "#FFF0FFF4" : "#FFE8EAF0");
    }

    private void ApplyFadeAppearance()
    {
        var shouldFade = _fadeWindow && !IsActive && !_isBubbleMode;
        var surfaceOpacity = shouldFade ? _fadeOpacityPercent : 100;
        var chromeOpacity = shouldFade ? Math.Min(100, _fadeOpacityPercent + 25) : 100;
        var panelBackground = BrushWithOpacity("#FF1B1E25", surfaceOpacity);
        var panelBorder = BrushWithOpacity("#FF2B3039", chromeOpacity);
        FilePanelBorder.Background = panelBackground;
        FilePanelBorder.BorderBrush = panelBorder;
        ContentPanelBorder.Background = panelBackground;
        ContentPanelBorder.BorderBrush = panelBorder;
        SettingsDropdownBorder.Background = panelBackground;
        SettingsDropdownBorder.BorderBrush = panelBorder;
        var topBarBackground = BrushWithOpacity("#FF111318", chromeOpacity);
        TopBar.Background = topBarBackground;
        BottomBar.Background = topBarBackground;

        Application.Current.Resources["ButtonBackground"] = BrushWithOpacity("#FF252A33", surfaceOpacity);
        Application.Current.Resources["ButtonBorderBrush"] = BrushWithOpacity("#FF383E49", chromeOpacity);
        Application.Current.Resources["ButtonHoverBackground"] = BrushWithOpacity("#FF313949", surfaceOpacity);
        Application.Current.Resources["ButtonHoverBorderBrush"] = BrushWithOpacity("#FF596887", chromeOpacity);
        Application.Current.Resources["ButtonPressedBackground"] = BrushWithOpacity("#FF3B4760", surfaceOpacity);
        WrapButton.Background = BrushWithOpacity(_wrapButtons ? "#FF267447" : "#FF252A33", surfaceOpacity);
        WrapButton.BorderBrush = BrushWithOpacity(_wrapButtons ? "#FF45A96B" : "#FF383E49", chromeOpacity);
        FadeButton.Background = BrushWithOpacity(_fadeWindow ? "#FF267447" : "#FF252A33", surfaceOpacity);
        FadeButton.BorderBrush = BrushWithOpacity(_fadeWindow ? "#FF45A96B" : "#FF383E49", chromeOpacity);
        BubbleHeader.Background = BrushWithOpacity("#FFE7C65F", surfaceOpacity);
        BubbleRestoreButton.Background = BrushWithOpacity("#FFFFEAA0", surfaceOpacity);
        StatusIndicatorBorder.Background = topBarBackground;
        StatusText.Foreground = Brush(_copyFeedbackActive ? "#FFBFFFD1" : "#FFB8BEC9");
        ApplySilhouetteAppearance(shouldFade);
    }

    private void ApplySilhouetteAppearance(bool silhouette)
    {
        foreach (var presenter in FindVisualChildren<ContentPresenter>(MainView))
            presenter.Opacity = silhouette ? 0 : 1;
        foreach (var textBlock in FindVisualChildren<TextBlock>(MainView))
            textBlock.Opacity = silhouette ? 0 : 1;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadTextFiles();

    private void FilePanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        LeftPanelWidthText.Text = $"W {Math.Round(e.NewSize.Width):0}";
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsDropdownBorder.Visibility = SettingsDropdownBorder.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void MainView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (SettingsDropdownBorder.Visibility == Visibility.Visible &&
            !SettingsDropdownBorder.IsMouseOver && !SettingsButton.IsMouseOver)
        {
            SettingsDropdownBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void PlaceholderSetting_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = sender is Button button
            ? $"{button.Content} is a placeholder."
            : "This setting is a placeholder.";
    }

    private void WrapButton_Click(object sender, RoutedEventArgs e)
    {
        _wrapButtons = WrapButton.IsChecked == true;
        WrapButton.Background = Brush(_wrapButtons ? "#FF267447" : "#FF252A33");
        WrapButton.BorderBrush = Brush(_wrapButtons ? "#FF45A96B" : "#FF383E49");
        WrapButton.Foreground = Brush(_wrapButtons ? "#FFF0FFF4" : "#FFE8EAF0");
        RenderSelectedFile();
        ApplyFadeAppearance();
    }

    private void FadeButton_Click(object sender, RoutedEventArgs e)
    {
        _fadeWindow = FadeButton.IsChecked == true;
        UpdateFadeButtonAppearance();
        ApplyFadeAppearance();
    }

    private void FadeOpacityButton_Click(object sender, RoutedEventArgs e)
    {
        var change = ReferenceEquals(sender, DecreaseFadeOpacityButton)
            ? -FadeOpacityStepPercent
            : FadeOpacityStepPercent;
        _fadeOpacityPercent = Math.Clamp(_fadeOpacityPercent + change,
            MinimumFadeOpacityPercent, MaximumFadeOpacityPercent);
        UpdateFadeOpacityControls();
        StatusText.Text = $"Fade opacity: {_fadeOpacityPercent}% when unfocused";
        _copyFeedbackActive = false;
        _feedbackTimer.Stop();
        _feedbackTimer.Start();
        ApplyFadeAppearance();
    }

    private void UpdateFadeOpacityControls()
    {
        DecreaseFadeOpacityButton.IsEnabled = _fadeOpacityPercent > MinimumFadeOpacityPercent;
        IncreaseFadeOpacityButton.IsEnabled = _fadeOpacityPercent < MaximumFadeOpacityPercent;
        DecreaseFadeOpacityButton.ToolTip = $"Decrease faded opacity (currently {_fadeOpacityPercent}%)";
        FadeButton.ToolTip = $"Fade when unfocused · {_fadeOpacityPercent}% surface opacity";
        IncreaseFadeOpacityButton.ToolTip = $"Increase faded opacity (currently {_fadeOpacityPercent}%)";
    }

    private void UpdateWindowSize()
    {
        WindowSizeText.Text = $"{Math.Round(ActualWidth):0} × {Math.Round(ActualHeight):0}";
    }

    private void StartWatchingFolder()
    {
        try
        {
            _watcher = new FileSystemWatcher(_appFolder, "*.txt")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnTextFileChanged;
            _watcher.Changed += OnTextFileChanged;
            _watcher.Deleted += OnTextFileChanged;
            _watcher.Renamed += OnTextFileRenamed;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Folder watching unavailable: {ex.Message}";
        }
    }

    private void OnTextFileChanged(object sender, FileSystemEventArgs e) => QueueRefresh();
    private void OnTextFileRenamed(object sender, RenamedEventArgs e) => QueueRefresh();

    private void QueueRefresh()
    {
        if (!Dispatcher.HasShutdownStarted)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _refreshTimer.Stop();
                _refreshTimer.Start();
            });
        }
    }

    private void LoadTextFiles()
    {
        string[] filePaths;
        try
        {
            filePaths = Directory.EnumerateFiles(_appFolder, "*.txt", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FileList.Children.Clear();
            AddStickItPlaceholder();
            ContentItems.Children.Clear();
            _selectedFilePath = null;
            SelectedFileTitle.Text = "Folder unavailable";
            _selectedFileStatus = $"Could not read the app folder: {ex.Message}";
            StatusText.Text = _selectedFileStatus;
            ApplyFadeAppearance();
            return;
        }

        FileList.Children.Clear();
        AddStickItPlaceholder();
        if (filePaths.Length == 0)
        {
            _selectedFilePath = null;
            SelectedFileTitle.Text = "No text files found";
            ContentItems.Children.Clear();
            _selectedFileStatus = "Add .txt files beside StickIt";
            StatusText.Text = _selectedFileStatus;
            ApplyFadeAppearance();
            return;
        }

        foreach (var filePath in filePaths)
        {
            var isSelected = string.Equals(filePath, _selectedFilePath, StringComparison.OrdinalIgnoreCase);
            var fileButton = new Button
            {
                Content = new TextBlock
                {
                    Text = Path.GetFileName(filePath),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Tag = filePath,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 2),
                Background = isSelected ? Brush("#FF2B3450") : Brushes.Transparent,
                BorderBrush = isSelected ? Brush("#FF5367A3") : Brushes.Transparent,
                Foreground = isSelected ? Brush("#FFC5D1FF") : Brush("#FFCDD1D9"),
                FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
                ToolTip = Path.GetFileName(filePath),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            fileButton.Click += FileButton_Click;
            fileButton.PreviewMouseRightButtonUp += FileButton_PreviewMouseRightButtonUp;
            FileList.Children.Add(fileButton);
        }

        if (_selectedFilePath is null || !filePaths.Contains(_selectedFilePath, StringComparer.OrdinalIgnoreCase))
            _selectedFilePath = filePaths.FirstOrDefault(path => string.Equals(
                Path.GetFileName(path), _preferredFileName, StringComparison.OrdinalIgnoreCase)) ?? filePaths[0];

        _preferredFileName = Path.GetFileName(_selectedFilePath);

        HighlightSelectedFile();
        RenderSelectedFile();
        ApplyFadeAppearance();
    }

    private void FileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            if (!string.Equals(path, _recentlyImportedFilePath, StringComparison.OrdinalIgnoreCase))
                _recentlyImportedFilePath = null;
            _selectedFilePath = path;
            _preferredFileName = Path.GetFileName(path);
            HighlightSelectedFile();
            RenderSelectedFile();
        }
    }

    private void FileButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: string path } fileButton)
            return;

        if (!string.Equals(path, _recentlyImportedFilePath, StringComparison.OrdinalIgnoreCase))
            _recentlyImportedFilePath = null;
        _selectedFilePath = path;
        _preferredFileName = Path.GetFileName(path);
        HighlightSelectedFile();
        RenderSelectedFile();
        _contextFilePath = path;
        FileActionsPopup.PlacementTarget = fileButton;
        FileActionsPopup.Placement = PlacementMode.Right;
        FileActionsPopup.IsOpen = true;
        e.Handled = true;
    }

    private void OpenInEditorButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _contextFilePath;
        FileActionsPopup.IsOpen = false;
        if (path is null)
            return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not open file: {ex.Message}";
        }
    }

    private void ShowInExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _contextFilePath;
        FileActionsPopup.IsOpen = false;
        if (path is null)
            return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not show file in Explorer: {ex.Message}";
        }
    }

    private void FilePanel_DragEnter(object sender, DragEventArgs e) => SetFileDropEffect(e);

    private void FilePanel_DragOver(object sender, DragEventArgs e) => SetFileDropEffect(e);

    private static void SetFileDropEffect(DragEventArgs e)
    {
        e.Effects = GetDroppedTextFiles(e).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static string[] GetDroppedTextFiles(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            return Array.Empty<string>();

        return paths.Where(path => File.Exists(path) &&
            string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void FilePanel_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var paths = GetDroppedTextFiles(e);
        if (paths.Length == 0)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        string? lastDestination = null;
        string? failedPath = null;
        var copiedCount = 0;
        foreach (var path in paths)
        {
            try
            {
                var destination = CopyTextFileWithUniqueName(path);
                if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                    copiedCount++;
                lastDestination = destination;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                failedPath = Path.GetFileName(path);
            }
        }

        if (lastDestination is not null)
        {
            _selectedFilePath = lastDestination;
            _preferredFileName = Path.GetFileName(lastDestination);
            _recentlyImportedFilePath = copiedCount > 0 ? lastDestination : null;
            LoadTextFiles();
            StatusText.Text = copiedCount > 0
                ? $"Added {copiedCount} text file(s)"
                : "That file is already in the app folder";
            _feedbackTimer.Stop();
            _feedbackTimer.Start();
        }
        else if (failedPath is not null)
        {
            StatusText.Text = $"Could not add {failedPath}";
        }
    }

    private string CopyTextFileWithUniqueName(string sourcePath)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath);

        for (var suffix = 1; ; suffix++)
        {
            var candidateName = suffix == 1 ? $"{fileName}{extension}" : $"{fileName}_{suffix}{extension}";
            var destinationPath = Path.Combine(_appFolder, candidateName);
            if (string.Equals(fullSourcePath, Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
                return destinationPath;
            if (File.Exists(destinationPath))
                continue;

            try
            {
                File.Copy(sourcePath, destinationPath, overwrite: false);
                return destinationPath;
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                // Another copy claimed this name in the meantime; try the next suffix.
            }
        }
    }

    private void AddStickItPlaceholder()
    {
        var placeholder = new Button
        {
            Content = new TextBlock
            {
                Text = "StickIt",
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 8,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            FontSize = 8,
            Tag = "StickItAccent",
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 3, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ToolTip = "Collapse to floating bubble"
        };
        placeholder.Click += StickItButton_Click;
        FileList.Children.Insert(0, placeholder);
    }

    private void StickItButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBubbleMode)
            return;

        _savedWidth = Width;
        _savedHeight = Height;
        _savedLeft = Left;
        _savedTop = Top;
        _savedMinWidth = MinWidth;
        _savedMinHeight = MinHeight;
        _savedResizeMode = ResizeMode;

        var screenToDip = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        var topBarAnchorScreen = TopBar.PointToScreen(new Point(TopBar.ActualWidth / 2, TopBar.ActualHeight / 2));
        var topBarAnchor = screenToDip.Transform(topBarAnchorScreen);
        _topBarAnchorOffsetX = topBarAnchor.X - Left;
        _topBarAnchorOffsetY = topBarAnchor.Y - Top;

        if (sender is FrameworkElement stickItButton)
        {
            var buttonScreenPosition = stickItButton.PointToScreen(new Point(0, 0));
            var buttonPosition = screenToDip.Transform(buttonScreenPosition);
            Left = buttonPosition.X + (stickItButton.ActualWidth - 68) / 2;
            Top = buttonPosition.Y + (stickItButton.ActualHeight - 34) / 2;
        }

        SettingsDropdownBorder.Visibility = Visibility.Collapsed;
        MainView.Visibility = Visibility.Collapsed;
        BubbleView.Visibility = Visibility.Visible;
        _isBubbleMode = true;
        MinWidth = MinHeight = 68;
        ResizeMode = ResizeMode.NoResize;
        Width = Height = 68;
        ApplyFadeAppearance();
    }

    private void RestoreFromBubble_Click(object sender, RoutedEventArgs e)
    {
        if (!_isBubbleMode)
            return;

        var restoreButtonScreenPosition = BubbleRestoreButton.PointToScreen(new Point(
            BubbleRestoreButton.ActualWidth / 2, BubbleRestoreButton.ActualHeight / 2));
        var screenToDip = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        var restoreButtonPosition = screenToDip.Transform(restoreButtonScreenPosition);

        BubbleView.Visibility = Visibility.Collapsed;
        MainView.Visibility = Visibility.Visible;
        MinWidth = _savedMinWidth;
        MinHeight = _savedMinHeight;
        ResizeMode = _savedResizeMode;
        Width = _savedWidth;
        Height = _savedHeight;
        Left = restoreButtonPosition.X - _topBarAnchorOffsetX;
        Top = restoreButtonPosition.Y - _topBarAnchorOffsetY;
        _isBubbleMode = false;
        ApplyFadeAppearance();
    }

    private void BubbleHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void HighlightSelectedFile()
    {
        foreach (var child in FileList.Children)
        {
            if (child is not Button button || button.Tag is not string path)
                continue;

            var isSelected = string.Equals(path, _selectedFilePath, StringComparison.OrdinalIgnoreCase);
            var isRecentlyImported = isSelected && string.Equals(path, _recentlyImportedFilePath, StringComparison.OrdinalIgnoreCase);
            button.Background = isRecentlyImported ? Brush("#FF3A3323") : isSelected ? Brush("#FF2B3450") : Brushes.Transparent;
            button.BorderBrush = isRecentlyImported ? Brush("#FF9B8240") : isSelected ? Brush("#FF5367A3") : Brushes.Transparent;
            button.Foreground = isRecentlyImported ? Brush("#FFE9D79A") : isSelected ? Brush("#FFC5D1FF") : Brush("#FFCDD1D9");
            button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void RenderSelectedFile()
    {
        ContentItems.Children.Clear();
        if (_selectedFilePath is null)
            return;

        SelectedFileTitle.Text = Path.GetFileName(_selectedFilePath);
        string[] lines;
        try
        {
            lines = File.ReadAllLines(_selectedFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _selectedFileStatus = "Could not read this file. It may still be saving; refresh to retry.";
            StatusText.Text = _selectedFileStatus;
            return;
        }

        var buttonCount = 0;
        var commentCount = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (TryParseComment(line, out var level, out var commentText))
            {
                ContentItems.Children.Add(CreateComment(commentText, level));
                commentCount++;
                continue;
            }

            var tabIndex = line.IndexOf('\t');
            var copyText = tabIndex < 0 ? line : line[..tabIndex];
            var buttonLabel = tabIndex < 0 ? line : line[(tabIndex + 1)..];
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = buttonLabel,
                    TextTrimming = _wrapButtons ? TextTrimming.None : TextTrimming.CharacterEllipsis,
                    TextWrapping = _wrapButtons ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    VerticalAlignment = VerticalAlignment.Center
                },
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ToolTip = buttonLabel,
                Tag = copyText
            };
            button.Click += CopyButton_Click;
            ContentItems.Children.Add(button);
            buttonCount++;
        }

        if (buttonCount == 0 && commentCount == 0)
            ContentItems.Children.Add(new TextBlock
            {
                Text = "This file has no lines to show yet.",
                Margin = new Thickness(2, 4, 2, 0),
                FontSize = 13,
                Foreground = Brush("#FF929BA9")
            });

        _selectedFileStatus = $"{buttonCount} button(s) · {commentCount} comment(s) · wrap {(_wrapButtons ? "on" : "off")}";
        StatusText.Text = _selectedFileStatus;
    }

    private static bool TryParseComment(string line, out int level, out string text)
    {
        var trimmed = line.TrimStart();
        var hashCount = 0;
        while (hashCount < trimmed.Length && trimmed[hashCount] == '#')
            hashCount++;

        if (hashCount < 2)
        {
            level = 0;
            text = "";
            return false;
        }

        level = Math.Min(hashCount, 5);
        text = trimmed[hashCount..].TrimStart();
        return true;
    }

    private static FrameworkElement CreateComment(string text, int level)
    {
        var foreground = level switch
        {
            2 => "#FFB5C0D1",
            3 => "#FF77D4D3",
            4 => "#FFC4A0F1",
            _ => "#FFFFA0A8"
        };
        var fontSize = level switch { 2 => 11.0, 3 => 12.0, 4 => 13.0, _ => 14.0 };
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            FontWeight = level >= 4 ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Brush(foreground),
            Margin = new Thickness(0, 1, 0, 6)
        };
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private static SolidColorBrush BrushWithOpacity(string color, int opacityPercent)
    {
        var rgb = (Color)ColorConverter.ConvertFromString(color);
        var alpha = (byte)Math.Round(Math.Clamp(opacityPercent, 0, 100) * 255 / 100.0);
        return new SolidColorBrush(Color.FromArgb(alpha, rgb.R, rgb.G, rgb.B));
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string text })
            return;

        try
        {
            Clipboard.SetText(text);
            StatusText.Text = $"Copied: {text}";
            _copyFeedbackActive = true;
            ApplyFadeAppearance();
            _feedbackTimer.Stop();
            _feedbackTimer.Start();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            StatusText.Text = "Clipboard is busy. Click again to retry.";
        }
    }

    private sealed class AppSettings
    {
        public string? SelectedFileName { get; set; }
        public bool WrapButtons { get; set; }
        public bool FadeWindow { get; set; } = true;
        public int FadeOpacityPercent { get; set; } = 50;
        public double Width { get; set; } = 300;
        public double Height { get; set; } = 440;
        public double? Left { get; set; }
        public double? Top { get; set; }
        public double LeftPanelWidth { get; set; } = 67;
    }
}
