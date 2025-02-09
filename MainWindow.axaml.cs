// MainWindow.axaml.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace G2BAvaloniaApp
{
    /// <summary>
    /// 로그 심각도
    /// </summary>
    public enum LogSeverity
    {
        Info,
        Warn,
        Error
    }

    /// <summary>
    /// 로그 항목 모델
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; } = "";
        public LogSeverity Severity { get; set; }
    }

    /// <summary>
    /// 로그 심각도별 텍스트 색상 변환기
    /// </summary>
    public class LogSeverityToColorConverter : Avalonia.Data.Converters.IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is LogSeverity severity)
            {
                switch (severity)
                {
                    case LogSeverity.Error:
                        return Brushes.LightCoral;
                    case LogSeverity.Warn:
                        return Brushes.Gold;
                    default:
                        return Brushes.White;
                }
            }
            return Brushes.White;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 로그 심각도별 배경색 변환기
    /// </summary>
    public class LogSeverityToBackgroundColorConverter : Avalonia.Data.Converters.IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is LogSeverity severity)
            {
                switch (severity)
                {
                    case LogSeverity.Error:
                        return new SolidColorBrush(Color.FromArgb(50, 255, 0, 0));
                    case LogSeverity.Warn:
                        return new SolidColorBrush(Color.FromArgb(50, 255, 215, 0));
                    default:
                        return Brushes.Transparent;
                }
            }
            return Brushes.Transparent;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class MainWindow : Window
    {
        // UI 컨트롤 변수
        private TextBox? _keywordTextBox;
        private Button?  _startButton;
        private Button?  _stopButton;
        private ProgressBar? _progressBar;
        private TextBlock? _progressText;
        private TextBlock? _statusDetail;

        // Details 탭 통계 컨트롤
        private TextBlock? _totalLogsText;
        private TextBlock? _errorCountText;
        private TextBlock? _warnCountText;
        private TextBlock? _infoCountText;

        // Log 탭의 인터랙티브 컨트롤들
        private CheckBox? _infoCheckBox;
        private CheckBox? _warnCheckBox;
        private CheckBox? _errorCheckBox;
        private TextBox? _logSearchTextBox;
        private Button? _clearLogsButton;
        private ListBox? _logListBox;

        private CancellationTokenSource? _cts;

        // 전체 로그 컬렉션과 필터링된 로그 컬렉션
        public ObservableCollection<LogEntry> LogEntries { get; } = new ObservableCollection<LogEntry>();
        private ObservableCollection<LogEntry> _filteredLogEntries = new ObservableCollection<LogEntry>();
        public ObservableCollection<LogEntry> FilteredLogEntries => _filteredLogEntries;

        // Clear Log 버튼 클릭 시 기록된 시각 이후의 로그만 보여주도록 함.
        private DateTime? _logClearTime = null;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            // XAML 컨트롤 찾기
            _keywordTextBox = this.FindControl<TextBox>("KeywordTextBox");
            _startButton    = this.FindControl<Button>("StartButton");
            _stopButton     = this.FindControl<Button>("StopButton");
            _progressBar    = this.FindControl<ProgressBar>("ProgressBar");
            _progressText   = this.FindControl<TextBlock>("ProgressText");
            _statusDetail   = this.FindControl<TextBlock>("StatusDetail");

            _totalLogsText  = this.FindControl<TextBlock>("TotalLogsText");
            _errorCountText = this.FindControl<TextBlock>("ErrorCountText");
            _warnCountText  = this.FindControl<TextBlock>("WarnCountText");
            _infoCountText  = this.FindControl<TextBlock>("InfoCountText");

            _infoCheckBox = this.FindControl<CheckBox>("InfoCheckBox");
            _warnCheckBox = this.FindControl<CheckBox>("WarnCheckBox");
            _errorCheckBox = this.FindControl<CheckBox>("ErrorCheckBox");
            _logSearchTextBox = this.FindControl<TextBox>("LogSearchTextBox");
            _clearLogsButton = this.FindControl<Button>("ClearLogsButton");
            _logListBox = this.FindControl<ListBox>("LogListBox");

            // 필터 컨트롤: IsChecked 속성 변경을 구독 (Checked/Unchecked 이벤트 대신)
            _infoCheckBox?.GetObservable(CheckBox.IsCheckedProperty).Subscribe(_ => OnLogFilterChanged(null, null));
            _warnCheckBox?.GetObservable(CheckBox.IsCheckedProperty).Subscribe(_ => OnLogFilterChanged(null, null));
            _errorCheckBox?.GetObservable(CheckBox.IsCheckedProperty).Subscribe(_ => OnLogFilterChanged(null, null));

            // LogSearchTextBox의 텍스트 변경 구독
            _logSearchTextBox?.GetObservable(TextBox.TextProperty).Subscribe(_ => OnLogFilterChanged(null, null));

            // ClearLogs 버튼 이벤트 구독
            if (_clearLogsButton != null)
            {
                _clearLogsButton.Click += OnClearLogsClick;
            }
            // 로그 목록의 더블클릭 이벤트 구독
            if (_logListBox != null)
            {
                _logListBox.DoubleTapped += OnLogItemDoubleTapped;
            }

            // 초기 진행 상태 UI 숨김
            if (_progressBar != null)  _progressBar.IsVisible = false;
            if (_progressText != null) _progressText.IsVisible = false;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // [Start] 버튼 클릭 이벤트
        private void OnStartClick(object? sender, RoutedEventArgs e)
        {
            if (_cts != null) return; // 이미 실행 중이면 무시

            _cts = new CancellationTokenSource();

            string keyword = _keywordTextBox?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(keyword))
                keyword = "RPA";

            AppendLog($"[INFO] 검색어 = {keyword}");

            if (_progressBar != null)  _progressBar.IsVisible = true;
            if (_progressText != null) _progressText.IsVisible = true;
            if (_statusDetail != null) _statusDetail.Text = "작업 진행 중...";

            // Worker.cs와 WorkerOptions.cs 파일의 구현을 사용
            var options = new WorkerOptions { Keyword = keyword };
            var worker = new Worker(options);
            worker.OnLog += AppendLog;

            // _fire-and-forget_ 방식으로 작업 실행 (CS4014 경고는 의도한 동작)
            _ = Task.Run(async () =>
            {
                try
                {
                    await worker.RunAsync(_cts.Token);
                }
                catch (Exception ex)
                {
                    AppendLog($"[ERROR] {ex.Message}");
                }
                finally
                {
                    _cts = null;
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (_progressBar != null)  _progressBar.IsVisible = false;
                        if (_progressText != null) _progressText.IsVisible = false;
                        if (_statusDetail != null) _statusDetail.Text = "대기 중";
                    });
                }
            });
        }

        // [Stop] 버튼 클릭 이벤트
        private void OnStopClick(object? sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                AppendLog("[INFO] 작업 중단 요청");

                if (_progressBar != null)  _progressBar.IsVisible = false;
                if (_progressText != null) _progressText.IsVisible = false;
                if (_statusDetail != null) _statusDetail.Text = "작업 중단됨";
            }
        }

        // 로그 추가 및 UI 갱신
        private void AppendLog(string message)
        {
            LogSeverity severity = LogSeverity.Info;
            if (message.StartsWith("[ERROR]"))
                severity = LogSeverity.Error;
            else if (message.StartsWith("[WARN]"))
                severity = LogSeverity.Warn;

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = message,
                Severity = severity
            };

            // 로그는 누적해서 저장 (Clear Log 시점 이전 로그는 필터에서 제외됨)
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                LogEntries.Add(entry);
                UpdateLogFilter();
                UpdateLogStatistics();

                // 새 로그 추가 후 자동 스크롤: 최신 로그가 보이도록 ListBox의 ScrollViewer를 최하단으로 이동
                _logListBox?.ScrollIntoView(entry);
                ScrollToBottom();
            });
        }

        // ScrollViewer를 찾아 최하단으로 스크롤하는 메서드
        private async void ScrollToBottom()
        {
            // 레이아웃 갱신을 위해 약간 지연
            await Task.Delay(50);
            var scrollViewer = _logListBox?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            scrollViewer?.ScrollToEnd();
        }

        // 로그 통계 업데이트 (Clear Log 이후의 로그만 반영)
        private void UpdateLogStatistics()
        {
            if (_totalLogsText == null || _errorCountText == null || _warnCountText == null || _infoCountText == null)
                return;

            var recentLogs = (_logClearTime == null)
                ? LogEntries
                : new ObservableCollection<LogEntry>(LogEntries.Where(x => x.Timestamp > _logClearTime.Value));

            int total = recentLogs.Count;
            int errorCount = recentLogs.Count(x => x.Severity == LogSeverity.Error);
            int warnCount = recentLogs.Count(x => x.Severity == LogSeverity.Warn);
            int infoCount = recentLogs.Count(x => x.Severity == LogSeverity.Info);

            _totalLogsText.Text = $"총 로그 수: {total}";
            _errorCountText.Text = $"에러 로그 수: {errorCount}";
            _warnCountText.Text = $"경고 로그 수: {warnCount}";
            _infoCountText.Text = $"정보 로그 수: {infoCount}";
        }

        // 필터 컨트롤 이벤트 핸들러 (매개변수를 null 허용)
        private void OnLogFilterChanged(object? sender, RoutedEventArgs? e)
        {
            UpdateLogFilter();
        }

        // 필터 조건에 따라 _filteredLogEntries 갱신 (Clear Log 이후의 로그만 포함)
        private void UpdateLogFilter()
        {
            _filteredLogEntries.Clear();
            bool showInfo = _infoCheckBox?.IsChecked ?? true;
            bool showWarn = _warnCheckBox?.IsChecked ?? true;
            bool showError = _errorCheckBox?.IsChecked ?? true;
            string searchText = _logSearchTextBox?.Text?.ToLowerInvariant() ?? "";

            // 최근 로그: _logClearTime가 설정되어 있다면 해당 시각 이후의 로그만 보여줌
            var recentLogs = (_logClearTime == null)
                ? LogEntries
                : LogEntries.Where(x => x.Timestamp > _logClearTime.Value);

            foreach (var log in recentLogs)
            {
                bool include = false;
                if (log.Severity == LogSeverity.Info && showInfo)
                    include = true;
                if (log.Severity == LogSeverity.Warn && showWarn)
                    include = true;
                if (log.Severity == LogSeverity.Error && showError)
                    include = true;
                if (!string.IsNullOrWhiteSpace(searchText) && !log.Message.ToLowerInvariant().Contains(searchText))
                    include = false;
                if (include)
                    _filteredLogEntries.Add(log);
            }
        }

        // Clear Logs 버튼 클릭 시, 기존 로그는 유지하면서 필터 기준 시점을 현재로 설정하여
        // 화면에 표시되는 로그를 '비우는' 효과를 줍니다.
        private void OnClearLogsClick(object? sender, RoutedEventArgs e)
        {
            _logClearTime = DateTime.Now;
            UpdateLogFilter();
            UpdateLogStatistics();
        }

        // 로그 항목 더블클릭 시 상세정보 팝업
        private async void OnLogItemDoubleTapped(object? sender, RoutedEventArgs e)
        {
            var selected = _logListBox?.SelectedItem as LogEntry;
            if (selected != null)
            {
                var dialog = new Window
                {
                    Title = "Log Details",
                    Width = 400,
                    Height = 200,
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            Text = $"Timestamp: {selected.Timestamp}\nSeverity: {selected.Severity}\nMessage: {selected.Message}",
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(10)
                        }
                    }
                };
                await dialog.ShowDialog(this);
            }
        }

        // 메뉴 [Exit] 클릭
        private void OnExitClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        // 메뉴 [About] 클릭
        private async void OnAboutClick(object? sender, RoutedEventArgs e)
        {
            await Task.Run(() => Console.WriteLine("나라장터 크롤러 (Avalonia) - 버전 1.0"));
        }
    }
}


