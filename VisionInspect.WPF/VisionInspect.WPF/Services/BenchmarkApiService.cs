
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using VisionInspect.WPF.Models;

namespace VisionInspect.WPF.Services;

public sealed class BenchmarkApiService
{
    // 기존 VisionApiService의 주소와 동일하게 설정
    private static readonly HttpClient Client = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:8000/"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<BenchmarkReport> GetLatestAsync()
    {
        using var response = await Client.GetAsync(
            "api/benchmark/latest"
        );

        response.EnsureSuccessStatusCode();

        var report = await response.Content
            .ReadFromJsonAsync<BenchmarkReport>();

        return report ?? throw new InvalidOperationException(
            "Benchmark API returned an empty response."
        );
    }
}
