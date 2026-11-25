@echo off
REM Camara en tiempo real: mano (UDP 5065) + jugadas (UDP 5066) -> Unity
REM Otra camara:   iniciar_camara.bat --cam 1
REM Webcam espejo: iniciar_camara.bat --espejo
cd /d "%~dp0"
if not exist venv\Scripts\python.exe (
  echo Primero ejecuta instalar_dependencias.bat
  pause
  exit /b 1
)
venv\Scripts\python.exe ajedrez_vivo.py %*
pause