// using Avalonia;
// using Avalonia.Controls;
// using Avalonia.Interactivity;
// using Avalonia.Markup.Xaml;
// using Avalonia.Media;
// using Avalonia.Threading;
// using System;
// using System.Collections.ObjectModel;
// using System.Globalization;
// using System.Linq;
// using System.Threading;
// using System.Threading.Tasks;

// namespace G2BAvaloniaApp
// {
//     /// <summary>
//     /// 로그 심각도
//     /// </summary>
//     public enum LogSeverity
//     {
//         Info,
//         Warn,
//         Error
//     }

//     /// <summary>
//     /// 로그 항목 모델
//     /// </summary>
//     public class LogEntry
//     {
//         public DateTime Timestamp { get; set; }
//         public string Message { get; set; } = "";
//         public LogSeverity Severity { get; set; }
//     }

//     /// <summary>
//     /// 로그 심각도별 텍스트 색상 변환기
//     /// </summary>
//     public class LogSeverityToColorConverter : Avalonia.Data.Converters.IValueConverter
//     {
//         public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
//         {
//             if (value is LogSeverity severity)
//             {
//                 switch (severity)
//                 {
//                     case LogSeverity.Error:
//                         return Brushes.LightCoral;
//                     case LogSeverity.Warn:
//                         return Brushes.Gold;
//                     default:
//                         return Brushes.White;
//                 }
//             }
//             return Brushes.White;
//         }

