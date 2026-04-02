[Setup]
AppName=Basip Key Service
AppVersion=1.0.13
AppPublisher=ООО "Артсек"
DefaultDirName={commonpf}\Basip Key Service
DefaultGroupName=Basip Key Service
OutputDir=Output
OutputBaseFilename=BasipKeyServiceSetup_v1.0.13
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=admin
WizardStyle=modern
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=no
VersionInfoVersion=1.0.13
VersionInfoCompany=ООО "Артсек"
VersionInfoDescription=Service for working with Bas-IP devices
VersionInfoProductName=Basip Key Service

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Types]
Name: "full"; Description: "Полная установка"
Name: "custom"; Description: "Выборочная установка"; Flags: iscustom

[Components]
Name: "main"; Description: "Основные файлы службы"; Types: full custom; Flags: fixed
Name: "service"; Description: "Установка как службы Windows"; Types: full; Flags: checkablealone
Name: "shortcuts"; Description: "Ярлыки в меню Пуск"; Types: full

[Files]
; Основное приложение (единый исполняемый файл)
Source: "publish\win-x64\basipkeysservices.exe"; DestDir: "{app}"; Flags: ignoreversion; Components: main
Source: "publish\win-x64\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist; Components: main
Source: "publish\win-x64\appsettings.Development.json"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist; Components: main

[Icons]
Name: "{group}\Basip Key Service"; Filename: "{app}\basipkeysservices.exe"; Components: shortcuts
Name: "{group}\Просмотр логов"; Filename: "{win}\notepad.exe"; Parameters: "{app}\logs\app.log"; Components: shortcuts
Name: "{group}\Удаление Basip Service"; Filename: "{uninstallexe}"; Components: shortcuts

[Run]
; Установка службы (только если выбран компонент service)
Filename: "sc"; Parameters: "create ""Basip Key Service"" binPath= ""{app}\basipkeysservices.exe"" start= auto DisplayName= ""Basip Key Service"""; Flags: runhidden waituntilterminated; StatusMsg: "Установка службы Windows..."; Components: service
Filename: "sc"; Parameters: "description ""Basip Key Service"" ""Service for working with Bas-IP devices. Version 1.0.13"""; Flags: runhidden waituntilterminated; Components: service
Filename: "sc"; Parameters: "start ""Basip Key Service"""; Flags: runhidden waituntilterminated; StatusMsg: "Запуск службы..."; Components: service

; Создание папки для логов
Filename: "{cmd}"; Parameters: "/c if not exist ""{app}\logs"" mkdir ""{app}\logs"""; Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "sc"; Parameters: "stop ""Basip Key Service"""; Flags: runhidden waituntilterminated
Filename: "sc"; Parameters: "delete ""Basip Key Service"""; Flags: runhidden waituntilterminated

[Code]
var
  ServiceInstalled: Boolean;

// Проверка прав администратора
function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsAdminLoggedOn then
  begin
    MsgBox('Для установки Basip Key Service требуются права администратора.', mbError, MB_OK);
    Result := False;
  end;
end;

// Проверка существующей службы
function IsServiceInstalled: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('sc', 'query "Basip Key Service"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// Инициализация
procedure InitializeWizard;
begin
  ServiceInstalled := IsServiceInstalled;
end;

// Перед установкой
procedure CurStepChanged(CurStep: TSetupStep);
var
  StopResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    if ServiceInstalled then
    begin
      if MsgBox('Обнаружена установленная служба Basip Key Service. Остановить и перезаписать?', mbConfirmation, MB_YESNO) = IDYES then
      begin
        Exec('sc', 'stop "Basip Key Service"', '', SW_HIDE, ewWaitUntilTerminated, StopResultCode);
        Sleep(2000);
      end;
    end;
  end;
  
  if CurStep = ssPostInstall then
  begin
    MsgBox('Установка Basip Key Service завершена успешно!' + #13#10 + #13#10 + 
           'Версия: 1.0.13' + #13#10 +
           'Исполняемый файл: basipkeysservices.exe' + #13#10 + #13#10 +
           'Служба "Basip Key Service" установлена и запущена.' + #13#10 +
           'Логи будут сохраняться в: {app}\logs\app.log' + #13#10 +
           'Конфигурация: {app}\appsettings.json', 
           mbInformation, MB_OK);
  end;
end;