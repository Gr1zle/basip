using FirebirdSql.Data.FirebirdClient;
using Microsoft.Extensions.Logging;
using RestSharp;
using Serilog.Core;
using System.Data;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Basip.DeviceLibrary;
using static Basip.WorkerOptions;

namespace Basip
{
    public class Worker : BackgroundService
    {
        public readonly ILogger logger;
        private WorkerOptions options;
        public TimeSpan timeout;
        public TimeSpan timestart;
        public TimeSpan deltasleep;

        private readonly string version;

        public Worker(ILogger<Worker> logger, WorkerOptions options)
        {
            this.logger = logger;
            this.options = options;

            version = GetApplicationVersion();

            var time = options.timeout.Split(':');
            timeout = new TimeSpan(Int32.Parse(time[0]), Int32.Parse(time[1]), Int32.Parse(time[2]));
            time = options.timeout.Split(':');
            timestart = new TimeSpan(Int32.Parse(time[0]), Int32.Parse(time[1]), Int32.Parse(time[2]));
            var now = new TimeSpan(DateTime.Now.TimeOfDay.Hours, DateTime.Now.TimeOfDay.Minutes, DateTime.Now.TimeOfDay.Seconds);
            deltasleep = (options.run_now) ? TimeSpan.Zero :
                (timestart >= now) ? timestart - now : timestart - now + new TimeSpan(1, 0, 0, 0);
        }

        // Получение версии приложения
        private string GetApplicationVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();


                // 2. AssemblyVersion (стандартный способ)
                var version = assembly.GetName().Version?.ToString();
                if (!string.IsNullOrEmpty(version) && version != "0.0.0.0")
                    return version;

                // 3. Из файла сборки
                if (!string.IsNullOrEmpty(assembly.Location))
                {
                    var fileInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
                    if (!string.IsNullOrEmpty(fileInfo.ProductVersion))
                        return fileInfo.ProductVersion;
                    if (!string.IsNullOrEmpty(fileInfo.FileVersion))
                        return fileInfo.FileVersion;
                }
            }
            catch (Exception ex)
            {
                logger.LogInformation($"Version detection: {ex.Message}");
            }

            return "2.0.0"; // Значение по умолчанию
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation(@$"32 basip start: {timestart} deltasleep: {deltasleep}");
            logger.LogInformation(@$"33 Service basip write and delete card started");
            await Task.Delay(deltasleep);

            // Выполняем инициализацию БД и получение устройств один раз при старте
            DB db = null;
            DataRowCollection devices = null;

            try
            {
                logger.LogInformation($"Версия приложения basip: {version}");

                // Создаем экземпляр DB с connection string
                db = new DB(options.db_config);

                // Проверяем наличие обязательных таблиц
                if (!db.CheckRequiredTables(logger))
                {
                    logger.LogCritical("Служебные таблицы не найдены. Программа завершает работу.");
                    Environment.Exit(1);
                }

                logger.LogInformation("Ok connect database");

                // Получаем устройства один раз при старте
                devices = db.GetDevice().Rows;
                logger.LogInformation("71 Зарегистрировано панелей bas-ip: " + devices.Count + " шт.");
            }
            catch (Exception e)
            {
                logger.LogError("No connect database: " + options.db_config);
                logger.LogError(e.ToString());
                Environment.Exit(1);
            }

            // Основной цикл, который будет выполняться периодически
            while (!stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation($@"Старт итерации");

                try
                {
                    // Запускаем run с уже инициализированными данными
                    run(devices, db);
                }
                catch (Exception ex)
                {
                    logger.LogError("Something crash restart everything");
                    logger.LogError(ex.ToString());
                    continue;
                }

                logger.LogInformation($@"timeout basip: {timeout}");
                await Task.Delay(timeout, stoppingToken);
            }

