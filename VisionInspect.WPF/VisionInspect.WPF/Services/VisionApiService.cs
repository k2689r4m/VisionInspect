
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using VisionInspect.WPF.Models;

namespace VisionInspect.WPF.Services;

public sealed class VisionApiService
{
    private static readonly HttpClient Client = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:8000/"),
        Timeout = TimeSpan.FromSeconds(120)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            using var response =
                await Client.GetAsync("api/health");

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<InspectionResult> InspectAsync(
        string imagePath,
        string model = "patchcore")
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException(
                "검사 이미지가 없습니다.",
                imagePath);

        using var form = new MultipartFormDataContent();

        await using var stream = File.OpenRead(imagePath);

        using var fileContent = new StreamContent(stream);

        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue("image/png");

        form.Add(
            fileContent,
            "file",
            Path.GetFileName(imagePath));

        string url =
            $"api/inspect?model={Uri.EscapeDataString(model)}";

        using var response =
            await Client.PostAsync(url, form);

        string json =
            await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"AI 검사 실패 ({model}): {json}");

        return JsonSerializer.Deserialize<InspectionResult>(
            json,
            JsonOptions)
            ?? throw new InvalidOperationException(
                "검사 결과를 변환할 수 없습니다.");
    }

    public async Task<byte[]> DownloadImageAsync(
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException(
                "결과 이미지 경로가 없습니다.");

        return await Client.GetByteArrayAsync(
            relativePath.TrimStart('/'));
    }
}
