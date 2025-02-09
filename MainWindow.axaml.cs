using System;
using System.Collections.Generic;         // 추가: IEnumerable<> 사용을 위해
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

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

    /// <summary>
    /// 로그 표시 모드를 나타내는 열거형
    /// </summary>
    public enum LogDisplayMode
    {
        All,
        Latest
    }

    public partial class MainWindow : Window
    {
        // UI 컨트롤 변수
        private TextBox? _keywordTextBox;
        private Button? _startButton;
        private Button? _stopButton;
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
        private ComboBox? _displayModeComboBox;

        private CancellationTokenSource? _cts;

        // 전체 로그 컬렉션과 필터링된 로그 컬렉션
        public ObservableCollection<LogEntry> LogEntries { get; } = new ObservableCollection<LogEntry>();
        private ObservableCollection<LogEntry> _filteredLogEntries = new ObservableCollection<LogEntry>();
        public ObservableCollection<LogEntry> FilteredLogEntries => _filteredLogEntries;

        // Latest 모드에서의 Clear Log 기준 시각 (이후의 로그만 UI에 표시)
        private DateTime? _logClearTime = null;

        // 한 화면에 표시할 최대 로그 건수 (Latest 모드)
        private const int MaxDisplayLogs = 100;

        // 현재 표시 모드 (All 또는 Latest)
        private LogDisplayMode _currentDisplayMode = LogDisplayMode.All;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            // XAML 컨트롤 찾기
            _keywordTextBox = this.FindControl<TextBox>("KeywordTextBox");
            _startButton = this.FindControl<Button>("StartButton");
            _stopButton = this.FindControl<Button>("StopButton");
            _progressBar = this.FindControl<ProgressBar>("ProgressBar");
            _progressText = this.FindControl<TextBlock>("ProgressText");
            _statusDetail = this.FindControl<TextBlock>("StatusDetail");

            _totalLogsText = this.FindControl<TextBlock>("TotalLogsText");
            _errorCountText = this.FindControl<TextBlock>("ErrorCountText");
            _warnCountText = this.FindControl<TextBlock>("WarnCountText");
            _infoCountText = this.FindControl<TextBlock>("InfoCountText");

            _infoCheckBox = this.FindControl<CheckBox>("InfoCheckBox");
            _warnCheckBox = this.FindControl<CheckBox>("WarnCheckBox");
            _errorCheckBox = this.FindControl<CheckBox>("ErrorCheckBox");
            _logSearchTextBox = this.FindControl<TextBox>("LogSearchTextBox");
            _clearLogsButton = this.FindControl<Button>("ClearLogsButton");
            _logListBox = this.FindControl<ListBox>("LogListBox");
            _displayModeComboBox = this.FindControl<ComboBox>("DisplayModeComboBox");

            // Display Mode 콤보박스 이벤트 구독
            if (_displayModeComboBox != null)
            {
                _displayModeComboBox.SelectionChanged += (s, e) =>
                {
                    if (_displayModeComboBox.SelectedIndex == 0)
                    {
                        _currentDisplayMode = LogDisplayMode.All;
                        _logClearTime = null; // All 모드에서는 Clear Log 기준 시간 무시
                    }
                    else
                    {
                        _currentDisplayMode = LogDisplayMode.Latest;
                        if (_logClearTime == null)
                        {
                            _logClearTime = DateTime.Now;
                        }
                    }
                    UpdateLogFilter();
                };
            }

            // 필터 컨트롤: IsChecked 및 텍스트 변경 구독
            _infoCheckBox?.GetObservable(CheckBox.IsCheckedProperty).Subscribe(_ => OnLogFilterChanged(null, null));
            _warnCheckBox?.GetObservable(CheckBox.IsCheckedProperty).Subscribe(_ => OnLogFilterChanged(null, null));
            _errorCheckBox?.GetObservable(CheckBox.IsCheckedProperty).Subscribe(_ => OnLogFilterChanged(null, null));
            _logSearchTextBox?.GetObservable(TextBox.TextProperty).Subscribe(_ => OnLogFilterChanged(null, null));

            // 이벤트 구독 (null 체크 후)
            if (_clearLogsButton != null)
            {
                _clearLogsButton.Click += OnClearLogsClick;
            }
            if (_logListBox != null)
            {
                _logListBox.DoubleTapped += OnLogItemDoubleTapped;
            }

            // 초기 진행 상태 UI 숨김
            if (_progressBar != null) _progressBar.IsVisible = false;
            if (_progressText != null) _progressText.IsVisible = false;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // [Start] 버튼 클릭 이벤트 (async void로 변경)
        private async void OnStartClick(object? sender, RoutedEventArgs e)
        {
            if (_cts != null) return; // 이미 실행 중이면 무시

            _cts = new CancellationTokenSource();

            string keyword = _keywordTextBox?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(keyword))
                keyword = "RPA";

            AppendLog($"[INFO] 검색어 = {keyword}");

            if (_progressBar != null) _progressBar.IsVisible = true;
            if (_progressText != null) _progressText.IsVisible = true;
            if (_statusDetail != null) _statusDetail.Text = "작업 진행 중...";

            // Worker.cs와 WorkerOptions.cs 파일의 구현 사용
            var options = new WorkerOptions { Keyword = keyword };
            var worker = new Worker(options);
            worker.OnLog += AppendLog;

            // async/await를 사용하여 Task.Run의 완료를 기다림 (CS4014 경고 해소)
            await Task.Run(async () =>
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
                        if (_progressBar != null) _progressBar.IsVisible = false;
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

                if (_progressBar != null) _progressBar.IsVisible = false;
                if (_progressText != null) _progressText.IsVisible = false;
                if (_statusDetail != null) _statusDetail.Text = "작업 중단됨";
            }
        }

        // 로그 추가 및 UI 갱신 (LogEntries는 계속 누적)
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

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                LogEntries.Add(entry);
                UpdateLogFilter();
                UpdateLogStatistics();
                // 최신 로그(FilteredLogEntries의 첫 항목)를 보이도록 스크롤
                if (_filteredLogEntries.Any())
                {
                    _logListBox?.ScrollIntoView(_filteredLogEntries.First());
                }
            });
        }

        // 로그 통계 업데이트 (전체 LogEntries 기준)
        private void UpdateLogStatistics()
        {
            if (_totalLogsText == null || _errorCountText == null || _warnCountText == null || _infoCountText == null)
                return;

            int total = LogEntries.Count;
            int errorCount = LogEntries.Count(x => x.Severity == LogSeverity.Error);
            int warnCount = LogEntries.Count(x => x.Severity == LogSeverity.Warn);
            int infoCount = LogEntries.Count(x => x.Severity == LogSeverity.Info);

            _totalLogsText.Text = $"총 로그 수: {total}";
            _errorCountText.Text = $"에러 로그 수: {errorCount}";
            _warnCountText.Text = $"경고 로그 수: {warnCount}";
            _infoCountText.Text = $"정보 로그 수: {infoCount}";
        }

        // 필터 컨트롤 이벤트 핸들러 (매개변수 null 허용)
        private void OnLogFilterChanged(object? sender, RoutedEventArgs? e)
        {
            UpdateLogFilter();
        }

        // 필터 조건에 따라 _filteredLogEntries 갱신  
        // - All 모드: 전체 LogEntries 중 필터 조건에 맞는 항목을 표시  
        // - Latest 모드: _logClearTime 이후의 로그를 내림차순 정렬하여 최대 MaxDisplayLogs 건 표시
        private void UpdateLogFilter()
        {
            _filteredLogEntries.Clear();
            bool showInfo = _infoCheckBox?.IsChecked ?? true;
            bool showWarn = _warnCheckBox?.IsChecked ?? true;
            bool showError = _errorCheckBox?.IsChecked ?? true;
            string searchText = _logSearchTextBox?.Text?.ToLowerInvariant() ?? "";

            IEnumerable<LogEntry> logs;
            if (_currentDisplayMode == LogDisplayMode.All)
            {
                logs = LogEntries;
            }
            else // Latest 모드
            {
                logs = _logClearTime == null ? LogEntries : LogEntries.Where(x => x.Timestamp > _logClearTime.Value);
                logs = logs.OrderByDescending(x => x.Timestamp).Take(MaxDisplayLogs);
            }

            foreach (var log in logs)
            {
                bool include = false;
                if (log.Severity == LogSeverity.Info && showInfo)
                    include = true;
                if (log.Severity == LogSeverity.Warn && showWarn)
                    include = true;
                if (log.Severity == LogSeverity.Error && showError)
                    include = true;
                if (!string.IsNullOrWhiteSpace(searchText) &&
                    !log.Message.ToLowerInvariant().Contains(searchText))
                    include = false;
                if (include)
                    _filteredLogEntries.Add(log);
            }
        }

        // Clear Logs 버튼 클릭 시 동작
        // - All 모드: 전체 LogEntries를 지워 UI와 통계 모두 초기화  
        // - Latest 모드: _logClearTime을 현재로 재설정하여 그 이후 생성된 로그만 UI에 표시 (통계는 전체 기준)
        private void OnClearLogsClick(object? sender, RoutedEventArgs e)
        {
            if (_currentDisplayMode == LogDisplayMode.All)
            {
                LogEntries.Clear();
                _logClearTime = null;
            }
            else // Latest 모드
            {
                _logClearTime = DateTime.Now;
            }
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
