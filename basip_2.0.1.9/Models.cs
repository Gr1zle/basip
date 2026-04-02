using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Basip  // Используем ваш текущий namespace, не DeviceLibrary
{
    public class LogsResponse
    {
        [JsonPropertyName("list_items")]
        public List<LogItem> list_items { get; set; }
    }

    public class LogItem
    {
        [JsonPropertyName("timestamp")]
        public long timestamp { get; set; }

        [JsonPropertyName("name")]
        public LogName name { get; set; }

        [JsonPropertyName("info")]
        public LogInfo info { get; set; }

        // Удобное свойство для получения DateTime из timestamp
        public DateTime EventTime => DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime;
    }

    public class LogName
    {
        [JsonPropertyName("key")]
        public string key { get; set; }
    }

    public class LogInfo
    {
        [JsonPropertyName("model")]
        public Dictionary<string, object> model { get; set; }
    }

    // Добавим также модель для ответа при авторизации, если её нет
    public class AuthResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; set; }
    }

    // Модель для информации об устройстве
    public class DeviceInfoResponse
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("firmware_version")]
        public string FirmwareVersion { get; set; }

        [JsonPropertyName("hardware_version")]
        public string HardwareVersion { get; set; }

        [JsonPropertyName("mac_address")]
        public string MacAddress { get; set; }
    }
}