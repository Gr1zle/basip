using FirebirdSql.Data.FirebirdClient;
using Microsoft.Extensions.Logging;
using RestSharp;
using Serilog.Core;
using System.Data;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;
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
        private string GetApplicationVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
                if (!string.IsNullOrEmpty(fileVersionInfo.FileVersion))
                {
                    return fileVersionInfo.FileVersion;
                }
                return "Unknown";
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to get application version: {ex.Message}");
                return "Unknown";
            }
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // System.Reflection.Assembly executingAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            //var fieVersionInfo = FileVersionInfo.GetVersionInfo(executingAssembly.Location);
            //var version = fieVersionInfo.FileVersion;


            //logger.LogTrace(@$"32 basip start: {timestart} deltasleep: {deltasleep} fieVersionInfo = {fieVersionInfo} version = {version}");

            logger.LogTrace(@$"32 basip start: {timestart} deltasleep: {deltasleep}");
            logger.LogTrace(@$"33 Service basip write and delete card started");
            await Task.Delay(deltasleep);
            while (!stoppingToken.IsCancellationRequested)
            {
                logger.LogTrace($@"Старт итерации");
                try
                {
                    logger.LogDebug($"Версия приложения basip: {version}");
                    run();//запуск модуля, который запустит асинхронные процессы.
                }
                catch (Exception ex)
                {
                    logger.LogError("Something crash restart everything");
                    logger.LogError(ex.ToString());
                    continue;
                }
                logger.LogTrace($@"timeout basip: {timeout}");
                await Task.Delay(timeout, stoppingToken);// пауза на указанное в настройках время.
            }
            logger.LogTrace(@$"49 basip stop");
        }
        private void run()
        {
            // Создаем экземпляр DB с connection string
            DB db = new DB(options.db_config);

            try
            {
                // Проверяем наличие обязательных таблиц
                if (!db.CheckRequiredTables())
                {
                    logger.LogCritical("Required tables are missing in the database. The program will be terminated.");
                    Environment.Exit(1);
                }
            }
            catch (Exception e)
            {
                logger.LogError("No connect database: " + options.db_config);
                logger.LogError(e.ToString());
                return;
            }
            logger.LogTrace("Ok connect database");

            List<Task> tasks = new List<Task>();
            Stopwatch stopwatch = Stopwatch.StartNew();

            // Получаем устройства - соединение управляется внутри DB
            DataRowCollection data = db.GetDevice().Rows;
            logger.LogDebug("71 Зарегистрировано панелей bas-ip: " + data.Count + " шт.");

            int validDevicesCount = 0;
            logger.LogTrace("70 Start async.");
            foreach (DataRow row in data)
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
                    tasks.Add(TaskGet(row));
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

        /* 12.03.2025 
         * Освной процесс работы с панелью
         * 
         * 
         */
        private async Task TaskGet(DataRow row)
        {
            int currentDeviceId = (int)row["id_dev"];
            Device dev = new Device(row, options.time_wait_http);


            JsonDocument deviceInfo = await dev.GetInfo();
            if (!dev.is_online || !await dev.Auth())
            {
                logger.LogDebug($"Device {dev.base_url} offline or auth failed");
                return;
            }

            DB dbForEvents = new DB(options.db_config);

            try
            {

                long lastTimestamp = dbForEvents.GetLastEventID(currentDeviceId);
                // Если нет сохраненного timestamp, берем время 1 час назад
                if (lastTimestamp == 0)
                {
                    lastTimestamp = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
                    logger.LogInformation($"No previous events found for device {currentDeviceId}, using 1 hour ago: {DateTimeOffset.FromUnixTimeMilliseconds(lastTimestamp).DateTime:yyyy-MM-dd HH:mm:ss}");
                }
                else
                {
                    logger.LogInformation($"Last event timestamp for device {currentDeviceId}: {DateTimeOffset.FromUnixTimeMilliseconds(lastTimestamp).DateTime:yyyy-MM-dd HH:mm:ss}");
                }

                // Получаем события НАЧИНАЯ С последнего timestamp (включительно)

                var logsResponse = await dev.GetEvents(lastTimestamp, 50);

                if (logsResponse.StatusCode == HttpStatusCode.OK && !string.IsNullOrEmpty(logsResponse.Content))
                {
                    var logsData = JsonSerializer.Deserialize<LogsResponse>(logsResponse.Content);

                    if (logsData?.list_items != null && logsData.list_items.Count > 0)
                    {
                        logger.LogInformation($"Retrieved {logsData.list_items.Count} events from device {dev.base_url}");

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

                                await ProcessLogEvent(dbForEvents, dev, logItem, currentDeviceId);
                                processedEventsCount++;
                            }
                            else
                            {
                                logger.LogDebug($"Skipping old event with timestamp: {logItem.timestamp} (last: {lastTimestamp})");
                            }
                        }
                        logger.LogInformation($"currentDeviceId = {currentDeviceId} maxTimestamp= {maxTimestamp}");
                        if (processedEventsCount > 0)
                        {
                            dbForEvents.SetLastEventID(currentDeviceId, maxTimestamp);
                            logger.LogInformation($"Processed {processedEventsCount} events, updated timestamp to: {DateTimeOffset.FromUnixTimeMilliseconds(maxTimestamp).DateTime:yyyy-MM-dd HH:mm:ss}");
                            if (options.clear_log)
                            {
                                var statusLog = await dev.ClearingLog();
                                switch (statusLog.StatusCode)
                                {
                                    case HttpStatusCode.OK:
                                        logger.LogDebug("Successful attempt to clear the event log");
                                        break;
                                    default:
                                        logger.LogDebug("Failed attempt to clear the event log");
                                        break;
                                }
                            }
                            else
                            {
                                logger.LogDebug("The event log has not been cleared");
                            }

                        }
                        else
                        {
                            logger.LogDebug($"No new events to process");
                        }
                    }
                    else
                    {
                        logger.LogDebug($"No events found in response for device {currentDeviceId}");

                    }
                }
                else
                {
                    logger.LogWarning($"Failed to get events. Status: {logsResponse.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error processing events for device {currentDeviceId}: {ex.Message}");
            }

            // ОБРАБОТКА КАРТ: создаем новый экземпляр DB для работы с картами
            DB dbForCards = new DB(options.db_config);

            try
            {
                DataRowCollection cardList = dbForCards.GetCardForLoad(currentDeviceId).Rows;
                logger.LogDebug($"Панель ID: {currentDeviceId}, IP: {dev.ip} - Card count: {cardList.Count}");

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
            logger.LogDebug($@"Command destination: writekey id_dev={deviceId} BASE_URL {dev.base_url} key=""{options.uidtransform(cardId)}"" AddCard ");

            RestResponse request = await dev.AddCard(options.uidtransform(cardId));
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
                    logger.LogWarning($@"Answer destination: writekey id_dev={targetDeviceId} BASE_URL {dev.base_url} Answer: {request.StatusCode} key=""{options.uidtransform(cardId)}""");
                    db.UpdateCardInDevIncrement((int)card["id_cardindev"]);
                    break;
            }

            // Удаляем из очереди если операция успешна или карта уже существует
            if (shouldDeleteFromQueue)
            {
                db.DeleteCardInDev((int)card["id_cardindev"]);
                logger.LogInformation($"Card {cardId} successfully processed and removed from queue for device {targetDeviceId}");
            }
        }

        private async Task ProcessCardWriteSuccess(DB db, Device dev, DataRow card, string cardId, int deviceId, RestResponse request)
        {
            try
            {
                var uid = JsonDocument.Parse(request.Content).RootElement.GetProperty("uid").ToString();
                logger.LogDebug($@"Answer destination: writekey id_dev={deviceId} BASE_URL {dev.base_url} Answer: OK key=""{options.uidtransform(cardId)}"" uid={uid}");

                int uidInt = 0;
                if (!int.TryParse(uid, out uidInt))
                {
                    logger.LogWarning($"Cannot parse UID '{uid}' as integer, using 0");
                    uidInt = 0;
                }

                int rowsUpdated = db.FixCardIdxOK(cardId, deviceId, uidInt);

                if (rowsUpdated > 0)
                {
                    logger.LogDebug($"Successfully updated {rowsUpdated} rows in CARDIDX");
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
            logger.LogDebug($@"Answer destination: writekey id_dev={deviceId} BASE_URL {dev.base_url} Answer: BAD REQUEST key=""{options.uidtransform(cardId)}"" card already exists");

            try
            {
                var cardInfoResponse = await dev.GetInfoCard(options.uidtransform(cardId), 2);
                int existingUid = 0;

                logger.LogDebug($"GetInfoCard response status: {cardInfoResponse.StatusCode}");

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
                                    logger.LogInformation($"CARD ALREADY EXISTS - Device: {dev.base_url}, Card Number: {cardId}, Existing UID: {existingUid}");
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

            logger.LogDebug(delcommandlog + "GetInfoCard " + "api_version=\"" + deviceInfo.RootElement.GetProperty("api_version").ToString() + "\"");

            // Получаем информацию о карте для удаления
            RestResponse? content = await dev.GetInfoCard(options.uidtransform(cardId),
                int.Parse(deviceInfo.RootElement.GetProperty("api_version").ToString().Split('.')[0]));

            switch (content.StatusCode)
            {
                case HttpStatusCode.OK:
                    JsonElement.ArrayEnumerator jsonlist = JsonDocument.Parse(content.Content).RootElement.GetProperty("list_items").EnumerateArray();

                    foreach (JsonElement element in jsonlist)
                    {
                        string uid_card = element.GetProperty("identifier_uid").ToString();
                        logger.LogDebug(delcommandlog + $@" uid ={uid_card} DeleteCard ");

                        var status = (await dev.DeleteCard(uid_card)).StatusCode;

                        switch (status)
                        {
                            case HttpStatusCode.OK:
                                logger.LogDebug($@"{delcommandlog}   Answer: OK uid={uid_card}");
                                db.DeleteCardInDev((int)card["id_cardindev"]);
                                break;
                            default:
                                logger.LogDebug($@"Answer destination: {delcommandlog} Answer: ERR uid={uid_card} no delete");
                                db.UpdateCardInDevIncrement((int)card["id_cardindev"]);
                                break;
                        }
                    }

                    if (jsonlist.Count() == 0)
                    {
                        logger.LogDebug($@"{delcommandlog} Answer: OK no card in panel");
                        db.DeleteCardInDev((int)card["id_cardindev"]);
                    }
                    break;

                default:
                    logger.LogError($@"{delcommandlog} Answer: ERR faild GetInfoCard (не удалось получить информацию о карте)");
                    db.UpdateCardInDevIncrement((int)card["id_cardindev"]);
                    break;
            }
        }


        private async Task ProcessLogEvent(DB db, Device dev, LogItem logEvent, int deviceId)
        {
           
            try
            {
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

                // Компактный формат
                //string note = $"Dev={deviceId},";
                //note += $"RDate={DateTime.Now:dd.MM HH:mm},";
                //note += $"DDate={logEvent.EventTime:dd.MM HH:mm},";
                //note += $"Ev={eventCode}";



                //// Тип события для информации

                string eventType = logEvent.name?.key ?? "Unknown";
                DateTime dateTime = DateTimeOffset.FromUnixTimeMilliseconds(logEvent.timestamp).DateTime;
                string note = $",Type={eventType} Readdate=#{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}# DeviceDate =#{dateTime}# DeviceDate = {logEvent.timestamp}, Card={cardNumber}";

 
                logger.LogDebug($"+{note}");
                ;

                if (eventCode > 0)
                    {
                    bool success = db.EventInsert(
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

                    if (success)
                    {
                        logger.LogInformation($"Event saved: Dev={deviceId}, Type={eventType}, Code={eventCode}");
                    }
                    else
                    {
                        logger.LogWarning($"Failed to save event: Dev={deviceId}, Type={eventType} ");
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