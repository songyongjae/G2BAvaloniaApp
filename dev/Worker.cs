// Worker.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using OfficeOpenXml;
using SeleniumExtras.WaitHelpers;

public class Worker : BackgroundService
{
    private readonly WorkerOptions _options;

    // 생성자에서 WorkerOptions 주입
    public Worker(WorkerOptions options)
    {
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 시작 로고
        LogInfo("나라장터 크롤러 시작...");
        LogInfo($"검색어 = \"{_options.Keyword}\"");

        var options = new ChromeOptions();
        options.AddArgument("--disable-gpu");
        // options.AddArgument("--headless"); // 필요시

        using (var driver = new ChromeDriver(options))
        {
            try
            {
                // 1) 나라장터 메인 접속 + 검색
                driver.Navigate().GoToUrl("https://www.g2b.go.kr/index.jsp");
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));

                // 검색창 열기
                var openSearchButton = wait.Until(drv => drv.FindElement(By.Id("mf_wfm_gnb_wfm_gnbBtm_btnTotalSrch")));
                openSearchButton.Click();
                Thread.Sleep(1000);

                // 검색어 입력
                var searchBox = wait.Until(drv => drv.FindElement(By.Id("mf_wfm_gnb_wfm_gnbBtm_inpGlobalSearch")));
                searchBox.Clear();
                searchBox.SendKeys(_options.Keyword);

                // 검색 버튼
                var searchGoButton = wait.Until(drv => drv.FindElement(By.Id("mf_wfm_gnb_wfm_gnbBtm_btnGlobalSearch")));
                searchGoButton.Click();

                WaitForProcessBarToDisappear(driver);

                // 2) 블록 단위로 순회
                var allData = new List<SearchData>();
                bool hasNextBlock = true;
                int blockIndex = 1;

                // "이전 블록"에서 수집한 데이터 (무한 루프 방지용)
                List<SearchData> prevBlockData = new List<SearchData>();

                while (!stoppingToken.IsCancellationRequested && hasNextBlock)
                {
                    LogSeparator();
                    LogInfo($"=== 블록 {blockIndex} 시작 ===");

                    // (A) 현재 블록의 페이지번호들 (ex. 1~10)
                    var pageLinks = GetCurrentBlockPages(driver);
                    if (pageLinks.Count == 0)
                    {
                        LogWarn("페이지 번호가 하나도 없습니다. -> 중단.");
                        break;
                    }

                    var currentBlockData = new List<SearchData>();

                    // (B) 블록 내 각 페이지 순회
                    for (int i = 0; i < pageLinks.Count; i++)
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        WaitForProcessBarToDisappear(driver);

                        try
                        {
                            // 스크롤 + JS click
                            ClickLinkByJs(driver, pageLinks[i]);
                            Thread.Sleep(1500);
                            WaitForProcessBarToDisappear(driver);

                            // 현재 페이지 테이블 파싱
                            var rows = ScrapeOnePage(driver);
                            LogInfo($"[블록={blockIndex}, 페이지={i+1}/{pageLinks.Count}] {rows.Count}건 수집");
                            currentBlockData.AddRange(rows);
                        }
                        catch (Exception ex)
                        {
                            LogError($"⚠ 페이지 클릭/스크래핑 예외 발생: {ex.Message}");
                            // 해당 페이지는 skip 후 계속
                            continue;
                        }

                        // 페이지가 새로 로딩되었으므로, pageLinks가 Stale 될 수 있어 다시 구함
                        pageLinks = GetCurrentBlockPages(driver);
                        if (i+1 >= pageLinks.Count) 
                            break; 
                    }

                    // (C) 무한 루프 방지 - 이전 블록과 동일 데이터인지 비교
                    if (currentBlockData.Count > 0 && prevBlockData.Count == currentBlockData.Count)
                    {
                        var firstPrev = prevBlockData[0];
                        var firstCurr = currentBlockData[0];
                        var lastPrev  = prevBlockData[prevBlockData.Count - 1];
                        var lastCurr  = currentBlockData[currentBlockData.Count - 1];

                        if (firstPrev.Equals(firstCurr) && lastPrev.Equals(lastCurr))
                        {
                            LogWarn("⚠ 이전 블록과 동일한 데이터 -> 무한 루프 가능성, 중단합니다.");
                            break;
                        }
                    }

                    // 이번 블록 데이터를 allData에 추가
                    allData.AddRange(currentBlockData);
                    prevBlockData = currentBlockData;

                    // (D) 다음 블록(>) 이동
                    hasNextBlock = ClickNextBlockIfExists(driver);
                    if (hasNextBlock)
                    {
                        blockIndex++;
                        Thread.Sleep(1500);
                        WaitForProcessBarToDisappear(driver);
                    }
                }

                // 3) 최종 엑셀 저장
                LogSeparator();
                LogInfo($"총 {allData.Count} 건 수집 완료. 엑셀 파일 저장 중...");

                SaveToExcel(allData);

