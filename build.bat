dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
if errorlevel 1 (echo Ошибка сборки & pause & exit /b 1)
if not exist dist mkdir dist
copy /y publish\TmcSklad.exe dist\TmcSklad.exe
echo.
echo Готово: dist\TmcSklad.exe
pause