//         public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
//         {
//             throw new NotImplementedException();
//         }
//     }

//     /// <summary>
//     /// 로그 심각도별 배경색 변환기
//     /// </summary>
//     public class LogSeverityToBackgroundColorConverter : Avalonia.Data.Converters.IValueConverter
//     {
//         public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
//         {
//             if (value is LogSeverity severity)
//             {
//                 switch (severity)
//                 {
//                     case LogSeverity.Error:
//                         return new SolidColorBrush(Color.FromArgb(50, 255, 0, 0));
//                     case LogSeverity.Warn:
//                         return new SolidColorBrush(Color.FromArgb(50, 255, 215, 0));
//                     default:
//                         return Brushes.Transparent;
//                 }
//             }
//             return Brushes.Transparent;
//         }

//         public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
//         {
//             throw new NotImplementedException();
//         }
//     }

//     public partial class MainWindow : Window
//     {
//         // UI 컨트롤 변수
//         private TextBox? _keywordTextBox;
//         private Button?  _startButton;
//         private Button?  _stopButton;
//         private ProgressBar? _progressBar;
//         private TextBlock? _progressText;
//         private TextBlock? _statusDetail;

//         // Details 탭 통계 컨트롤
//         private TextBlock? _totalLogsText;
//         private TextBlock? _errorCountText;
//         private TextBlock? _warnCountText;
//         private TextBlock? _infoCountText;

