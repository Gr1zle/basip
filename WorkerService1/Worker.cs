using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Diagnostics;
using System.Net;

namespace CustomController;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly WorkerOptions _options;
    private readonly TimeSpan _interval;

    public Worker(ILogger<Worker> logger, WorkerOptions options)
    {
        _logger = logger;
        _options = options;

        var parts = options.Timeout.Split(':');
        _interval = new TimeSpan(
            int.Parse(parts[0]),
            int.Parse(parts[1]),
            int.Parse(parts[2])
        );

        _logger.LogInformation($"Сервис запущен. Интервал: {_interval} | RunNow: {_options.RunNow}");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("=== Сервис автоматической записи карт в контроллер запущен ===");

        if (!_options.RunNow)
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        
        while (!stoppingToken.IsCancellationRequested)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await ProcessAllDevicesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Критическая ошибка при обработке устройств");
            }

            _logger.LogInformation($"Цикл обработки завершён за {stopwatch.Elapsed.TotalSeconds:F2} сек. Следующий запуск через {_interval}");

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("Сервис остановлен");
    }
    
    private async Task ProcessAllDevicesAsync()
    {
        using var db = new DB(_options.db_config);

        if (!db.CheckRequiredTables(_logger))
        {
            _logger.LogCritical("Не удалось проверить таблицы БД. Пропуск цикла.");
            return;
        }

        DataTable devices = db.GetDevice();

        _logger.LogInformation($"Найдено устройств в базе: {devices.Rows.Count}");

        foreach (DataRow row in devices.Rows)
        {
            try
            {
                int idDev = Convert.ToInt32(row["id_dev"]);
                int ipInt = Convert.ToInt32(row["IP"]);

                byte[] bytes = BitConverter.GetBytes(ipInt);
                Array.Reverse(bytes);
                string ipAddress = new IPAddress(bytes).ToString();

                _logger.LogDebug($"Обработка устройства ID={idDev}, IP={ipAddress}");

                var device = new Device(ipAddress, idDev, _options.TimeWaitHttp);

                await ProcessCardsForDeviceAsync(db, device, idDev);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при обработке устройства ID {row["id_dev"]}");
            }
        }
    }

    private async Task ProcessCardsForDeviceAsync(DB db, Device device, int idDev)
    {
        DataTable cards = db.GetCardForLoad(idDev);

        if (cards.Rows.Count == 0)
        {
            //_logger.LogDebug($"Устройство {idDev} ({device.Ip}) — очередь карт пуста");
            return;
        }

        _logger.LogInformation($"Устройство {idDev} ({device.Ip}) — карт в очереди: {cards.Rows.Count}");

        foreach (DataRow card in cards.Rows)
        {
            int operation = Convert.ToInt32(card["operation"]);
            string cardIdStr = card["id_card"].ToString();
            int idCardInDev = Convert.ToInt32(card["id_cardindev"]);

            if (!int.TryParse(cardIdStr, out int uid))
            {
                _logger.LogWarning($"Некорректный UID карты: {cardIdStr} (id_cardindev={idCardInDev})");
                db.UpdateCardInDevIncrement(idCardInDev);
                continue;
            }

            if (operation == 1) // Добавление карты
            {
                await ProcessCardWriteAsync(db, device, uid, idCardInDev, card);
            }
            else if (operation == 2) // Удаление карты
            {
                await ProcessCardDeleteAsync(db, device, uid, idCardInDev);
            }
            else
            {
                _logger.LogWarning($"Неизвестная операция {operation} для карты {uid}");
                db.UpdateCardInDevIncrement(idCardInDev);
            }
        }
    }

    private async Task ProcessCardWriteAsync(DB db, Device device, int uid, int idCardInDev, DataRow cardRow)
    {
        _logger.LogInformation($"[{device.Ip}] → Запись карты UID = {uid}");

        const int maxAttempts = 4;
        int delayMs = 1200;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            string response = await device.AddCardAsync(uid);

            if (IsSuccessResponse(response))
            {
                _logger.LogInformation($"[{device.Ip}] ✓ Карта {uid} успешно записана");
                db.DeleteCardInDev(idCardInDev);
                db.FixCardIdxOK(cardRow["id_card"].ToString(), device.IdDev, uid);
                await Task.Delay(600);   // небольшая пауза после успеха
                return;
            }

            // Логируем подробнее
            _logger.LogWarning($"[{device.Ip}] ✗ Попытка {attempt}/{maxAttempts} не удалась. UID={uid} Ответ: {response}");

            if (attempt < maxAttempts)
            {
                await Task.Delay(delayMs);
                delayMs = delayMs * 2; // экспоненциальная задержка
            }
        }

        // Если все попытки провалились
        _logger.LogError($"[{device.Ip}] Не удалось записать карту {uid} после {maxAttempts} попыток");
        db.UpdateCardInDevIncrement(idCardInDev);

        // Большая пауза после нескольких ошибок подряд
        await Task.Delay(3000);
    }

    private async Task ProcessCardDeleteAsync(DB db, Device device, int uid, int idCardInDev)
    {
        _logger.LogInformation($"[{device.Ip}] → Удаление карты UID = {uid}");

        string response = await device.DeleteCardAsync(uid);

        if (IsSuccessResponse(response) || response.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation($"[{device.Ip}] ✓ Карта {uid} успешно удалена (или отсутствовала)");
            db.DeleteCardInDev(idCardInDev);
        }
        else
        {
            _logger.LogWarning($"[{device.Ip}] ✗ Ошибка удаления карты {uid}. Ответ: {response}");
            db.UpdateCardInDevIncrement(idCardInDev);
        }
    }

    private bool IsSuccessResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return false;
        return response.Contains("OK", StringComparison.OrdinalIgnoreCase) ||
               response.Contains("success", StringComparison.OrdinalIgnoreCase) ||
               response.Contains("\"answer\":\"OK\"", StringComparison.OrdinalIgnoreCase);
    }
}