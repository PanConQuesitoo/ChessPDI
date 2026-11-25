# Detector de movimientos de ajedrez · Entrega para Unity

Recibe dos fotos consecutivas del tablero (antes y después de una jugada) y devuelve en JSON qué pieza se movió, de qué casilla a cuál y con qué confianza.

```json
{"pieza": "caballo", "origen": "B1", "destino": "C3", "confianza": 0.97}
```

## Contenido de la carpeta

| Archivo | Para qué sirve |
|---|---|
| `detector_ajedrez.py` | Programa que hace todo: detecta el tablero, clasifica las 64 casillas con la CNN y compara las dos fotos |
| `modelo_ajedrez.pth` | Pesos de la red entrenada (423.175 parámetros, mejor época 36) |
| `config_modelo.json` | Clases, tamaño de entrada (96 × 96) y normalización |
| `requirements_detector.txt` | Librerías de Python que necesita |
| `ChessDetector.cs` | Ejemplo de cómo llamarlo desde Unity en C# |
| `resultados/` | Reporte de clasificación, matriz de confusión y curvas de entrenamiento |

## Instalación (una sola vez)

Con Python 3.10 a 3.14 instalado:

```
cd carpeta_de_esta_entrega
python -m venv venv
venv\Scripts\activate
pip install -r requirements_detector.txt
```

## Uso desde la terminal

```
venv\Scripts\python.exe detector_ajedrez.py foto_antes.jpg foto_despues.jpg
```

Opciones:

- `--salida resultado.json`: además de imprimirlo, lo guarda en un archivo.
- `--completo`: agrega el tipo de jugada, una observación y la lista de casillas que cambiaron.
- `--modelo carpeta`: si `modelo_ajedrez.pth` y `config_modelo.json` están en otra carpeta.

El programa imprime **una sola línea con el JSON** en la salida estándar. Los avisos de OpenCV salen por la salida de error y no afectan el JSON.

Código de salida: `0` todo bien, `2` no se detectó el tablero en alguna foto, `1` otro error (por ejemplo, una foto que no existe).

## Uso desde Unity

`ChessDetector.cs` ejecuta el programa con `System.Diagnostics.Process`, lee la línea de salida y la convierte con `JsonUtility` en un objeto con `pieza`, `origen`, `destino` y `confianza`. En el Inspector hay que poner:

- `rutaPython`: el `python.exe` del entorno donde se instalaron las librerías.
- `rutaScript`: la ruta de `detector_ajedrez.py`.

Cada llamada tarda unos segundos, porque carga el modelo y procesa dos fotos. Conviene llamarla en un hilo aparte o en una corrutina para no congelar la escena.

## Tipos de resultado (con `--completo`)

| `tipo` | Significado |
|---|---|
| `normal` | Una pieza pasó a una casilla vacía |
| `captura` | Una pieza pasó a una casilla donde había otra |
| `enroque` | El rey se movió dos columnas y la torre lo acompañó |
| `captura_al_paso` | Captura al paso de un peón |
| `promocion` | Un peón llegó a la última fila y cambió de pieza |
| `captura_misma_clase` | Una pieza capturó a otra de su misma clase (por ejemplo, peón por peón). Se conoce el origen, pero no el destino |
| `inconsistente` | Cambiaron dos casillas pero la pieza que sale no coincide con la que llega: probablemente la red confundió una pieza |
| `sin_cambios` | Las dos fotos tienen el mismo estado |
| `no_determinado` | Cambiaron casillas que no corresponden a una jugada conocida |
| `error` | No se detectó el tablero o no se pudo abrir una foto |

## Limitaciones

- Las clases no distinguen color: el resultado dice "caballo", no "caballo blanco".
- Las fotos deben tomarse desde arriba, del lado de las blancas (fila 1 abajo), con el mismo tablero usado en el entrenamiento.
- Cuando `confianza` es baja (menos de 0,7, aproximadamente), conviene pedir una foto nueva o confirmar la jugada.

## Resultados del modelo

Entrenado con 500 tableros (13.555 imágenes de casillas), divididos por fotografía en 350 de entrenamiento, 75 de validación y 75 de prueba.

| Clase | Precisión | Recall | F1 | Casillas en prueba |
|---|---|---|---|---|
| rey | 1,000 | 0,980 | 0,990 | 150 |
| reina | 0,978 | 0,978 | 0,978 | 92 |
| torre | 0,984 | 1,000 | 0,992 | 180 |
| alfil | 1,000 | 0,975 | 0,987 | 157 |
| caballo | 1,000 | 1,000 | 1,000 | 106 |
| peón | 0,993 | 1,000 | 0,997 | 588 |
| vacía | 1,000 | 1,000 | 1,000 | 600 |

- Exactitud en prueba: **99,5 %** (9 errores en 1.873 casillas).
- Los errores son entre piezas de forma parecida vista desde arriba: alfil y peón; rey, reina y torre. La clase vacía no tiene errores, por eso el origen y el destino de una jugada son muy confiables.
- Detección de movimientos en pares de prueba: 10 de 10.

Gráficas en `resultados/matriz_confusion.png` y `resultados/curvas_entrenamiento.png`.