//         // Log 탭의 인터랙티브 컨트롤들
//         private CheckBox? _infoCheckBox;
//         private CheckBox? _warnCheckBox;
//         private CheckBox? _errorCheckBox;
//         private TextBox? _logSearchTextBox;
//         private Button? _clearLogsButton;
//         private ListBox? _logListBox;

//         private CancellationTokenSource? _cts;

//         // 전체 로그 컬렉션과 필터링된 로그 컬렉션
//         public ObservableCollection<LogEntry> LogEntries { get; } = new ObservableCollection<LogEntry>();
//         private ObservableCollection<LogEntry> _filteredLogEntries = new ObservableCollection<LogEntry>();
//         public ObservableCollection<LogEntry> FilteredLogEntries => _filteredLogEntries;

//         // Clear Log 버튼 클릭 시 기록된 시각 이후의 로그만 보여주도록 함.
//         private DateTime? _logClearTime = null;

//         public MainWindow()
//         {
//             InitializeComponent();
//             DataContext = this;

//             // XAML 컨트롤 찾기
//             _keywordTextBox = this.FindControl<TextBox>("KeywordTextBox");
//             _startButton    = this.FindControl<Button>("StartButton");
//             _stopButton     = this.FindControl<Button>("StopButton");
//             _progressBar    = this.FindControl<ProgressBar>("ProgressBar");
//             _progressText   = this.FindControl<TextBlock>("ProgressText");
//             _statusDetail   = this.FindControl<TextBlock>("StatusDetail");

//             _totalLogsText  = this.FindControl<TextBlock>("TotalLogsText");
//             _errorCountText = this.FindControl<TextBlock>("ErrorCountText");
//             _warnCountText  = this.FindControl<TextBlock>("WarnCountText");
//             _infoCountText  = this.FindControl<TextBlock>("InfoCountText");

//             _infoCheckBox = this.FindControl<CheckBox>("InfoCheckBox");
//             _warnCheckBox = this.FindControl<CheckBox>("WarnCheckBox");
//             _errorCheckBox = this.FindControl<CheckBox>("ErrorCheckBox");
//             _logSearchTextBox = this.FindControl<TextBox>("LogSearchTextBox");
//             _clearLogsButton = this.FindControl<Button>("ClearLogsButton");
//             _logListBox = this.FindControl<ListBox>("LogListBox");

