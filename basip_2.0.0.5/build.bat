@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ========================================
echo    Сборка Basip Key Service
echo ========================================
echo.

echo Текущая папка: %CD%
echo.

echo Шаг 1: Очистка старых файлов...
if exist "publish" rmdir /s /q "publish"
if exist "Output" rmdir /s /q "Output"
echo [OK] Папки очищены
echo.

echo Шаг 2: Поиск файла проекта...
dir *.csproj /b
if errorlevel 1 (
    echo [ОШИБКА] Файл .csproj не найден в текущей папке!
    echo Убедитесь, что build.bat находится в папке с Basip.csproj
    pause
    exit /b 1
)

for /f "delims=" %%i in ('dir *.csproj /b') do set PROJECT_FILE=%%i
echo Найден проект: !PROJECT_FILE!
echo.

echo Шаг 3: Восстановление NuGet пакетов...
dotnet restore "!PROJECT_FILE!"
if errorlevel 1 (
    echo [ОШИБКА] Не удалось восстановить пакеты
    pause
    exit /b 1
)
echo [OK] Пакеты восстановлены
echo.

echo Шаг 4: Публикация приложения...
dotnet publish "!PROJECT_FILE!" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -o "publish\win-x64"
if errorlevel 1 (
    echo [ОШИБКА] Публикация не удалась!
    pause
    exit /b 1
)
echo [OK] Приложение собрано
echo.

echo Шаг 5: Проверка результата...
if not exist "publish\win-x64\basipkeysservices.exe" (
    echo [ОШИБКА] Исполняемый файл не найден!
    echo Ищем любые EXE файлы...
    dir "publish\win-x64\*.exe" /b
    pause
    exit /b 1
)

for %%F in ("publish\win-x64\basipkeysservices.exe") do (
    set size=%%~zF
    set /a sizeMB=!size!/1048576
    echo Размер EXE: !sizeMB! МБ
)
echo.

echo Шаг 6: Создание папок...
mkdir "Output" >nul 2>&1
echo [OK] Папка Output создана
echo.

echo Шаг 7: Создание инсталлятора...
if not exist "BasipSetup.iss" (
    echo [ОШИБКА] Файл BasipSetup.iss не найден!
    echo Создайте файл скрипта Inno Setup
    pause
    exit /b 1
)

"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "BasipSetup.iss"
if errorlevel 1 (
    echo [ОШИБКА] Ошибка Inno Setup!
    echo Проверьте:
    echo 1. Установлен ли Inno Setup
    echo 2. Правильный ли путь в 43-й строке build.bat
    echo 3. Нет ли ошибок в BasipSetup.iss
    pause
    exit /b 1
)

if exist "Output\BasipKeyServiceSetup_v1.0.13.exe" (
    echo.
    echo ========================================
    echo УСПЕХ: Инсталлятор создан!
    echo ========================================
    echo Файл: Output\BasipKeyServiceSetup_v1.0.13.exe
    for %%F in ("Output\BasipKeyServiceSetup_v1.0.13.exe") do (
        set size=%%~zF
        set /a sizeMB=!size!/1048576
        echo Размер инсталлятора: !sizeMB! МБ
    )
    echo.
    echo Готово к распространению!
) else (
    echo [ОШИБКА] Инсталлятор не создан!
    echo Проверьте папку Output
)

echo.
pause