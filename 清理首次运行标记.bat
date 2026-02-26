@echo off
chcp 65001 >nul
echo ========================================
echo   Star Driver - 清理首次运行标记
echo ========================================
echo.
echo 此脚本将删除首次运行标记，让您可以重新体验欢迎界面。
echo.
echo 将删除以下文件：
echo   - %%LocalAppData%%\StarDriver\settings.json
echo   - %%LocalAppData%%\StarDriver\StarDriver.firstrun
echo.
pause

set "APP_DATA=%LOCALAPPDATA%\StarDriver"

echo.
echo 正在清理...
echo.

if exist "%APP_DATA%\settings.json" (
    del "%APP_DATA%\settings.json"
    echo [✓] 已删除 settings.json
) else (
    echo [i] settings.json 不存在
)

if exist "%APP_DATA%\StarDriver.firstrun" (
    del "%APP_DATA%\StarDriver.firstrun"
    echo [✓] 已删除 StarDriver.firstrun
) else (
    echo [i] StarDriver.firstrun 不存在
)

echo.
echo ========================================
echo   清理完成！
echo ========================================
echo.
echo 下次启动应用时将显示欢迎界面。
echo.
pause
