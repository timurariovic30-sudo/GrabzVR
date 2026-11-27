@echo off
chcp 65001 >nul
rem GRABZ STUDIO — живой офис студии. Запускает локальный сервер и открывает браузер.
cd /d "%~dp0"
where py >nul 2>nul && (py -3 server.py %* & goto :eof)
where python >nul 2>nul && (python server.py %* & goto :eof)
echo Python 3 не найден. Установите его с https://www.python.org/downloads/
pause
