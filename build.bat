@echo off
chcp 65001 >nul
echo Збираю SystemMonitor.exe (перший раз завантажуються пакети, це може тривати кілька хвилин)...
dotnet publish SystemMonitor.csproj -c Release -o publish
if errorlevel 1 (
  echo.
  echo ПОМИЛКА збірки. Скопіюйте текст помилки вище і надішліть його.
) else (
  echo.
  echo ГОТОВО: publish\SystemMonitor.exe
)
pause