                LogInfo($"엑셀 저장 완료! (총 {allData.Count} 건)");
            }
            catch (Exception ex)
            {
                LogError($"❌ 오류 발생: {ex.Message}");
            }
        }

        // 앱을 너무 빨리 종료하지 않도록 잠깐 대기 (필요 없으면 주석 처리 가능)
        await Task.Delay(30000, stoppingToken);

        LogInfo("작업 종료.");
    }

    #region 주요 메서드들 (기존 로직 그대로)

    private List<IWebElement> GetCurrentBlockPages(IWebDriver driver)
    {
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));
        var pageListDiv = wait.Until(drv => drv.FindElement(By.CssSelector("#mf_wfm_container_pglList")));
        var ul = pageListDiv.FindElement(By.CssSelector("ul.w2pageList_ul"));
        var liList = ul.FindElements(By.CssSelector("li.w2pageList_li_label"));

        var pageLinks = new List<IWebElement>();
        foreach (var li in liList)
        {
            var aTag = li.FindElement(By.TagName("a"));
            if (aTag != null)
            {
                pageLinks.Add(aTag);
            }
        }
        return pageLinks;
    }

    private bool ClickNextBlockIfExists(IWebDriver driver)
    {
        try
        {
            var nextBlockBtn = driver.FindElement(By.Id("mf_wfm_container_pglList_next_btn"));
            var cls = nextBlockBtn.GetAttribute("class");
            if (cls.Contains("disable"))
            {
                LogInfo("▶ 다음 블록 버튼 disable -> 마지막 블록");
                return false;
            }
            ClickLinkByJs(driver, nextBlockBtn);
            return true;
        }
        catch (NoSuchElementException)
        {
            LogInfo("▶ 다음 블록 버튼 없음 -> 마지막 블록");
            return false;
        }
        catch (ElementClickInterceptedException ex)
        {
            LogError($"▶ 블록 이동 클릭 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 테이블 파싱 - 14개 중 4번째 열은 '더보기/닫기'임 -> 13개 컬럼만 추출
    /// </summary>
    private List<SearchData> ScrapeOnePage(IWebDriver driver)
    {
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
        var table = wait.Until(drv => drv.FindElement(By.Id("mf_wfm_container_testTable")));
        var tbody = table.FindElement(By.Id("mf_wfm_container_grdTotalSrch"));
        var trList = tbody.FindElements(By.CssSelector("tr.w2group.sdtg"));

        var list = new List<SearchData>();
        foreach (var tr in trList)
        {
            var tds = tr.FindElements(By.TagName("td"));
            if (tds.Count < 14) continue;

            // tds[4]는 "더보기"/"닫기" 칸이므로 스킵
            var item = new SearchData
            {
                No         = CleanField(tds[0].Text),
                Step       = CleanField(tds[1].Text),
                Work       = CleanField(tds[2].Text),
                Title      = CleanField(tds[3].Text),
                BizNo      = CleanField(tds[5].Text),
                BizDate    = CleanField(tds[6].Text),
                Inst       = CleanField(tds[7].Text),
                Demand     = CleanField(tds[8].Text),
                NoticeDate = CleanField(tds[9].Text),
                ContractType = CleanField(tds[10].Text),
                Method     = CleanField(tds[11].Text),
                Amt        = CleanField(tds[12].Text),
                RefNo      = CleanField(tds[13].Text)
            };
            list.Add(item);
        }
        return list;
    }

    private string CleanField(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "-")
            return "";
        return raw.Trim();
    }

    private void WaitForProcessBarToDisappear(IWebDriver driver, int timeoutSec=10)
    {
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSec));
        try
        {
            wait.Until(ExpectedConditions.InvisibilityOfElementLocated(By.Id("___processbar2")));
        }
        catch (WebDriverTimeoutException)
        {
            LogWarn("⚠ 로딩창이 일정 시간 안에 사라지지 않음 -> 계속 진행");
        }
    }

    private void ClickLinkByJs(IWebDriver driver, IWebElement element)
    {
        IJavaScriptExecutor js = (IJavaScriptExecutor)driver;
        js.ExecuteScript("arguments[0].scrollIntoView(true);", element);
        Thread.Sleep(500);
        js.ExecuteScript("arguments[0].click();", element);
    }

    private void SaveToExcel(List<SearchData> data)
    {
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string filePath = Path.Combine(desktopPath, "나라장터_검색결과.xlsx");

        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using (var package = new ExcelPackage(new FileInfo(filePath)))
        {
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

            int row = 1;
            foreach (var item in data)
            {
                row++;
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
            }

            package.Save();
        }

        LogInfo($"📂 전체 데이터 저장 완료: {filePath}");
    }

    #endregion

    #region 콘솔 로그 헬퍼
    private void LogInfo(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(msg);
        Console.ResetColor();
    }

    private void LogWarn(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(msg);
        Console.ResetColor();
    }

    private void LogError(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(msg);
        Console.ResetColor();
    }

    private void LogSeparator()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("-------------------------------------------------");
        Console.ResetColor();
    }
    #endregion
}