//             // 필터 컨트롤: IsChecked 속성 변경을 구독 (Checked/Unchecked 이벤트 대신)
//             _infoCheckBox?.GetObservable(CheckBox.IsCheckedProperty).Subscribe(_ => OnLogFilterChanged(null, null));
//             _warnCheckBox?.GetObservable(CheckBox.IsCheckedProperty).Subscribe(_ => OnLogFilterChanged(null, null));
//             _errorCheckBox?.GetObservable(CheckBox.IsCheckedProperty).Subscribe(_ => OnLogFilterChanged(null, null));

//             // LogSearchTextBox의 텍스트 변경 구독
//             _logSearchTextBox?.GetObservable(TextBox.TextProperty).Subscribe(_ => OnLogFilterChanged(null, null));

//             // ClearLogs 버튼 이벤트 구독 (null 체크 후)
//             if (_clearLogsButton != null)
//             {
//                 _clearLogsButton.Click += OnClearLogsClick;
//             }
//             // 로그 목록의 더블클릭 이벤트 구독 (null 체크 후)
//             if (_logListBox != null)
//             {
//                 _logListBox.DoubleTapped += OnLogItemDoubleTapped;
//             }

//             // 초기 진행 상태 UI 숨김
//             if (_progressBar != null)  _progressBar.IsVisible = false;
//             if (_progressText != null) _progressText.IsVisible = false;
//         }

//         private void InitializeComponent()
//         {
//             AvaloniaXamlLoader.Load(this);
//         }

//         // [Start] 버튼 클릭 이벤트
//         private void OnStartClick(object? sender, RoutedEventArgs e)
//         {
//             if (_cts != null) return; // 이미 실행 중이면 무시

//             _cts = new CancellationTokenSource();

//             string keyword = _keywordTextBox?.Text?.Trim() ?? "";
//             if (string.IsNullOrEmpty(keyword))
//                 keyword = "RPA";

//             AppendLog($"[INFO] 검색어 = {keyword}");

//             if (_progressBar != null)  _progressBar.IsVisible = true;
//             if (_progressText != null) _progressText.IsVisible = true;
//             if (_statusDetail != null) _statusDetail.Text = "작업 진행 중...";

//             // Worker.cs와 WorkerOptions.cs 파일의 구현을 사용
//             var options = new WorkerOptions { Keyword = keyword };
//             var worker = new Worker(options);
//             worker.OnLog += AppendLog;

//             // _fire-and-forget_ 방식으로 작업 실행 (CS4014 경고는 의도한 동작)
//             _ = Task.Run(async () =>
//             {
//                 try
//                 {
//                     await worker.RunAsync(_cts.Token);
//                 }
//                 catch (Exception ex)
//                 {
//                     AppendLog($"[ERROR] {ex.Message}");
//                 }
//                 finally
//                 {
//                     _cts = null;
//                     Dispatcher.UIThread.InvokeAsync(() =>
//                     {
//                         if (_progressBar != null)  _progressBar.IsVisible = false;
//                         if (_progressText != null) _progressText.IsVisible = false;
//                         if (_statusDetail != null) _statusDetail.Text = "대기 중";
//                     });
//                 }
//             });
//         }

//         // [Stop] 버튼 클릭 이벤트
//         private void OnStopClick(object? sender, RoutedEventArgs e)
//         {
//             if (_cts != null)
//             {
//                 _cts.Cancel();
//                 AppendLog("[INFO] 작업 중단 요청");

//                 if (_progressBar != null)  _progressBar.IsVisible = false;
//                 if (_progressText != null) _progressText.IsVisible = false;
//                 if (_statusDetail != null) _statusDetail.Text = "작업 중단됨";
//             }
//         }

//         // 로그 추가 및 UI 갱신
//         private void AppendLog(string message)
//         {
//             LogSeverity severity = LogSeverity.Info;
//             if (message.StartsWith("[ERROR]"))
//                 severity = LogSeverity.Error;
//             else if (message.StartsWith("[WARN]"))
//                 severity = LogSeverity.Warn;

//             var entry = new LogEntry
//             {
//                 Timestamp = DateTime.Now,
//                 Message = message,
//                 Severity = severity
//             };

//             // 로그는 누적해서 저장 (Clear Log 시점 이전 로그는 필터에서 제외됨)
//             Dispatcher.UIThread.InvokeAsync(() =>
//             {
//                 LogEntries.Add(entry);
//                 UpdateLogFilter();
//                 UpdateLogStatistics();

//                 // 새 로그 추가 시 자동 스크롤
//                 _logListBox?.ScrollIntoView(entry);
//             });
//         }

