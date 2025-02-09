// Program.cs

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

class Program
{
    static void Main(string[] args)
    {
        // 간단한 배너/안내
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=======================================");
        Console.WriteLine("   [나라장터 RPA 크롤러] 콘솔 버전");
        Console.WriteLine("=======================================");
        Console.ResetColor();

        // 사용자에게 검색어 입력받기
        Console.Write("검색어를 입력해주세요 (기본값: 'RPA'): ");
        string keyword = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            keyword = "RPA";
        }
        Console.WriteLine($"입력된 검색어: {keyword}");
        Console.WriteLine();

        // Host 생성
        Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                // 검색어를 DI로 넘기기 위해, 설정(Options) 클래스를 Singleton 등록
                services.AddSingleton(new WorkerOptions { Keyword = keyword });

                // Worker 등록
                services.AddHostedService<Worker>();
            })
            .Build()
            .Run();
    }
}
