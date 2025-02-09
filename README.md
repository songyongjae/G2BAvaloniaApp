# G2BAvaloniaApp

## Setup Instructions

이 프로젝트는 Avalonia UI 기반으로 개발된 나라장터 크롤러 애플리케이션입니다. 사용자는 검색 키워드를 입력하고, Selenium을 이용하여 나라장터에서 데이터를 크롤링한 후, EPPlus를 통해 Excel 파일로 저장할 수 있습니다.

### 1. 필수 요구사항
- .NET 9.0 이상
- Avalonia UI 11.2.3
- Selenium WebDriver (ChromeDriver 필요)
- EPPlus 7.6.0

### 2. 프로젝트 클론 및 설치
```sh
# GitHub에서 프로젝트 클론
git clone https://github.com/your-repo/G2BAvaloniaApp.git
cd G2BAvaloniaApp

# 필요한 패키지 복원
dotnet restore
```

### 3. ChromeDriver 설치
Chrome 버전에 맞는 ChromeDriver를 다운로드하고 실행 파일을 `G2BAvaloniaApp/bin/Debug/net9.0` 폴더에 배치하세요.

ChromeDriver 다운로드 링크: https://sites.google.com/chromium.org/driver/downloads

### 4. 빌드 및 실행
```sh
# 애플리케이션 빌드
dotnet build

# 애플리케이션 실행
dotnet run
```

---

## Navigation Guide

이 애플리케이션은 Avalonia UI를 기반으로 구성되어 있으며, 주요 UI 요소는 다음과 같습니다.

1. **검색어 입력 영역**
   - 검색어를 입력한 후 `Start` 버튼을 누르면 크롤링이 시작됩니다.
   - `Stop` 버튼을 누르면 현재 진행 중인 크롤링 작업이 중단됩니다.

2. **로그 창**
   - 크롤링 진행 상황이 실시간으로 표시됩니다.
   - 필터링 기능을 통해 특정 로그 수준(Info, Warn, Error)만 볼 수 있습니다.
   - 검색창을 이용하여 특정 로그를 검색할 수 있습니다.

3. **탭 메뉴**
   - `Log`: 크롤링 진행 상황을 확인할 수 있습니다.
   - `Details`: 현재 크롤링 상태 및 통계를 확인할 수 있습니다.

4. **진행 상태 표시 바**
   - 진행률이 프로그레스 바로 표시되며, 작업이 완료되면 사라집니다.

---

## Usage Examples

### 1. 기본 검색 실행
1. 프로그램 실행 후, 검색어 입력란에 키워드를 입력합니다.
2. `Start` 버튼을 클릭하면 크롤러가 실행되며 로그 창에 진행 사항이 표시됩니다.
3. 크롤링이 완료되면, `나라장터_검색결과_app.xlsx` 파일이 바탕화면에 저장됩니다.

### 2. 크롤링 중지
1. 크롤링 실행 중 `Stop` 버튼을 누르면 진행이 중단됩니다.
2. 중단된 상태에서도 기존 로그는 유지됩니다.

### 3. 로그 필터링
1. `Info`, `Warn`, `Error` 체크박스를 이용해 원하는 로그만 표시할 수 있습니다.
2. 검색창에 키워드를 입력하여 특정 메시지를 포함하는 로그를 찾을 수 있습니다.

---

## Configuration

### 1. 환경 설정
환경설정을 변경하려면 `WorkerOptions.cs` 파일을 수정하세요.

### 2. 크롤링 로직 개요

이 프로젝트의 핵심은 `Worker.cs` 파일에 구현된 크롤링 로직입니다. `Worker` 클래스는 검색어를 기반으로 나라장터 웹사이트를 자동화하여 데이터를 수집합니다.

#### 주요 기능
1. **Selenium WebDriver 초기화**
   - ChromeDriver를 실행하고 `disable-gpu` 등의 옵션을 설정하여 원활한 실행을 보장합니다.

2. **검색 실행 및 페이지 이동**
   - 검색어를 입력하고 검색 버튼을 클릭합니다.
   - 크롤링이 블록 단위로 수행되며, 각 블록 내의 모든 페이지를 순회합니다.

3. **데이터 스크래핑**
   - 검색 결과 테이블을 탐색하여 데이터를 추출합니다.
   - `SearchData` 객체에 데이터를 저장하고 리스트로 관리합니다.

4. **Excel 저장 기능**
   - EPPlus 라이브러리를 이용하여 검색된 데이터를 엑셀 파일로 저장합니다.
   - 기본 저장 경로는 바탕화면이며, 필요시 설정을 변경할 수 있습니다.

### 3. 크롤링 속도 조절
크롤링 속도는 `Thread.Sleep(1000);` 값을 조정하여 조절할 수 있습니다. 너무 짧게 설정하면 사이트에서 차단될 가능성이 있으므로 적절한 값으로 조정해야 합니다.

### 4. 에러 핸들링
크롤링 도중 발생하는 오류를 감지하고 로그를 기록합니다. 주요 예외 처리는 다음과 같습니다:
- `NoSuchElementException`: 특정 요소를 찾을 수 없을 때 발생
- `TimeoutException`: 페이지 로딩 대기 시간이 초과될 때 발생
- `WebDriverException`: Selenium 관련 일반적인 오류 발생

각 예외 상황에 대해 적절한 로그를 남기고, 필요하면 크롤링을 중단하거나 재시도할 수 있도록 구현되어 있습니다.

### 5. Excel 저장 경로 변경
Excel 저장 경로를 변경하려면 `Worker.cs` 파일에서 `filePath` 값을 수정하면 됩니다.

```csharp
string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
string filePath = Path.Combine(desktopPath, "나라장터_검색결과_app.xlsx");
```

원하는 디렉토리로 변경하면 크롤링 결과를 다른 위치에 저장할 수 있습니다.