//         // 로그 통계 업데이트 (Clear Log 이후의 로그만 반영)
//         private void UpdateLogStatistics()
//         {
//             if (_totalLogsText == null || _errorCountText == null || _warnCountText == null || _infoCountText == null)
//                 return;

//             var recentLogs = (_logClearTime == null)
//                 ? LogEntries
//                 : new ObservableCollection<LogEntry>(LogEntries.Where(x => x.Timestamp > _logClearTime.Value));

//             int total = recentLogs.Count;
//             int errorCount = recentLogs.Count(x => x.Severity == LogSeverity.Error);
//             int warnCount = recentLogs.Count(x => x.Severity == LogSeverity.Warn);
//             int infoCount = recentLogs.Count(x => x.Severity == LogSeverity.Info);

//             _totalLogsText.Text = $"총 로그 수: {total}";
//             _errorCountText.Text = $"에러 로그 수: {errorCount}";
//             _warnCountText.Text = $"경고 로그 수: {warnCount}";
//             _infoCountText.Text = $"정보 로그 수: {infoCount}";
//         }

//         // 필터 컨트롤 이벤트 핸들러 (매개변수를 null 허용)
//         private void OnLogFilterChanged(object? sender, RoutedEventArgs? e)
//         {
//             UpdateLogFilter();
//         }

//         // 필터 조건에 따라 _filteredLogEntries 갱신 (Clear Log 이후의 로그만 포함)
//         private void UpdateLogFilter()
//         {
//             _filteredLogEntries.Clear();
//             bool showInfo = _infoCheckBox?.IsChecked ?? true;
//             bool showWarn = _warnCheckBox?.IsChecked ?? true;
//             bool showError = _errorCheckBox?.IsChecked ?? true;
//             string searchText = _logSearchTextBox?.Text?.ToLowerInvariant() ?? "";

//             // 최근 로그: _logClearTime가 설정되어 있다면 해당 시각 이후의 로그만 보여줌
//             var recentLogs = (_logClearTime == null)
//                 ? LogEntries
//                 : LogEntries.Where(x => x.Timestamp > _logClearTime.Value);

//             foreach (var log in recentLogs)
//             {
//                 bool include = false;
//                 if (log.Severity == LogSeverity.Info && showInfo)
//                     include = true;
//                 if (log.Severity == LogSeverity.Warn && showWarn)
//                     include = true;
//                 if (log.Severity == LogSeverity.Error && showError)
//                     include = true;
//                 if (!string.IsNullOrWhiteSpace(searchText) && !log.Message.ToLowerInvariant().Contains(searchText))
//                     include = false;
//                 if (include)
//                     _filteredLogEntries.Add(log);
//             }
//         }

//         // Clear Logs 버튼 클릭 시, 기존 로그는 유지하면서 필터링 기준 시점을 현재로 설정하여
//         // 화면에 표시되는 로그를 '비우는' 효과를 줍니다.
//         private void OnClearLogsClick(object? sender, RoutedEventArgs e)
//         {
//             _logClearTime = DateTime.Now;
//             UpdateLogFilter();
//             UpdateLogStatistics();
//         }

//         // 로그 항목 더블클릭 시 상세정보 팝업
//         private async void OnLogItemDoubleTapped(object? sender, RoutedEventArgs e)
//         {
//             var selected = _logListBox?.SelectedItem as LogEntry;
//             if (selected != null)
//             {
//                 var dialog = new Window
//                 {
//                     Title = "Log Details",
//                     Width = 400,
//                     Height = 200,
//                     Content = new ScrollViewer
//                     {
//                         Content = new TextBlock
//                         {
//                             Text = $"Timestamp: {selected.Timestamp}\nSeverity: {selected.Severity}\nMessage: {selected.Message}",
//                             TextWrapping = TextWrapping.Wrap,
//                             Margin = new Thickness(10)
//                         }
//                     }
//                 };
//                 await dialog.ShowDialog(this);
//             }
//         }

//         // 메뉴 [Exit] 클릭
//         private void OnExitClick(object? sender, RoutedEventArgs e)
//         {
//             Close();
//         }

//         // 메뉴 [About] 클릭
//         private async void OnAboutClick(object? sender, RoutedEventArgs e)
//         {
//             await Task.Run(() => Console.WriteLine("나라장터 크롤러 (Avalonia) - 버전 1.0"));
//         }
//     }
// }
