using System.Text;
using System.Text.Json;

public class Device
{
    private readonly HttpClient _httpClient;
    public string Ip { get; }
    public int IdDev { get; }

    public Device(string ip, int idDev, int timeoutSeconds = 25)
    {
        Ip = ip;
        IdDev = idDev;

        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            BaseAddress = new Uri($"http://{ip}")
        };

        // Важно: закрываем соединение после каждого запроса
        _httpClient.DefaultRequestHeaders.ConnectionClose = true;
    }

    public async Task<string> AddCardAsync(int uid, int tz0 = 50, int tz1 = 30, int ch = 0)
    {
        try
        {
            var url = $"/api/v0/key/{uid}";
            var payload = new { UID = uid, TZ0 = tz0, TZ1 = tz1, ch = ch };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                return $"{{ \"answer\": \"ERR: HTTP {(int)response.StatusCode}\" }}";
            }

            return await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException ex)
        {
            return $"{{ \"answer\": \"ERR: Network - {ex.Message}\" }}";
        }
        catch (TaskCanceledException)
        {
            return "{ \"answer\": \"ERR: Timeout\" }";
        }
        catch (Exception ex)
        {
            return $"{{ \"answer\": \"ERR: {ex.Message}\" }}";
        }
    }

public async Task<string> GetCardAsync(int uid)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/v0/key/{uid}");
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            return $"{{ \"answer\": \"ERR: {ex.Message}\" }}";
        }
    }

    public async Task<string> DeleteCardAsync(int uid, int ch = 0)
    {
        try
        {
            var url = $"/api/v0/key/{uid}";
            var payload = new { UID = uid, ch = ch };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Delete, url) { Content = content };
            var response = await _httpClient.SendAsync(request);

            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            return $"{{ \"answer\": \"ERR: сеть - {ex.Message}\" }}";
        }
    }
}