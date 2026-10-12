@echo off
chcp 65001 >nul
echo ============================================================
echo   BaoTools - Auto-Sync all 29 Languages
echo ============================================================
echo.
python tools\auto_translate_languages.py
echo.
pause
