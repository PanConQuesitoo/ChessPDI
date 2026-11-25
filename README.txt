# Ajedrez Interactivo sobre Arena

Un ajedrez 3D en Unity que se juega frente a una webcam. Se puede jugar sobre un tablero real o sin ninguno. Con tablero, la cámara ve qué ficha cambió de lugar y el juego repite esa jugada. Sin tablero, la mano real mueve una mano virtual, como el control del Wii; al soltar una pieza, el juego mide qué tan rápido y brusco fue el movimiento y lo convierte en luz, fuego, rayo o supernova.

Proyecto final de Procesamiento Digital de Imágenes, Ingeniería Multimedia, Universidad Autónoma de Occidente (Cali), 2026-2.
Autores: Emmanuel Carrera Cardona y Camila López Vásquez.

![Los 10 escenarios del juego](docs/img/escenarios.jpg)

La explicación técnica completa, con figuras y fórmulas, está en [docs/Ajedrez_sobre_Arena_Como_Funciona.pdf](docs/Ajedrez_sobre_Arena_Como_Funciona.pdf).

## Contenido

1. [Descripción del proyecto](#1-descripción-del-proyecto)
2. [Arquitectura del sistema](#2-arquitectura-del-sistema)
3. [Metodología](#3-metodología)
4. [Implementación](#4-implementación)
5. [Datos](#5-datos)
6. [Interfaz gráfica de usuario](#6-interfaz-gráfica-de-usuario)
7. [Resultados](#7-resultados)
8. [Conclusiones](#8-conclusiones)
9. [Instalación y ejecución](#9-instalación-y-ejecución)

## 1. Descripción del proyecto

### Problemática

En una partida de ajedrez sobre un tablero real, lo que pasa en la mesa se queda en la mesa: no hay respuesta visual ni sonora, y nada conecta el gesto de mover una pieza con un entorno digital. Para lograr esa conexión hay que resolver un problema de visión por computador: una cámara común ve el tablero en perspectiva, con reflejos, sombras y manos tapando las piezas, y aun así tiene que saber dónde está la mano, cuándo terminó la jugada y qué pieza se movió.

### Justificación

El ajedrez es un buen caso de prueba para el procesamiento digital de imágenes porque tiene una geometría conocida (una cuadrícula de 8×8) y reglas estrictas que sirven para validar lo que la cámara cree ver. La primera propuesta usaba un Kinect de Xbox One. Lo cambiamos por una webcam RGB para que el sistema funcione en cualquier PC con Windows, sin hardware especial, y eso obligó a resolver con imagen (homografía, bordes, redes neuronales) lo que el sensor de profundidad habría dado hecho.

### Objetivo general

Integrar el ajedrez con técnicas de procesamiento digital de imágenes y aprendizaje profundo, de forma que las acciones de la mano frente a una cámara produzcan una respuesta visual inmediata en un entorno 3D.

### Objetivos específicos

1. Detectar y enderezar un tablero físico en la imagen de una webcam con PDI tradicional (bordes, contornos, homografía), sin calibración manual.
2. Seguir la mano en tiempo real y reconocer el gesto de agarrar (pinza entre pulgar e índice).
3. Entrenar una red convolucional que clasifique el contenido de cada casilla y usarla para reconocer jugadas.
4. Construir en Unity el juego, los 10 escenarios, los efectos que dependen del movimiento de la mano y un menú que se maneje solo con la mano.
5. Entregar el sistema como ejecutable para Windows que otra persona pueda instalar y probar.

### Solución propuesta

Dos programas que corren al mismo tiempo en el mismo PC. Un programa en Python (`Detector/ajedrez_vivo.py`) abre la cámara, sigue la mano con MediaPipe, encuentra el tablero con OpenCV y lee las piezas con la red neuronal. El juego en Unity dibuja todo, aplica las reglas del ajedrez y genera los efectos. Se comunican por UDP dentro del mismo PC. Unity enciende y apaga el programa de Python sin intervención del usuario.

Cada modo tiene una sola fuente de verdad. Con tablero, la cámara mira desde arriba un tablero real y solo cuenta lo que cambia en él: cuando una ficha cambia de lugar, Python lo detecta, la red lee las casillas y Unity hace esa jugada. La mano virtual queda oculta y no mueve piezas. Sin tablero, la cámara solo ve la mano, que funciona como puntero sobre la pantalla; la pinza agarra y suelta las piezas.

## 2. Arquitectura del sistema

![Arquitectura](docs/img/arquitectura.png)

```mermaid
flowchart LR
    CAM[Webcam 1280x720] --> HILO[Hilo de captura]
    HILO --> MP[MediaPipe Hands<br/>21 puntos + pinza]
    HILO --> TAB[Tablero<br/>Canny + homografía + Sobel]
    TAB --> MOV[Diferencia de cuadros<br/>máquina de estados]
    MOV --> CNN[CNN 7 clases<br/>64 casillas]
    MP -- "UDP 5065: B;x;y;g / V;x;y;g" --> MANO[EntradaManoUDP<br/>ControladorMano]
    CNN -- "UDP 5066: jugada JSON" --> DET[EntradaCamaraAjedrez<br/>ReglasAjedrez]
    TAB -- "UDP 5067/5068: estado y video" --> MENU[ConexionCamara<br/>MenuAjedrez]
    MENU -- "UDP 5070: órdenes y ping" --> HILO
    MANO --> JUEGO[ControladorTablero<br/>Efectos, Escenarios]
    DET --> JUEGO
```

### Componentes

En Python, todo vive en `Detector/ajedrez_vivo.py`, repartido en tres hilos para no perder tiempo real:

| Hilo | Qué hace |
|---|---|
| Cámara | Lee cuadros sin parar y guarda solo el más reciente, así nunca se procesa una imagen vieja |
| Principal | Mano con MediaPipe, estado del sistema, mensajes a Unity y video para el menú |
| Trabajo | Calibrar el tablero y leer las 64 casillas con la red, que tarda cientos de milisegundos |

En Unity, los scripts están en `Assets/AjedrezArena/Scripts/`:

| Script | Función |
|---|---|
| `Menu/ConexionCamara.cs` | Lanza Python, recibe su estado y su video, le envía órdenes y lo apaga al salir |
| `Menu/MenuAjedrez.cs` | Pantallas del menú, cursor de la mano, cámara 360°, calibración y pausa |
| `Mano/EntradaManoUDP.cs`, `Mano/ControladorMano.cs` | Mano virtual: posición, agarre, velocidad, aceleración y efecto al soltar |
| `Deteccion/EntradaCamaraAjedrez.cs`, `Deteccion/DeteccionCamara.cs` | Recibe las jugadas leídas por la red, las valida con las reglas y las ejecuta en el tablero 3D |
| `Ajedrez/ReglasAjedrez.cs` | Motor de reglas puro (sin Unity), verificado con perft |
| `Tablero/ControladorTablero.cs` | Partida, piezas, capturas y animaciones |
| `Efectos/*`, `Escenarios/*` | Partículas, rayos, sonidos sintetizados, iluminación y los 10 escenarios |
| `Editor/CrearEjecutable.cs` | Menú que genera la carpeta del `.exe` lista para compartir |

### Protocolo de comunicación

Todo viaja por `127.0.0.1`. Se eligió UDP porque los datos cambian 15 a 30 veces por segundo: si se pierde un paquete, el siguiente llega milisegundos después y nunca se acumula atraso.

| Puerto | Sentido | Contenido |
|---|---|---|
| 5065 | Python → Unity | Mano: `B;x;y;g` en coordenadas del tablero, o `V;x;y;g` en coordenadas de pantalla |
| 5066 | Python → Unity | Jugada en JSON (`pieza`, `origen`, `destino`, `confianza`, `tipo`), solo en modo juego |
| 5067 | Python → Unity | Estado en JSON: modo, fps, mano, calibración, calidad, quietud, visto bueno, avisos |
| 5068 | Python → Unity | Video JPEG de 560 px (menos de 64 KB) para el menú |
| 5070 | Unity → Python | Órdenes: `modo` (menu, calibrar, juego, libre), recalibrar, girar, espejo, cambiar cámara, ping, salir |

Si Python pasa 10 segundos sin recibir el ping de Unity, se cierra para no dejar la cámara ocupada.

## 3. Metodología

### Etapas del desarrollo

1. Modelado y juego base. Generamos por script en Blender los modelos de 10 escenarios (Machu Picchu, Kioto, Giza, Islandia, Santorini, Cartagena, Louvre, Museo del Oro, Historia Natural y Arte Moderno), cada uno con su tablero, sus 6 piezas y su mano. En Unity escribimos el motor de reglas y lo validamos con perft antes de construir encima.
2. Mano virtual y efectos. La mano primero se movía con el mouse. Medimos velocidad y aceleración de la pieza en casillas por segundo y asignamos un efecto por rangos.
3. Red neuronal de piezas. Entrenamos una CNN propia con casillas recortadas de fotos de tableros (ver [Datos](#5-datos)) y la empaquetamos en `detector_ajedrez.py`, que compara dos fotos y devuelve la jugada.
4. Cámara en tiempo real. Unimos todo en `ajedrez_vivo.py`: seguimiento de la mano, calibración automática, detección de quietud y lectura con la red, comunicándose con Unity por UDP.
5. Menú con la mano. Quitamos el mouse y el teclado del flujo normal: Unity enciende la cámara, el menú se maneja con la pinza y la calibración da el visto bueno sola.
6. Modo sin tablero y distribución. Agregamos el puntero tipo Wii para jugar sin tablero físico y el menú que genera el `.exe`.
7. Una fuente de verdad por modo. Con tablero dejamos de usar la mano para mover piezas: solo cuentan los cambios del tablero real. También quitamos la exigencia de la posición inicial y la orientación automática, y el menú volvió a aceptar el mouse.
8. Pruebas. Pruebas automáticas con imágenes sintéticas y cámaras simuladas, y pruebas con la webcam real del proyecto.

### Técnicas implementadas

| Parte | Técnica | Tipo |
|---|---|---|
| Mejorar la imagen | Filtro bilateral si el ruido supera σ = 8; CLAHE sobre el canal L de Lab si el contraste es bajo | PDI tradicional |
| Encontrar el tablero | Canny (50/150) + dilatación + contornos + `approxPolyDP`; en paralelo, `findChessboardCornersSB` (7×7) con homografía RANSAC | PDI tradicional |
| Enderezar | Homografía con `getPerspectiveTransform` y `warpPerspective` a 100 px por casilla | PDI tradicional |
| Ajustar la cuadrícula | Perfiles de Sobel en x e y; se buscan 9 líneas equiespaciadas sobre los picos | PDI tradicional |
| Confirmar que es un tablero | Correlación del brillo 8×8 con un patrón ±1; umbral 0,30 (0,35 para el visto bueno) | PDI tradicional |
| Orientar | Retirado en la v6: el tablero se usa tal como se ve; el giro se corrige a mano con *Girar 90°* | - |
| Detectar fin de jugada | Diferencia de cuadros en gris: píxel cambiado si difiere más de 22 niveles; actividad si cambia más del 1,5 %; quietud de 0,6 s | PDI tradicional |
| Seguir la mano | MediaPipe Hands, 21 puntos, imagen reducida a 640 px | Deep Learning preentrenado |
| Agarrar y soltar | Pinza normalizada con histéresis (cerrar < 0,30, abrir > 0,42) | Geometría sobre los puntos |
| Reconocer piezas | CNN de 4 bloques convolucionales, 7 clases, entrada 96×96 | Deep Learning supervisado |
| Validar la jugada | Motor de reglas con jugadas legales, jaque, enroque, al paso, coronación | Algoritmo clásico |

La pinza se calcula dividiendo la distancia entre las puntas del pulgar (punto 4) y del índice (punto 8) por el tamaño de la mano, medido de la muñeca (punto 0) al nudillo del medio (punto 9). Así el gesto vale lo mismo con la mano cerca o lejos de la cámara:

$$\text{pinza} = \frac{\lVert P_4 - P_8 \rVert}{\lVert P_0 - P_9 \rVert}$$

![Histéresis de la pinza](docs/img/pinza.png)

La homografía es una matriz 3×3 que lleva cada punto de la imagen al plano del tablero. La división entre *w* es la que deshace la perspectiva:

$$\begin{bmatrix} x' \\ y' \\ w \end{bmatrix} = H \begin{bmatrix} x \\ y \\ 1 \end{bmatrix}, \qquad b_x = \frac{x'}{w},\quad b_y = \frac{y'}{w}$$

![Homografía](docs/img/homografia.png)

### Modelos utilizados

Para la mano se usa MediaPipe Hands de Google, en su versión ligera (`model_complexity=0`), una mano, confianza 0,6 para detectar y 0,5 para seguir. No lo entrenamos; se usa tal cual.

Para las piezas, la CNN propia `CNNAjedrez` tiene cuatro bloques Conv 3×3 + BatchNorm + ReLU + MaxPool con 32, 64, 128 y 256 filtros, promedio global, capa de 256 a 128 con dropout y salida de 7 clases (rey, reina, torre, alfil, caballo, peón, vacía). Tiene 423.175 parámetros. Una red pequeña entrenada desde cero basta, sin recurrir a transfer learning con ResNet o MobileNet, porque el problema es estrecho (siempre el mismo tipo de recorte, siempre desde arriba), la red cabe en CPU y lee las 64 casillas en un solo lote.

![Arquitectura de la CNN](docs/img/cnn.png)

No usamos deep learning para encontrar el tablero porque la geometría clásica lo resuelve bien, es más rápida y permite explicar cada fallo (por ejemplo, "la calidad de cuadrícula es 0,22"). La red solo entra donde las reglas a mano no alcanzan: distinguir un alfil de un peón visto desde arriba.

## 4. Implementación

### Estructura del repositorio

```
Assets/AjedrezArena/
  Modelos/                 98 FBX: escenarios, tableros, piezas y manos temáticas
  Resources/Miniaturas/    imágenes de los escenarios para el menú
  Scripts/                 código C# (ver tabla de componentes)
Detector/
  ajedrez_vivo.py          visión en tiempo real (lo enciende Unity)
  entrega_unity/           detector_ajedrez.py, modelo_ajedrez.pth, config_modelo.json, resultados/
  enviar_deteccion_udp.py  envía jugadas de prueba a Unity sin cámara
  enviar_mano_udp.py       versión anterior del puente: demo y seguimiento por color en HSV
  instalar_dependencias.bat
  LEEME_CAMARA.txt
Blender/                   scripts que generan los modelos y animan una partida
Pruebas/                   pruebas automáticas en Python y C#
docs/                      PDF técnico e imágenes de este README
```

### `Detector/ajedrez_vivo.py`

Las clases principales son estas:

- `Camara`: hilo de captura. Al abrir prueba combinaciones de motor y formato (DirectShow, Media Foundation, con y sin MJPG, y resoluciones de 1280, 960 y 640 px), descarta las que dan imagen negra, mide los fps y se queda con la primera que llega a 15. Guarda la elegida en `camara_ok.json`.
- `SeguidorMano`: MediaPipe, cursor entre pulgar e índice, pinza con histéresis.
- `Calibracion`: busca el tablero con el detector, convierte píxeles a coordenadas de tablero y combina giro y espejo.
- `MaquinaJugadas`: estados LISTO → OCUPADO → ASENTANDO → LEYENDO.
- `Clasificador`: carga la CNN una vez y lee lotes de casillas.
- `Sesion`: el bucle por cuadro. Decide qué hacer según el modo que pide Unity (`menu`, `calibrar`, `juego`, `libre`).

![Máquina de estados](docs/img/estados.png)

En el modo `libre` (sin tablero) la función que convierte la mano en puntero es esta:

```python
def puntero_desde_imagen(ix, iy, zona=0.1):
    """Imagen (0..1) -> pantalla (0..1). Se invierte x como un espejo y el
    recuadro central [zona, 1-zona] cubre toda la pantalla."""
    def ajustar(v):
        return min(1.0, max(0.0, (v - zona) / (1.0 - 2.0 * zona)))
    return ajustar(1.0 - ix), ajustar(iy)
```

Unity recibe ese punto y lanza un rayo desde la cámara del juego hasta el plano del tablero:

```csharp
Ray r = camara.ViewportPointToRay(new Vector3(fuente.Posicion.x, fuente.Posicion.y, 0f));
Plane plano = tablero.Plano;
if (plano.Raycast(r, out float d)) p = r.GetPoint(d);   // aquí aparece la mano virtual
```

![Puntero sin tablero](docs/img/puntero.png)

### Una sola fuente de verdad por modo

Con tablero manda el tablero real. Al empezar la partida, `MenuAjedrez` pausa la mano virtual (`ManoJuega` es falso), así que la mano del jugador no mueve nada en pantalla. El jugador mueve la ficha física; cuando retira la mano y todo queda quieto, Python compara el tablero con la última lectura, deduce qué ficha cambió de lugar y manda la jugada. `EntradaCamaraAjedrez` la interpreta con las reglas (`InterpreteDeteccion.Interpretar`): si es legal, la pieza 3D se mueve con su animación; si no, se avisa "Jugada no válida" y el tablero 3D no cambia. Las lecturas con confianza menor a 0,7 esperan confirmación (Enter para aceptar, Retroceso para descartar).

Sin tablero manda la mano: la pinza agarra, lleva y suelta la pieza, y la jugada se ejecuta al soltar. En este modo Python no envía jugadas. Si se juega sin cámara, el mouse hace el papel de la mano.

### Efectos según el movimiento

Estos efectos son del modo sin tablero, donde la mano mueve la pieza. Con tablero, la jugada que llega de la cámara se anima con un impulso suave. Mientras la mano lleva una pieza se mide su rapidez y su aceleración. Al soltar se toma el máximo de los últimos 0,45 s, para que cuente el impulso aunque la mano frene justo antes:

![Efectos](docs/img/efectos.png)

Los umbrales (`rapidezFuego = 5`, `rapidezRayo = 12`, `aceleracionRayo = 80`, `tiempoParaCargar = 2,2 s`) se ajustan en el Inspector del objeto `AjedrezArena`. La tecla F1 muestra los medidores en vivo.

### Otros scripts

- `Blender/generar_modelos_ajedrez.py` genera los 98 FBX. Cada pieza es un perfil de torno definido por puntos, y cada escenario es una función.
- `Blender/animar_partida.py` anima "La Ópera" de Morphy (París, 1858) en el escenario del Louvre.
- `Detector/entrega_unity/detector_ajedrez.py` funciona solo por terminal: `python detector_ajedrez.py antes.jpg despues.jpg` imprime la jugada en JSON.

## 5. Datos

### Conjunto de entrenamiento de la CNN

| Dato | Valor |
|---|---|
| Fotografías de tablero | 500, del mismo tablero de madera, tomadas desde arriba |
| Muestras (casillas recortadas) | 13.555 |
| División | Por fotografía: 350 de entrenamiento, 75 de validación, 75 de prueba |
| Clases | rey, reina, torre, alfil, caballo, peón, vacía |
| Entrada de la red | Recorte de la casilla con 15 % de margen, redimensionado a 96×96 |
| Normalización | media (0,417; 0,451; 0,441), desviación (0,268; 0,229; 0,222) |

La división se hizo por fotografía para que las casillas de una misma foto no queden repartidas entre entrenamiento y prueba. Si quedaran repartidas, la red vería en la prueba fotos casi iguales a las de entrenamiento y la exactitud saldría inflada.

El recorte lleva margen porque, vista desde arriba con algo de perspectiva, una pieza alta invade la casilla vecina. Sin margen, la parte de arriba de un rey quedaría cortada.

Las clases no distinguen color. El color se resuelve en Unity con el turno y las reglas: si juegan las blancas, la pieza que se movió es blanca.

Las fotografías originales no se incluyen en el repositorio por su tamaño. Enlace de descarga: *pendiente de publicar*.

### Visualización

La lectura de un tablero sintético de prueba se ve así: el tablero detectado en verde, a la izquierda, y lo que la red reconoce en cada casilla, a la derecha.

<p>
<img src="docs/img/prueba_tablero_detectado.jpg" width="49%">
<img src="docs/img/prueba_lectura_red.jpg" width="40%">
</p>

## 6. Interfaz gráfica de usuario

El menú se maneja solo con la mano. El cursor es el punto medio entre el pulgar y el índice. Para elegir un botón se juntan los dedos o se deja el cursor quieto 1,1 s encima, mientras un anillo se va llenando. El mouse también funciona siempre en el menú: al moverlo o hacer clic toma el control del cursor, y a los 4 s sin usarlo vuelve a mandar la mano.

![Flujo del menú](docs/img/menu.png)

| Pantalla | Qué hace |
|---|---|
| Encendiendo | Unity lanza Python y muestra el avance. Si algo falla, ofrece *Instalar librerías*, *Otra cámara*, *Reintentar* o *Jugar sin cámara* |
| Principal | Fondo con la cámara girando 360° alrededor del tablero. Botones: *Jugar con tablero*, *Jugar solo con la mano*, *Calibrar cámara*, *Salir*. Una tarjeta muestra el video y el estado de la cámara |
| Tableros | Los 10 escenarios con miniatura. Al elegir uno cambia el fondo |
| Calibración (con tablero) | Video y lista de verificación: cámara, tablero, quietud, sin manos y lectura de la red. No exige la posición inicial: el tablero se lee tal como está, con las blancas abajo |
| Preparar la mano (sin tablero) | Video en espejo con la zona útil marcada; basta con juntar los dedos una vez |
| Juego | Al llevar la mano al borde derecho aparece el botón *Menú* (también con ESC) |
| Pausa | *Continuar*, *Siguiente escenario*, *Nueva partida*, *Menú principal*, *Salir* |

Elegir el escenario, calibración con una pieza fuera de lugar, y visto bueno con la cuenta regresiva:

![Elegir tablero](docs/img/menu_tableros.jpg)
![Calibración pendiente](docs/img/menu_calibracion.jpg)
![Cámara lista](docs/img/menu_listo.jpg)

Partida en modo libre con la webcam del proyecto:

![Juego sin tablero](docs/img/juego_modo_libre.jpg)

Cada escenario tiene su propia mano:

![Manos temáticas](docs/img/manos.jpg)

### Instrucciones de uso

1. Abrir el juego (Play en Unity o `AjedrezArena.exe`). La cámara se enciende sola; la primera vez tarda entre 10 y 20 segundos.
2. Para jugar sin tablero: *Jugar solo con la mano* → elegir escenario → *Preparar la mano* → juntar los dedos una vez → cuenta 3-2-1.
3. Para jugar con tablero: poner el tablero con las blancas abajo bajo la cámara (si queda de lado, usar *Girar 90°*), elegir *Jugar con tablero* → escenario → *Calibrar y jugar*, y retirar las manos. Cuando todo está en verde, el juego empieza solo.
4. Para mover una pieza con tablero: moverla en el tablero real y retirar la mano. El juego repite la jugada en pantalla cuando la cámara ve el cambio.
5. Para mover una pieza sin tablero: apuntar, juntar los dedos para agarrar, llevarla y separar los dedos para soltarla.
6. El menú también se puede usar con el mouse. Para volver al menú durante la partida: ESC, o llevar la mano fuera del tablero (con tablero) o al borde derecho (sin tablero).

## 7. Resultados

### Clasificación de casillas (conjunto de prueba, 1.873 casillas)

| Clase | Precisión | Recall | F1 | Casillas |
|---|---|---|---|---|
| rey | 1,000 | 0,980 | 0,990 | 150 |
| reina | 0,978 | 0,978 | 0,978 | 92 |
| torre | 0,984 | 1,000 | 0,992 | 180 |
| alfil | 1,000 | 0,975 | 0,987 | 157 |
| caballo | 1,000 | 1,000 | 1,000 | 106 |
| peón | 0,993 | 1,000 | 0,997 | 588 |
| vacía | 1,000 | 1,000 | 1,000 | 600 |
| **Exactitud** | | | **0,995** | 1.873 |

<p>
<img src="docs/img/matriz_confusion.png" width="44%">
<img src="docs/img/curvas_entrenamiento.png" width="54%">
</p>

Los 9 errores se dan entre piezas que se parecen vistas desde arriba: 4 alfiles leídos como peón, 2 reyes como reina, 1 rey como torre y 2 reinas como torre. La clase *vacía* no tiene ningún error, y eso es lo que más importa para reconocer jugadas: el origen (casilla que se vacía) y el destino (casilla que se llena) salen de esa clase. Una confusión entre rey y reina no cambia qué casillas se movieron.

La validación oscila hasta la época 25 (picos en las épocas 12 y 18) y después se estabiliza. Se guardó la época 36, la de menor pérdida de validación (0,015, con 99,7 % de acierto). La pérdida de entrenamiento queda por encima de la de validación porque el dropout solo actúa durante el entrenamiento.

### Pruebas del sistema

| Prueba | Qué verifica | Resultado |
|---|---|---|
| Pares de fotos de prueba (detector) | Jugada correcta a partir de una foto antes y otra después | 10 de 10 |
| `PerftPrueba.cs` | Motor de reglas contra los valores de referencia en 5 posiciones (por ejemplo, 197.281 posiciones a profundidad 4 desde el inicio) | Coincide en todas |
| `DeteccionPrueba.cs` | 29 casos: la partida de Morphy completa hasta el mate, enroques, al paso, coronación, errores de la red | 29 de 29 |
| `ConciliacionPrueba.cs` | 9 casos de mano contra cámara | 9 de 9 |
| `prueba_vivo.py` | Tablero sintético en perspectiva: calibración, 64 casillas, cámara girada 90° y 180°, imagen espejada, flujo menú → calibrar → juego, jugada con mano simulada, modo libre | Todo correcto |
| Cámaras simuladas | Imagen negra, formatos lentos, cámara inexistente, cambio de cámara | Todo correcto |

### Rendimiento con la webcam real

| Medida | Antes | Después |
|---|---|---|
| Imágenes por segundo de la cámara | 1 (DirectShow, imagen negra) y 3–4 (Media Foundation sin comprimir) | 15,4 (Media Foundation + MJPG, 1280×720) |
| Arranque de la cámara | Python se cerraba porque abrir la webcam tardaba 7 s | Abre en 5–7 s y avisa al menú mientras tanto |
| Unity en el editor | | ~56 fps |

El cuello de botella es la webcam. Con poca luz alarga la exposición y entrega menos imágenes por segundo, aunque el procesamiento alcance de sobra.

## 8. Conclusiones

Cada técnica quedó en la parte donde rinde más. La geometría clásica (bordes, homografía, correlación con el patrón del tablero) encuentra y endereza el tablero sin entrenamiento y, cuando falla, se sabe por qué. La red neuronal resuelve solo lo que las reglas no alcanzan, distinguir piezas parecidas, y llega a 99,5 % de exactitud por casilla. MediaPipe da el seguimiento de la mano sin tener que entrenar nada. Las reglas del ajedrez cierran el ciclo: corrigen lo que la cámara no puede saber, como el color de la pieza.

La decisión que más ordenó el sistema fue darle a cada modo una sola fuente de verdad. Con tablero, el tablero real decide: no hay que conciliar lo que hizo la mano con lo que vio la cámara, y una jugada mal leída nunca queda en pantalla si no es legal. El costo es una espera de casi un segundo entre mover la ficha y verla moverse en el juego. Sin tablero, la mano decide y responde al instante.

### Limitaciones

- La red no distingue colores, y una captura entre piezas de la misma clase solo cambia una casilla. Se deduce con las reglas cuando hay una sola captura posible; si hay dos, hace falta otra lectura.
- La red se entrenó con un solo tablero de madera. Con un tablero de otro estilo o colores, habría que reentrenarla.
- Con poca luz bajan los fps y suben los errores de lectura.
- El modo con tablero necesita la cámara desde arriba y el tablero completo en la imagen, con las blancas abajo.
- Con tablero, la jugada aparece en pantalla casi un segundo después de mover la ficha, porque hay que esperar a que la mano salga y todo quede quieto.
- El `.exe` no incluye Python: la persona que lo recibe debe ejecutar `instalar_dependencias.bat`, que descarga cerca de 1 GB.

### Oportunidades de mejora

- Ampliar las clases con el color (14 clases) o medir el brillo de cada pieza.
- Entrenar con varios tableros y con aumento de datos de iluminación.
- Ejecutar la red en GPU con CUDA.
- Empaquetar Python con PyInstaller para que el ejecutable no requiera instalación.
- Volver a la idea original de proyectar sobre arena: la vista cenital ortográfica (tecla V) ya está lista para un proyector.

## 9. Instalación y ejecución

### Requisitos

- Windows 10 u 11 con una webcam.
- Unity 6 (6000.3.14f1) con Universal Render Pipeline, para abrir el proyecto.
- Python 3.12. MediaPipe 0.10.21 no funciona con Python 3.13.

### Ejecutar desde Unity

1. Clonar el repositorio y abrir la carpeta como proyecto en Unity Hub.
2. Una sola vez, hacer doble clic en `Detector/instalar_dependencias.bat`. Crea `Detector/venv` e instala `numpy<2`, `mediapipe==0.10.21`, `pillow`, `torch` y `torchvision`.
3. Abrir `Assets/Scenes/SampleScene.unity`. Si la escena está vacía, usar el menú **Ajedrez Arena → 1. Configurar escena**.
4. Dar Play. Unity enciende la cámara; lo que pase queda en `Detector/ajedrez_vivo.log`.

### Generar el ejecutable

En Unity, menú **Ajedrez Arena → 5. Crear .exe para compartir**. La carpeta queda en `Build/AjedrezArena` (unos 120 MB) con el juego, la carpeta `Detector` y un `LEEME_PRIMERO.txt`. Quien la reciba debe descomprimirla, ejecutar una vez `Detector\instalar_dependencias.bat` (instala Python 3.12 con winget si hace falta) y abrir `AjedrezArena.exe`.

### Ejecutar solo la parte de visión

```
cd Detector
venv\Scripts\python.exe ajedrez_vivo.py --ventanas      # con ventanas de OpenCV (C recalibrar, G girar, M espejo, Q salir)
venv\Scripts\python.exe ajedrez_vivo.py --diagnostico   # mide los fps de la cámara con cada formato
venv\Scripts\python.exe entrega_unity\detector_ajedrez.py antes.jpg despues.jpg --completo
```

Para probar Unity sin cámara, con el juego en Play: `python enviar_deteccion_udp.py partida` juega la Ópera de Morphy completa.

### Ejecutar las pruebas

```
python -I Pruebas/prueba_vivo.py Detector/entrega_unity Detector/ajedrez_vivo.py
```

Las pruebas en C# se compilaron con Mono, usando `StubsUnity.cs` en lugar de las librerías de Unity:

```
mcs -out:pruebas.exe Pruebas/StubsUnity.cs Pruebas/PerftPrueba.cs Assets/AjedrezArena/Scripts/Ajedrez/ReglasAjedrez.cs
mono pruebas.exe
```

### Problemas comunes

| Síntoma | Qué hacer |
|---|---|
| "Falta instalar las librerías" | Ejecutar `Detector/instalar_dependencias.bat` |
| La luz de la cámara prende pero no hay imagen | El programa prueba otro formato automáticamente; si todos dan negro, la cámara está tapada o el cuarto está oscuro |
| El cursor se mueve al revés | Activar o desactivar `espejarImagen` en el objeto `MenuConLaMano` |
| No encuentra el tablero | El tablero debe verse completo, desde arriba y con luz pareja |
| Materiales rosados | Usar URP, y antes de un build ejecutar **Ajedrez Arena → 2. Crear materiales** |
