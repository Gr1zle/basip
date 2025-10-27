ИНСТРУКЦИЯ К ПРОГРАММЕ BAS-IP TOOLS SERVICE

1. НАЗНАЧЕНИЕ И КРАТКОЕ ОПИСАНИЕ РАБОТЫ ПРОГРАММЫ

Bas-IP Tools Service - это Windows-сервис для интеграции СКУД системы с панелями управления Bas-IP.

Основные возможности:
- Синхронизация карт доступа - запись и удаление карт на панелях Bas-IP
- Обработка событий доступа - сбор событий с панелей (проход по карте, доступ запрещен)
- Автоматическое обновление - периодическая синхронизация данных
- Логирование операций - детальное протоколирование всех действий

Поддерживаемые операции с картами:
- Запись карт - добавление новых карт доступа на панели
- Удаление карт - удаление карт с панелей
- Проверка статуса - верификация существующих карт

Обрабатываемые события:
- access_granted_by_valid_identifier - доступ разрешен (код 50)
- access_denied_by_unknown_card - доступ запрещен (код 46)


2. УСТАНОВКА

Требования к системе:
- ОС: Windows 7/8/10/11 или Windows Server
- .NET: Runtime 6.0 или выше
- База данных: Firebird SQL
- Сеть: Доступ к панелям Bas-IP по HTTP

Процесс установки:

1. Подготовка базы данных:
   - Убедитесь, что в базе данных существуют необходимые таблицы:
     * DEVICE
     * BAS_PARAM
     * CARDINDEV
     * CARD
     * CARDIDX

2. Настройка конфигурации:
   - Отредактируйте файл appsettings.json
   - Настройте параметры подключения к БД
   - Укажите параметры работы службы

3. Установка как службы Windows:
   # Публикация приложения
   dotnet publish -c Release -o "C:\Program Files\BasipService"

   # Установка службы
   sc create "BasipService" binPath="C:\Program Files\BasipService\Basip.exe"
   sc description "BasipService" "Bas-IP Integration Service"

4. Запуск службы:
   sc start BasipService


3. НАСТРОЙКА

Конфигурационный файл appsettings.json:

{
  "Logging": {
    "LogLevel": {
      "Default": "Trace"
    }
  },
  "Log": {
    "LogFolerPath": "C:\\ProgramData\\Basip\\",
    "LogLevel": "Debug",
    "retainedFileCountLimit": 7
  },
  "Service": {
    "db_config": "User=SYSDBA;Password=temp;Database=C:\\Path\\To\\Database.GDB;DataSource=127.0.0.1;Port=3050;",
    "format_card_uid": 2,
    "run_now": true,
    "timestart": "9:10:0",
    "timeout": "0:0:10",
    "time_wait_http": 10,
    "clear_log": true
  }
}

Параметры настройки:

Раздел "Service":
- db_config - строка подключения к Firebird базе данных
- format_card_uid - формат идентификатора карты:
  * 0 - HEX 8 byte (00124CD8)
  * 2 - DEC 10 digit (0001493650)
- run_now - запуск сразу после старта (true/false)
- timestart - время первого запуска (ЧЧ:ММ:СС)
- timeout - интервал между опросами (ЧЧ:ММ:СС)
- time_wait_http - таймаут HTTP запросов (секунды)
- clear_log - очистка журнала событий на панелях после обработки

Раздел "Log":
- LogFolerPath - путь к каталогу логов
- LogLevel - уровень детализации логов
- retainedFileCountLimit - количество хранимых файлов логов

Настройка устройств в базе данных:

Устройства настраиваются через таблицы БД:

1. Таблица DEVICE - основные параметры устройств
2. Таблица BAS_PARAM - дополнительные параметры:
   - IP - IP-адрес панели (integer)
   - LOGIN - логин для авторизации
   - PASS - пароль для авторизации
   - LASTEVENT - временная метка последнего обработанного события

4. КОНТРОЛЬ РАБОТЫ ПРИЛОЖЕНИЯ (ПОЯСНЕНИЯ К ЛОГАМ)

Уровни логирования:

- Trace - максимальная детализация, отладочная информация
- Debug - отладочная информация о процессах
- Information - информационные сообщения о работе
- Warning - предупреждения о нештатных ситуациях
- Error - ошибки выполнения операций
- Critical - критические ошибки, требующие вмешательства

Типовые сообщения в логах:

Старт/остановка службы:
32 basip start: 09:10:00 deltasleep: 00:00:00
33 Service basip write and delete card started
49 basip stop

Работа с устройствами:
71 Зарегистрировано панелей bas-ip: 5 шт.
Processing 3 devices with valid IP addresses

Операции с картами:
Command destination: writekey id_dev=1 BASE_URL http://192.168.1.100:80 key="0001234567" AddCard
Answer destination: writekey id_dev=1 BASE_URL http://192.168.1.100:80 Answer: OK key="0001234567" uid=1673

Обработка событий:
Event saved: Dev=1, Type=access_granted_by_valid_identifier, Code=50
Event saved: Dev=1, Type=access_denied_by_unknown_card, Code=46

Ошибки и предупреждения:
Skipping device ID 5 - no IP address
Device http://192.168.1.101:80 offline or auth failed
Failed to get events. Status: Unauthorized

Мониторинг работы:

1. Проверка подключения к БД:
   - Успешное подключение: Ok connect database
   - Ошибка: No connect database: [connection_string]

2. Статус устройств:
   - Онлайн: Device [ip] online and authenticated
   - Офлайн: Device [ip] offline or auth failed

3. Обработка событий:
   - Новые события: Retrieved [count] events from device [ip]
   - Нет событий: No new events to process

4. Очистка логов:
   - Успешно: Successful attempt to clear the event log
   - Ошибка: Failed attempt to clear the event log

Файлы логов:
- Расположение: C:\ProgramData\Basip\ (или указанный в настройках путь)
- Имя файла: ArtonitBasIpTools.log
- Ротация: сохраняются последние 7 файлов
- Формат: ДД-ММ-ГГГГ ЧЧ:мм:сс.fff - [Уровень] Сообщение

Диагностика проблем:

1. Нет подключения к панелям:
   - Проверить сетевую доступность
   - Проверить правильность IP-адресов в БД
   - Проверить логин/пароль авторизации

2. Карты не записываются:
   - Проверить формат format_card_uid
   - Проверить наличие карты в таблице CARDINDEV
   - Проверить HTTP-статусы в логах

3. События не обрабатываются:
   - Проверить параметр clear_log
   - Проверить временные метки в BAS_PARAM
   - Проверить коды событий в логах панелей