            logger.LogCritical(@$"49 basip stop");
        }

        private void run(DataRowCollection devices, DB db)
        {
            List<Task> tasks = new List<Task>();
            Stopwatch stopwatch = Stopwatch.StartNew();

            int validDevicesCount = 0;
            logger.LogInformation("70 Start async.");

            foreach (DataRow row in devices)
            {
                try
                {
                    if (row["IP"] == DBNull.Value || Convert.ToInt32(row["IP"]) == 0)
                    {
                        logger.LogDebug($"Skipping device ID {row["id_dev"]} - no IP address");
                        continue;
                    }

                    Device device = new Device(row, options.time_wait_http);
                    validDevicesCount++;
                    tasks.Add(TaskGet(row, db));
                }
                catch (Exception ex)
                {
                    logger.LogDebug($"Failed to create device from row ID {row["id_dev"]}: {ex.Message}");
                    continue;
                }
            }

            logger.LogInformation($"Processing {validDevicesCount} devices with valid IP addresses");

            if (tasks.Count > 0)
            {
                Task.WaitAll(tasks.ToArray());
            }
            else
            {
                logger.LogInformation("No devices with valid IP addresses found");
                return;
            }

            Task.WaitAll(tasks.ToArray());
        }

        // Изменяем TaskGet, чтобы он принимал DB
        private async Task TaskGet(DataRow row, DB dbForEvents)
        {
            int currentDeviceId = (int)row["id_dev"];
            Device dev = new Device(row, options.time_wait_http);

            JsonDocument deviceInfo = await dev.GetInfo();
            if (!await dev.Auth())
            {
                logger.LogInformation($"Device {dev.base_url} auth failed");
                return;
            }
            if (!dev.is_online)
            {
                logger.LogDebug($"Device {dev.base_url} offline");
                return;
            }

            // Используем переданный dbForEvents вместо создания нового
            try
            {
                string apiVersion = deviceInfo.RootElement.GetProperty("api_version").ToString();
                int majorVersion = int.Parse(apiVersion.Split('.')[0]);

                long lastTimestamp = dbForEvents.GetLastEventID(currentDeviceId);
                // Если нет сохраненного timestamp, берем время 1 час назад
                if (lastTimestamp == 0)
                {
                    lastTimestamp = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
                    logger.LogInformation($"No previous events found for device {currentDeviceId} IP: {dev.base_url}, using 1 hour ago: {DateTimeOffset.FromUnixTimeMilliseconds(lastTimestamp).DateTime:yyyy-MM-dd HH:mm:ss}");
                }
                else
                {
                    logger.LogTrace($"Last event timestamp for device {currentDeviceId} IP: {dev.base_url}: {DateTimeOffset.FromUnixTimeMilliseconds(lastTimestamp).DateTime:yyyy-MM-dd HH:mm:ss}");
                }

                // Получаем события НАЧИНАЯ С последнего timestamp (включительно)
                var logsResponse = await dev.GetEvents(lastTimestamp, 50);

                if (logsResponse.StatusCode == HttpStatusCode.OK && !string.IsNullOrEmpty(logsResponse.Content))
                {
                    var logsData = JsonSerializer.Deserialize<Basip.LogsResponse>(logsResponse.Content);

                    if (logsData?.list_items != null && logsData.list_items.Count > 0)
                    {
                        logger.LogTrace($"Retrieved {logsData.list_items.Count} events from device {dev.base_url}");

                        long maxTimestamp = lastTimestamp;
                        int processedEventsCount = 0;

                        // Обрабатываем все события НАЧИНАЯ С последнего timestamp
                        foreach (var logItem in logsData.list_items)
                        {
                            // Берем события с timestamp >= lastTimestamp
                            if (logItem.timestamp >= lastTimestamp)
                            {
                                if (logItem.timestamp > maxTimestamp)
                                {
                                    maxTimestamp = logItem.timestamp;
                                }

                                await ProcessLogEvent(dbForEvents, dev, logItem, currentDeviceId, deviceInfo);
                                processedEventsCount++;
                            }
                            else
                            {
                                logger.LogDebug($"Skipping old event with timestamp: {logItem.timestamp} (last: {lastTimestamp})");
                            }
                        }
                        logger.LogTrace($"currentDeviceId = {currentDeviceId} maxTimestamp= {maxTimestamp}");
                        if (processedEventsCount > 0)
                        {
                            dbForEvents.SetLastEventID(currentDeviceId, maxTimestamp);
                            logger.LogDebug($"Processed {processedEventsCount} events, updated timestamp to: {DateTimeOffset.FromUnixTimeMilliseconds(maxTimestamp).DateTime:yyyy-MM-dd HH:mm:ss}");

                            if (options.clear_log)
                            {
                                var statusLog = await dev.ClearingLog(majorVersion);
                                switch (statusLog.StatusCode)
                                {
                                    case HttpStatusCode.OK:
                                        logger.LogInformation($"Successful attempt to clear the event log IP: {dev.base_url}, device {currentDeviceId}, major api = {majorVersion}");
                                        break;
                                    default:
                                        logger.LogWarning($"Failed attempt to clear the event log IP: {dev.base_url}, device {currentDeviceId}, major api = {majorVersion}");
                                        break;
                                }
                            }
                            else
                            {
                                logger.LogWarning($"The event log has not been cleared IP: {dev.base_url}, device {currentDeviceId}, major api = {majorVersion}");
                            }
                        }
                        else
                        {
                            logger.LogInformation($"No new events to process IP: {dev.base_url}, device {currentDeviceId}, major api = {majorVersion}");
                        }
                    }
                    else
                    {
                        logger.LogDebug($"No events found in response for device {currentDeviceId} ");
                    }
                }
                else
                {
                    logger.LogWarning($"Failed to get events. Status: {logsResponse.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error processing events for device {currentDeviceId} IP: {dev.base_url}: {ex.Message}");
            }

            // ОБРАБОТКА КАРТ: создаем новый экземпляр DB для работы с картами
            DB dbForCards = new DB(options.db_config);

            try
            {
                DataRowCollection cardList = dbForCards.GetCardForLoad(currentDeviceId).Rows;
                logger.LogInformation($"Панель ID: {currentDeviceId}, IP: {dev.ip} - Card count: {cardList.Count}");

                foreach (DataRow card in cardList)
                {
                    switch ((int)card["operation"])
                    {
                        case 1: // Запись карты
                            await ProcessCardWrite(dbForCards, dev, card, currentDeviceId);
                            break;

                        case 2: // Удаление карты
                            await ProcessCardDelete(dbForCards, dev, card, currentDeviceId, deviceInfo);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error processing cards for device {currentDeviceId}: {ex.Message}");
            }
        }

        private async Task ProcessCardWrite(DB db, Device dev, DataRow card, int deviceId)
        {
            string cardId = card["id_card"].ToString();
            logger.LogInformation($@"369 Command destination: writekey id_dev={deviceId} BASE_URL {dev.base_url} key=""{options.uidtransform(cardId)}"" AddCard ");

            RestResponse request = await dev.AddIdentifier(options.uidtransform(cardId), "card");
            bool shouldDeleteFromQueue = false;
            int targetDeviceId = (int)card["id_dev"];

            switch (request.StatusCode)
            {
                case HttpStatusCode.OK:
                    await ProcessCardWriteSuccess(db, dev, card, cardId, targetDeviceId, request);
                    shouldDeleteFromQueue = true;
                    break;

                case HttpStatusCode.BadRequest:
                    await ProcessCardAlreadyExists(db, dev, card, cardId, targetDeviceId);
                    shouldDeleteFromQueue = true;
                    break;

                default:
                    logger.LogInformation($@"Answer destination: writekey id_dev={targetDeviceId} BASE_URL {dev.base_url} Answer: {request.StatusCode} key=""{options.uidtransform(cardId)}""");
                    db.UpdateCardInDevIncrement((int)card["id_cardindev"]);
                    break;
            }

            // Удаляем из очереди если операция успешна или карта уже существует
            if (shouldDeleteFromQueue)
            {
                db.DeleteCardInDev((int)card["id_cardindev"]);
                logger.LogDebug($"Card {cardId} successfully processed and removed from queue for device {targetDeviceId}");
            }
        }

        private async Task ProcessCardWriteSuccess(DB db, Device dev, DataRow card, string cardId, int deviceId, RestResponse request)
        {
            try
            {
                var uid = JsonDocument.Parse(request.Content).RootElement.GetProperty("uid").ToString();
                logger.LogInformation($@"Answer destination: writekey id_dev={deviceId} BASE_URL {dev.base_url} Answer: OK key=""{options.uidtransform(cardId)}"" uid={uid}");

                int uidInt = 0;
                if (!int.TryParse(uid, out uidInt))
                {
                    logger.LogWarning($"Cannot parse UID '{uid}' as integer, using 0");
                    uidInt = 0;
                }

                int rowsUpdated = db.FixCardIdxOK(cardId, deviceId, uidInt);

                if (rowsUpdated > 0)
                {
                    logger.LogInformation($"Successfully updated {rowsUpdated} rows in CARDIDX");
                }
                else
                {
                    logger.LogWarning($"No rows updated in CARDIDX for card {cardId}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error updating CARDIDX: {ex.Message}");
                // Фолбэк: пытаемся записать с uid=0
                try
                {
                    db.FixCardIdxOK(cardId, deviceId, 0);
                }
                catch { }
            }
        }

        private async Task ProcessCardAlreadyExists(DB db, Device dev, DataRow card, string cardId, int deviceId)
        {
            logger.LogInformation($@"Answer destination: writekey id_dev={deviceId} BASE_URL {dev.base_url} Answer: BAD REQUEST key=""{options.uidtransform(cardId)}"" card already exists");

            try
            {
                var cardInfoResponse = await dev.GetInfoCard(options.uidtransform(cardId), 2);
                int existingUid = 0;

                logger.LogInformation($"GetInfoCard response status: {cardInfoResponse.StatusCode}");

                if (cardInfoResponse.StatusCode == HttpStatusCode.OK && !string.IsNullOrEmpty(cardInfoResponse.Content))
                {
                    try
                    {
                        var cardInfo = JsonDocument.Parse(cardInfoResponse.Content);

                        if (cardInfo.RootElement.TryGetProperty("list_items", out var listItems) && listItems.GetArrayLength() > 0)
                        {
                            var firstItem = listItems[0];

                            if (firstItem.TryGetProperty("identifier_uid", out var uidProperty))
                            {
                                var uidStr = uidProperty.ToString();

                                if (int.TryParse(uidStr, out existingUid))
                                {
                                    logger.LogInformation($"CARD ALREADY EXISTS - Device: {dev.base_url}, id_dev={deviceId}, Card Number: {cardId}, Existing UID: {existingUid}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning($"Could not parse UID for existing card {cardId}: {ex.Message}");
                    }
                }

                int rowsUpdated = db.FixCardIdxOK(cardId, deviceId, existingUid);

                if (rowsUpdated > 0)
                {
                    logger.LogDebug($"Successfully updated CARDIDX for existing card UID: {existingUid}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error processing existing card: {ex.Message}");
            }
        }

        private async Task ProcessCardDelete(DB db, Device dev, DataRow card, int deviceId, JsonDocument deviceInfo)
        {
            string cardId = card["id_card"].ToString();
            string delcommandlog = $@"deletekey id_dev ={deviceId} BASE_URL {dev.base_url} key = ""{options.uidtransform(cardId)}""";

            string apiVersion = deviceInfo.RootElement.GetProperty("api_version").ToString();
            int majorVersion = int.Parse(apiVersion.Split('.')[0]);

            logger.LogInformation(delcommandlog + "GetInfoCard " + "api_version=\"" + apiVersion + "\"");

            RestResponse? content = await dev.GetInfoCard(options.uidtransform(cardId), majorVersion);

            switch (content.StatusCode)
            {
                case HttpStatusCode.OK:
                    JsonDocument jsonDoc = JsonDocument.Parse(content.Content);
                    JsonElement.ArrayEnumerator jsonlist = jsonDoc.RootElement.GetProperty("list_items").EnumerateArray();

                    /*logger.LogDebug($"{delcommandlog} Получен ответ от устройства c мажорной версией api = {majorVersion}: {content.Content}");*/

                    // Ищем точное совпадение номера карты
                    string uidToDelete = null;
                    foreach (JsonElement element in jsonlist)
                    {
                        if (element.GetProperty("identifier_number").ToString() == options.uidtransform(cardId))
                        {
                            uidToDelete = element.GetProperty("identifier_uid").ToString();
                            break;
                        }
                    }

                    if (uidToDelete != null)
                    {
                        logger.LogInformation($"508 {delcommandlog}" + $@" для карты {cardId} uid ={uidToDelete} DeleteCard major api = {majorVersion}");

                        var status = (await dev.DeleteCard(uidToDelete)).StatusCode;

                        switch (status)
                        {
                            case HttpStatusCode.OK:
                                logger.LogDebug($@"{delcommandlog}   Answer: OK uid={uidToDelete}");
                                db.DeleteCardInDev((int)card["id_cardindev"]);
                                logger.LogInformation($"Card {cardId} successfully processed and removed from queue for device {deviceId}");
                                break;
                            default:
                                logger.LogDebug($@"Answer destination: {delcommandlog} Answer: ERR uid={uidToDelete} no delete");
                                db.UpdateCardInDevIncrement((int)card["id_cardindev"]);
                                break;
                        }
                    }
                    else
                    {
                        logger.LogDebug($@"{delcommandlog} Answer: OK no card in panel");
                        db.DeleteCardInDev((int)card["id_cardindev"]);
                        logger.LogInformation($"Card {cardId} successfully processed and removed from queue for device {deviceId}");
                    }
                    break;

                default:
                    logger.LogError($@"{delcommandlog} Answer: ERR faild GetInfoCard (не удалось получить информацию о карте)");
                    db.UpdateCardInDevIncrement((int)card["id_cardindev"]);
                    break;
            }
        }


        // Добавь это поле в класс (например, Dictionary для маппинга событий)
        private static Dictionary<string, int> _eventTypeMapping;

        // Метод для инициализации маппинга из таблицы eventtype
        private void InitializeEventTypeMapping(DB db)
        {
            if (_eventTypeMapping != null) return;

            _eventTypeMapping = new Dictionary<string, int>();

            // Здесь нужно загрузить данные из таблицы eventtype
            // Предполагаю, что у тебя есть метод для получения всех eventtype из БД
            var eventTypes = db.GetAllEventTypes(); // Тебе нужно реализовать этот метод

            foreach (var eventType in eventTypes)
            {
                // Сопоставляем NAME из таблицы eventtype с ключами событий
                // Нужно преобразовать NAME в формат, который используется в logEvent.name.key
                string key = ConvertNameToKey(eventType.Name);
                if (!string.IsNullOrEmpty(key))
                {
                    _eventTypeMapping[key] = eventType.Id;
                }
            }
        }

        // Вспомогательный метод для преобразования NAME в ключ события
        private string ConvertNameToKey(string eventName)
        {
            return eventName?.ToLower()
                .Replace(" ", "_")
                .Replace("ё", "е")
                .Replace("неизвестная_карточка", "access_denied_by_unknown_card")
                .Replace("действительная_карточка", "access_granted_by_valid_identifier")
                // Добавь другие преобразования по необходимости
                ?? string.Empty;
        }

        private async Task ProcessLogEvent(DB db, Device dev, LogItem logEvent, int deviceId, JsonDocument deviceInfo)
        {

            try
            {
                string apiVersion = deviceInfo.RootElement.GetProperty("api_version").ToString();
                int majorVersion = int.Parse(apiVersion.Split('.')[0]);

                string cardNumber = null;
                if (logEvent.info?.model != null)


                {
                    // Пробуем разные ключи, которые могут содержать номер карты
                    if (logEvent.info.model.ContainsKey("card"))
                    {
                        cardNumber = logEvent.info.model["card"]?.ToString();
                    }
                    else if (logEvent.info.model.ContainsKey("number"))
                    {
                        cardNumber = logEvent.info.model["number"]?.ToString();
                    }

                }

                // ПРАВИЛЬНЫЕ КОДЫ СОБЫТИЙ
                int eventCode = logEvent.name?.key switch
                {
                    "access_denied_by_unknown_card" => 46,
                    "access_granted_by_valid_identifier" => 50,
                    _ => 0 // для остальных событий
                };

                //// Тип события для информации

                string eventType = logEvent.name?.key ?? "Unknown";
                DateTime dateTime = DateTimeOffset.FromUnixTimeMilliseconds(logEvent.timestamp).DateTime;
                string note = $"Device=\"{dev.name}\", Type={eventType} Readdate=#{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}# DeviceDate =#{dateTime}# DeviceDate = {logEvent.timestamp}";

                if (eventCode > 0)
                {
                    int? insertedEventTypeId = db.EventInsert(
                        id_db: 1,
                        id_eventtype: eventCode,
                        id_cntrl: dev.ctrl,
                        id_reader: 0,
                        note: cardNumber,
                        time: logEvent.EventTime,
                        id_video: null,
                        id_user: null,
                        ess1: null,
                        ess2: null,
                        idsource: 1,
                        idserverts: logEvent.timestamp
                    );

                    if (eventType != "successful_login_api_call")
                    {
                        if (majorVersion == 1 || majorVersion == 2)
                        {
                            if (cardNumber != null)
                            {
                                logger.LogInformation($"+{note}, Card={cardNumber}, EventType={eventCode}, Id_Event={insertedEventTypeId.Value},  major api = {majorVersion}");
                            }
                            else
                            {
                                logger.LogInformation($"+{note}, EventType={eventCode}, Id_Event={insertedEventTypeId.Value}, major api = {majorVersion}");
                            }
                        }
                        else
                        {
                            logger.LogCritical($"Not implemented with major api = {majorVersion}");
                        }
                    }

                    if (insertedEventTypeId.HasValue)
                    {
                        logger.LogInformation($"Event saved: Dev={deviceId}, Type={eventCode}, EventType={eventCode}, Id_Event={insertedEventTypeId.Value}, major api = {majorVersion}");
                    }
                    else
                    {
                        logger.LogError($"Failed to save event: Dev={deviceId}, EventType={eventCode}, major api = {majorVersion} ");
                    }
                }
            }

            catch (Exception ex)
            {
                logger.LogError($"Error: {ex.Message}");
            }
        }
    }
}