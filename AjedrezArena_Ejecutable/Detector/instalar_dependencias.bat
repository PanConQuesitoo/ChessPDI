@echo off
chcp 65001 >nul
cd /d "%~dp0"
title Ajedrez sobre Arena - instalar la camara
echo.
echo  ==========================================================
echo   Ajedrez sobre Arena: instalando la camara (solo una vez)
echo   Descarga ~1 GB. Puede tardar 5-15 minutos.
echo  ==========================================================
echo.

rem ---- 1) buscar Python 3.9 - 3.12 (MediaPipe todavia no funciona con 3.13)
set "PY="
for %%V in (3.12 3.11 3.10) do (
  if not defined PY (
    py -%%V -c "import sys" >nul 2>&1 && set "PY=py -%%V"
  )
)
if not defined PY (
  python -c "import sys; sys.exit(0 if (3,9)<=sys.version_info[:2]<=(3,12) else 1)" >nul 2>&1 && set "PY=python"
)
if not defined PY if exist "%LOCALAPPDATA%\Programs\Python\Python312\python.exe" set PY="%LOCALAPPDATA%\Programs\Python\Python312\python.exe"

if not defined PY (
  echo No encontre Python 3.12. Lo instalo con winget...
  winget install -e --id Python.Python.3.12 --scope user --accept-package-agreements --accept-source-agreements
  if exist "%LOCALAPPDATA%\Programs\Python\Python312\python.exe" set PY="%LOCALAPPDATA%\Programs\Python\Python312\python.exe"
)
if not defined PY (
  echo.
  echo  [ERROR] No pude instalar Python.
  echo  Instala Python 3.12 desde https://www.python.org/downloads/release/python-3128/
  echo  marcando "Add python.exe to PATH", y vuelve a abrir este archivo.
  pause
  exit /b 1
)
echo Usando: %PY%

rem ---- 2) entorno propio de la camara (carpeta venv, al lado de este archivo)
if not exist "venv\Scripts\python.exe" (
  %PY% -m venv venv
  if errorlevel 1 ( echo [ERROR] No pude crear el entorno venv. & pause & exit /b 1 )
)

rem ---- 3) librerias
"venv\Scripts\python.exe" -m pip install --upgrade pip
"venv\Scripts\python.exe" -m pip install "numpy<2" "mediapipe==0.10.21" pillow torch torchvision
if errorlevel 1 ( echo. & echo [ERROR] Fallo la instalacion. Revisa tu internet y vuelve a intentar. & pause & exit /b 1 )

"venv\Scripts\python.exe" -c "import cv2, mediapipe, torch; print('OK: camara lista')"
echo.
echo  Listo. Ya puedes abrir AjedrezArena.exe
echo  (si mueves la carpeta del juego a otro lugar, vuelve a ejecutar este archivo)
pause
