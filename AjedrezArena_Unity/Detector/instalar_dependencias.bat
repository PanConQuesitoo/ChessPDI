@echo off
REM ==============================================================
REM  Instala lo que necesita ajedrez_vivo.py (una sola vez, ~1 GB)
REM  en un entorno virtual propio: la carpeta venv de aqui al lado.
REM  Para desinstalar basta con borrar la carpeta venv.
REM ==============================================================
cd /d "%~dp0"
set "PY=%LOCALAPPDATA%\Programs\Python\Python312\python.exe"
if not exist "%PY%" set "PY=py -3.12"
if not exist venv\Scripts\python.exe (
  echo Creando entorno virtual con Python 3.12...
  %PY% -m venv venv
)
venv\Scripts\python.exe -m pip install --upgrade pip
venv\Scripts\python.exe -m pip install "numpy<2" "mediapipe==0.10.21" pillow torch torchvision
echo.
venv\Scripts\python.exe -c "import torch, cv2, mediapipe; print('OK  torch', torch.__version__, '| opencv', cv2.__version__, '| mediapipe', mediapipe.__version__)"
echo.
echo Listo. Ahora abre Unity, dale Play y ejecuta iniciar_camara.bat
pause
