// Worker.cs

using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using OfficeOpenXml; // EPPlus
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace G2BAvaloniaApp
{
    public class Worker
    {
        private readonly WorkerOptions _options;

        // 로그 이벤트 (UI에서 구독)
        public event Action<string>? OnLog;

        public Worker(WorkerOptions options)
        {
            _options = options;
        }

        public async Task RunAsync(CancellationToken token)
        {
            LogInfo($"나라장터 크롤러 시작 (검색어: {_options.Keyword})");

            var chromeOptions = new ChromeOptions();
            chromeOptions.AddArgument("--disable-gpu");
            // chromeOptions.AddArgument("--headless");

            using (var driver = new ChromeDriver(chromeOptions))
            {
                try
                {
                    driver.Navigate().GoToUrl("https://www.g2b.go.kr/index.jsp");
                    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));

                    var openSearchButton = wait.Until(drv => drv.FindElement(By.Id("mf_wfm_gnb_wfm_gnbBtm_btnTotalSrch")));
                    openSearchButton.Click();
                    Thread.Sleep(500);

                    var searchBox = wait.Until(drv => drv.FindElement(By.Id("mf_wfm_gnb_wfm_gnbBtm_inpGlobalSearch")));
                    searchBox.Clear();
                    searchBox.SendKeys(_options.Keyword);

                    var searchGoBtn = wait.Until(drv => drv.FindElement(By.Id("mf_wfm_gnb_wfm_gnbBtm_btnGlobalSearch")));
                    searchGoBtn.Click();

                    WaitForProcessBarToDisappear(driver);

                    // 블록 순회
                    bool hasNext = true;
                    int blockIndex = 1;
                    var allData = new List<SearchData>();
                    var prevBlockData = new List<SearchData>();

                    while (!token.IsCancellationRequested && hasNext)
                    {
                        LogInfo($"=== 블록 {blockIndex} 시작 ===");
                        var pageLinks = GetCurrentBlockPages(driver);
                        if (pageLinks.Count == 0)
                        {
                            LogWarn("페이지 번호가 없습니다 -> 중단");
                            break;
                        }

                        var currentBlockData = new List<SearchData>();
                        for (int i = 0; i < pageLinks.Count; i++)
                        {
                            if (token.IsCancellationRequested) break;

                            WaitForProcessBarToDisappear(driver);

                            try
                            {
                                ClickLinkByJs(driver, pageLinks[i]);
                                Thread.Sleep(1000);

                                WaitForProcessBarToDisappear(driver);
                                var rows = ScrapeOnePage(driver);
                                currentBlockData.AddRange(rows);
                                LogInfo($"[페이지 {i+1}/{pageLinks.Count}] {rows.Count}건 수집 (누적: {currentBlockData.Count})");
                            }
                            catch (Exception ex)
                            {
                                LogError($"[오류] {ex.Message}");
                                continue;
                            }

                            // Stale 방지 재획득
                            pageLinks = GetCurrentBlockPages(driver);
                        }

                        // 무한 루프 방지
                        if (currentBlockData.Count > 0 && prevBlockData.Count == currentBlockData.Count)
                        {
                            if (IsSameBlock(prevBlockData, currentBlockData))
                            {
                                LogWarn("이전 블록과 동일 -> 중단");
                                break;
                            }
                        }

                        allData.AddRange(currentBlockData);
                        prevBlockData = currentBlockData;

                        hasNext = ClickNextBlockIfExists(driver);
                        if (hasNext)
                        {
                            blockIndex++;
                            Thread.Sleep(1000);
                            WaitForProcessBarToDisappear(driver);
                        }
                    }

                    LogInfo($"총 {allData.Count}건 수집 -> 엑셀 저장");
                    SaveToExcel(allData);
                    LogInfo("엑셀 저장 완료");
                }
                catch (Exception ex)
                {
                    LogError($"크롤링 오류: {ex.Message}");
                }
            }

            LogInfo("크롤러 종료");
            await Task.Delay(500, token); // 살짝 대기
        }

        private bool IsSameBlock(List<SearchData> a, List<SearchData> b)
        {
            if (a.Count == 0 || b.Count == 0) return false;
            if (a[0].Equals(b[0]) && a[^1].Equals(b[^1])) return true;
            return false;
        }

        private void WaitForProcessBarToDisappear(IWebDriver driver, int timeoutSec = 10)
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSec));
            try
            {
                wait.Until(drv =>
                {
                    try
                    {
                        var e = drv.FindElement(By.Id("___processbar2"));
                        return !e.Displayed; // 표시중이면 false, 사라지면 true
                    }
                    catch (NoSuchElementException)
                    {
                        return true; // 없음 -> 이미 사라짐
                    }
                });
            }
            catch (WebDriverTimeoutException)
            {
                LogWarn("로딩창이 사라지지 않음 -> 계속 진행");
            }
        }

        private List<IWebElement> GetCurrentBlockPages(IWebDriver driver)
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));
            var pageListDiv = wait.Until(drv => drv.FindElement(By.CssSelector("#mf_wfm_container_pglList")));
            var ul = pageListDiv.FindElement(By.CssSelector("ul.w2pageList_ul"));
            var liList = ul.FindElements(By.CssSelector("li.w2pageList_li_label"));

            var list = new List<IWebElement>();
            foreach (var li in liList)
            {
                var aTag = li.FindElement(By.TagName("a"));
                if (aTag != null) list.Add(aTag);
            }
            return list;
        }

        private bool ClickNextBlockIfExists(IWebDriver driver)
        {
            try
            {
                var nextBtn = driver.FindElement(By.Id("mf_wfm_container_pglList_next_btn"));
                var cls = nextBtn.GetAttribute("class");
                if (cls.Contains("disable"))
                {
                    LogInfo("다음 블록 버튼 disable -> 마지막");
                    return false;
                }
                ClickLinkByJs(driver, nextBtn);
                return true;
            }
            catch (NoSuchElementException)
            {
                LogInfo("다음 블록 버튼 없음 -> 마지막");
                return false;
            }
            catch (ElementClickInterceptedException ex)
            {
                LogError($"블록 이동 실패: {ex.Message}");
                return false;
            }
        }

        private List<SearchData> ScrapeOnePage(IWebDriver driver)
        {
            var list = new List<SearchData>();
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

            var table = wait.Until(drv => drv.FindElement(By.Id("mf_wfm_container_testTable")));
            var tbody = table.FindElement(By.Id("mf_wfm_container_grdTotalSrch"));
            var trList = tbody.FindElements(By.CssSelector("tr.w2group.sdtg"));

            foreach (var tr in trList)
            {
                var tds = tr.FindElements(By.TagName("td"));
                if (tds.Count < 14) continue;

                // 14개 중 4번째(인덱스=3) "더보기/닫기" 제외
                var item = new SearchData
                {
                    No            = CleanField(tds[0].Text),
                    Step          = CleanField(tds[1].Text),
                    Work          = CleanField(tds[2].Text),
                    Title         = CleanField(tds[3].Text),
                    BizNo         = CleanField(tds[5].Text),
                    BizDate       = CleanField(tds[6].Text),
                    Inst          = CleanField(tds[7].Text),
                    Demand        = CleanField(tds[8].Text),
                    NoticeDate    = CleanField(tds[9].Text),
                    ContractType  = CleanField(tds[10].Text),
                    Method        = CleanField(tds[11].Text),
                    Amt           = CleanField(tds[12].Text),
                    RefNo         = CleanField(tds[13].Text),
                };
                list.Add(item);
            }
            return list;
        }

        private string CleanField(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "-") return "";
            return raw.Trim();
        }

        private void ClickLinkByJs(IWebDriver driver, IWebElement el)
        {
            var js = (IJavaScriptExecutor)driver;
            js.ExecuteScript("arguments[0].scrollIntoView(true);", el);
            Thread.Sleep(200);
            js.ExecuteScript("arguments[0].click();", el);
        }

        private void SaveToExcel(List<SearchData> data)
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string filePath = Path.Combine(desktopPath, "나라장터_검색결과_app.xlsx");

            // EPPlus 라이선스
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage(new FileInfo(filePath));

            var sheet = package.Workbook.Worksheets.Count > 0
                ? package.Workbook.Worksheets[0]
                : package.Workbook.Worksheets.Add("검색결과");

            sheet.Cells.Clear();

            // 13개 컬럼 헤더
            sheet.Cells[1,1].Value = "No";
            sheet.Cells[1,2].Value = "단계구분";
            sheet.Cells[1,3].Value = "업무구분";
            sheet.Cells[1,4].Value = "사업명";
            sheet.Cells[1,5].Value = "사업번호";
            sheet.Cells[1,6].Value = "사업일자";
            sheet.Cells[1,7].Value = "공고·계약기관";
            sheet.Cells[1,8].Value = "수요기관";
            sheet.Cells[1,9].Value = "공고일자";
            sheet.Cells[1,10].Value = "계약구분";
            sheet.Cells[1,11].Value = "계약방법";
            sheet.Cells[1,12].Value = "계약금액";
            sheet.Cells[1,13].Value = "참조번호";

            int row = 2;
            foreach (var item in data)
            {
                sheet.Cells[row,1].Value  = item.No;
                sheet.Cells[row,2].Value  = item.Step;
                sheet.Cells[row,3].Value  = item.Work;
                sheet.Cells[row,4].Value  = item.Title;
                sheet.Cells[row,5].Value  = item.BizNo;
                sheet.Cells[row,6].Value  = item.BizDate;
                sheet.Cells[row,7].Value  = item.Inst;
                sheet.Cells[row,8].Value  = item.Demand;
                sheet.Cells[row,9].Value  = item.NoticeDate;
                sheet.Cells[row,10].Value = item.ContractType;
                sheet.Cells[row,11].Value = item.Method;
                sheet.Cells[row,12].Value = item.Amt;
                sheet.Cells[row,13].Value = item.RefNo;
                row++;
            }

            package.Save();
            LogInfo($"엑셀 저장 완료: {filePath}");
        }

        // 로그 출력 (이벤트)
        private void LogInfo(string msg) => OnLog?.Invoke($"[INFO] {msg}");
        private void LogWarn(string msg) => OnLog?.Invoke($"[WARN] {msg}");
        private void LogError(string msg) => OnLog?.Invoke($"[ERROR] {msg}");
    }
}
