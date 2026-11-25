"""
=====================================================================
  Ajedrez Interactivo sobre Arena - LA CÁMARA EN TIEMPO REAL (v2)
=====================================================================
Una sola cámara mirando el tablero desde arriba. Normalmente lo ENCIENDE
UNITY (menú del juego) con --unity; también se puede usar solo.

Qué hace en cada cuadro
  1) MANO     MediaPipe Hands -> 21 puntos. Cursor = punto medio pulgar-índice.
              Pinza (pulgar e índice juntos) = agarrar / hacer clic en el menú.
  2) TABLERO  Homografía con las esquinas que encuentra el detector de la entrega.
  3) QUIETUD  Diferencia entre cuadros sobre el tablero enderezado.
  4) RED      La CNN de la entrega lee las 64 casillas (en un hilo aparte).

Modos (los cambia Unity por el puerto de control 5070)
  menu      solo mano + estado + video: NO envía jugadas
  calibrar  además lee el tablero cada ~1 s y revisa: tablero visto, quieto y sin
            manos, 32 piezas donde van (posición inicial) y orientación
            (la corrige sola: giro y espejo). Cuando todo está bien -> "listo"
  juego     mano en coordenadas del tablero (5065) y jugadas al quedar quieto (5066)

Puertos (todo en 127.0.0.1 salvo --host)
  5065  -> Unity   mano en el juego          "B;x;y;g"
  5066  -> Unity   jugada detectada          JSON {pieza, origen, destino, confianza, tipo}
  5067  -> Unity   estado (cada cuadro)      JSON (ver EstadoVivo.a_dict)
  5068  -> Unity   video de la cámara         JPEG
  5070  <- Unity   control                   {"cmd": "modo", "valor": "menu|calibrar|juego", "nueva": true}
                                            {"cmd": "recalibrar"} {"cmd": "camara", "indice": 1}
                                            {"cmd": "ping"} {"cmd": "salir"}

Uso suelto (sin Unity):  python ajedrez_vivo.py            (abre ventanas y juega directo)
                         python ajedrez_vivo.py --cam 1
Teclas en las ventanas:  C recalibrar · B base · L leer · O orientar · M espejo · G girar · Q salir

Requisitos: torch, torchvision, pillow, numpy<2, mediapipe==0.10.21 (instalar_dependencias.bat)
"""
import argparse
import concurrent.futures as futuros
import json
import os
import socket
import sys
import threading
import time

import cv2
import numpy as np

AQUI = os.path.dirname(os.path.abspath(__file__))

ABREV = {"rey": "R", "reina": "D", "torre": "T", "alfil": "A", "caballo": "C", "peon": "P", "vacia": "."}
VERDE, ROJO, AMARILLO, BLANCO, CIAN, GRIS = (80, 220, 80), (60, 60, 240), (40, 210, 240), (255, 255, 255), (230, 200, 60), (150, 150, 150)
COLUMNAS = "ABCDEFGH"
FILA_MAYOR = ["torre", "caballo", "alfil", "reina", "rey", "alfil", "caballo", "torre"]


# =====================================================================
#  POSICIÓN INICIAL Y ORIENTACIÓN
# =====================================================================
def esperado_inicial():
    d = {}
    for j, c in enumerate(COLUMNAS):
        d[c + "1"] = d[c + "8"] = FILA_MAYOR[j]
        d[c + "2"] = d[c + "7"] = "peon"
        for r in "3456":
            d[c + r] = "vacia"
    return d


ESPERADO = esperado_inicial()


def nombre(i, j):
    """Fila i de arriba (0 = fila 8) y columna j (0 = A) del tablero enderezado."""
    return COLUMNAS[j] + str(8 - i)


def malas_ocupacion(estado):
    """Casillas cuya ocupación (pieza / vacía) no coincide con la posición inicial."""
    return sorted(c for c, e in estado.items() if (e["clase"] != "vacia") != (ESPERADO[c] != "vacia"))


def tipos_correctos(estado):
    return sum(1 for c, e in estado.items() if e["clase"] == ESPERADO[c])


def transformar_rejilla(valor, giro, espejo):
    """valor(i, j) -> función con la vista girada 'giro' grados (horario) y luego espejada."""
    def nuevo(i, j):
        if espejo:
            j = 7 - j
        if giro == 90:
            return valor(7 - j, i)
        if giro == 180:
            return valor(7 - i, 7 - j)
        if giro == 270:
            return valor(j, 7 - i)
        return valor(i, j)
    return nuevo


def transformar_estado(estado, giro, espejo=False):
    v = transformar_rejilla(lambda i, j: estado[nombre(i, j)], giro, espejo)
    return {nombre(i, j): v(i, j) for i in range(8) for j in range(8)}


def puntaje_reyes(estado):
    """(reyes y damas como en la posición normal, reyes y damas cambiados de columna)."""
    normal = sum([estado["E1"]["clase"] == "rey", estado["E8"]["clase"] == "rey",
                  estado["D1"]["clase"] == "reina", estado["D8"]["clase"] == "reina"])
    cambiado = sum([estado["D1"]["clase"] == "rey", estado["D8"]["clase"] == "rey",
                    estado["E1"]["clase"] == "reina", estado["E8"]["clase"] == "reina"])
    return normal, cambiado


def brillo_piezas(tablero_margen, tam, margen):
    """Brillo medio del centro de cada casilla (8x8), en el tablero enderezado."""
    pad = int(round(margen * tam))
    gris = cv2.cvtColor(tablero_margen, cv2.COLOR_BGR2GRAY).astype(np.float32)
    m = int(tam * 0.3)
    return np.array([[gris[pad + i * tam + m:pad + (i + 1) * tam - m, pad + j * tam + m:pad + (j + 1) * tam - m].mean()
                      for j in range(8)] for i in range(8)])


def decidir_orientacion(estado, brillo, umbral_brillo=8.0):
    """
    Busca el giro (0/90/180/270) y el espejo que dejan la posición inicial bien puesta:
      - la ocupación (filas 1, 2, 7 y 8 llenas) decide entre 0/180 y 90/270,
      - el brillo de las piezas decide de qué lado están las blancas (abajo),
      - el rey en la columna E (y la dama en la D) decide si la imagen está espejada.
    Devuelve (giro, espejo, malas, motivo). giro None si el tablero no está en la posición inicial.
    """
    opciones = []
    for g in (0, 90, 180, 270):
        e = transformar_estado(estado, g)
        opciones.append((len(malas_ocupacion(e)), g))
    opciones.sort()
    mejor_malas = opciones[0][0]
    if mejor_malas > 6:
        return None, False, mejor_malas, "el tablero no está en la posición inicial"
    empatadas = [g for m, g in opciones if m == mejor_malas]
    giro = empatadas[0]
    motivo = "ocupación"
    if len(empatadas) > 1:
        dif = {}
        for g in empatadas:
            b = transformar_rejilla(lambda i, j: brillo[i][j], g, False)
            abajo = np.mean([b(i, j) for i in (6, 7) for j in range(8)])
            arriba = np.mean([b(i, j) for i in (0, 1) for j in range(8)])
            dif[g] = abajo - arriba
        candidato = max(empatadas, key=lambda g: dif[g])
        if dif[candidato] >= umbral_brillo:
            giro, motivo = candidato, "blancas abajo (brillo)"
        else:
            giro = 0 if 0 in empatadas else empatadas[0]
            motivo = "brillo dudoso: se deja como está"
    e = transformar_estado(estado, giro)
    normal, cambiado = puntaje_reyes(e)
    espejo = cambiado > normal
    if espejo:
        motivo += " + espejo (rey en la columna D)"
    return giro, espejo, mejor_malas, motivo


# =====================================================================
#  DETECTOR DE LA ENTREGA (la CNN entrenada)
# =====================================================================
def buscar_carpeta_modelo(sugerida=None):
    candidatas = [sugerida] if sugerida else []
    candidatas += [os.path.join(AQUI, "entrega_unity"), AQUI, os.path.join(os.path.dirname(AQUI), "entrega_unity")]
    for c in candidatas:
        if c and os.path.isfile(os.path.join(c, "detector_ajedrez.py")) and os.path.isfile(os.path.join(c, "modelo_ajedrez.pth")):
            return os.path.abspath(c)
    raise FileNotFoundError("No encontré detector_ajedrez.py + modelo_ajedrez.pth. Usa --modelo CARPETA (la de entrega_unity).")


def importar_detector(carpeta):
    if carpeta not in sys.path:
        sys.path.insert(0, carpeta)
    import detector_ajedrez as det
    return det


# =====================================================================
#  CALIBRACIÓN: dónde está el tablero en la imagen (homografía)
# =====================================================================
class Calibracion:
    """4 esquinas del tablero + giro + espejo. Convierte píxeles <-> tablero.

    Coordenadas del tablero: (x, y) de 0 a 1 con origen en la esquina de a1
    (x hacia la columna h, y hacia la fila 8). Es lo que entiende Unity.
    """

    def __init__(self, det, esq, escala, giro, puntaje, forma, espejo=False):
        self.det = det
        self.esq = np.asarray(esq, np.float32)        # en la imagen preprocesada
        self.escala = float(escala)                    # imagen preprocesada / imagen original
        self.giro = int(giro) % 360
        self.espejo = bool(espejo)
        self.puntaje = float(puntaje)
        self.forma = forma
        M, self.lado = det.matriz_alineado(self.esq, 0.0)
        self.M = (M @ np.diag([self.escala, self.escala, 1.0])).astype(np.float64)   # píxel original -> tablero
        self.Minv = np.linalg.inv(self.M)
        self.tam_mov = 160
        k = self.tam_mov / self.lado
        self.M_mov = np.diag([k, k, 1.0]) @ self.M

    @staticmethod
    def buscar(det, frame, giro_extra=0):
        """Igual que detectar_tablero() de la entrega, pero guarda las esquinas."""
        img = det.preprocesar(frame)
        escala = img.shape[1] / frame.shape[1]
        candidatos = det.candidatos_contornos(img) + det.candidato_esquinas_internas(img)
        mejor_p, mejor_e = 0.0, None
        for esq in candidatos:
            esq = det.refinar_cuadricula(img, esq)
            p = det.puntaje_cuadricula(det.alinear(img, esq))
            if p > mejor_p:
                mejor_p, mejor_e = p, esq
        if mejor_e is None or mejor_p < det.UMBRAL_CUADRICULA:
            return None, mejor_p
        tablero = det.alinear(img, mejor_e)
        giro = 90 if np.corrcoef(det.brillo_casillas(tablero).ravel(), det.PATRON.ravel())[0, 1] < 0 else 0
        giro = (giro + det.ROTACION_EXTRA + giro_extra) % 360
        return Calibracion(det, mejor_e, escala, giro, mejor_p, frame.shape[:2]), mejor_p

    def componer(self, giro, espejo):
        """Nueva calibración = esta vista girada 'giro' (horario) y luego espejada si 'espejo'."""
        if self.espejo:
            return Calibracion(self.det, self.esq, self.escala, self.giro - giro, self.puntaje, self.forma, not espejo)
        return Calibracion(self.det, self.esq, self.escala, self.giro + giro, self.puntaje, self.forma, espejo)

    def _rotar(self, u, v):
        L, g = self.lado, self.giro
        if g == 90:
            x, y = L - v, u
        elif g == 180:
            x, y = L - u, L - v
        elif g == 270:
            x, y = v, L - u
        else:
            x, y = u, v
        return (L - x, y) if self.espejo else (x, y)

    def _desrotar(self, x, y):
        L, g = self.lado, self.giro
        if self.espejo:
            x = L - x
        if g == 90:
            return y, L - x
        if g == 180:
            return L - x, L - y
        if g == 270:
            return L - y, x
        return x, y

    def pixel_a_tablero(self, px, py):
        u, v = cv2.perspectiveTransform(np.array([[[px, py]]], np.float64), self.M)[0, 0]
        x, y = self._rotar(u, v)
        return float(x / self.lado), float(1.0 - y / self.lado)

    def tablero_a_pixel(self, bx, by):
        u, v = self._desrotar(bx * self.lado, (1.0 - by) * self.lado)
        p = cv2.perspectiveTransform(np.array([[[u, v]]], np.float64), self.Minv)[0, 0]
        return float(p[0]), float(p[1])

    @staticmethod
    def casilla(bx, by):
        if 0.0 <= bx < 1.0 and 0.0 <= by < 1.0:
            return COLUMNAS[int(bx * 8)] + str(int(by * 8) + 1)
        return None

    def contorno(self, margen=0.0):
        m = margen
        return np.array([self.tablero_a_pixel(x, y) for x, y in [(-m, -m), (1 + m, -m), (1 + m, 1 + m), (-m, 1 + m)]], np.float32)

    def dentro(self, px, py, margen=0.12):
        return cv2.pointPolygonTest(self.contorno(margen).reshape(-1, 1, 2), (float(px), float(py)), False) >= 0

    def tablero_alineado(self, frame):
        """Lo que ve la red: tablero enderezado con margen, girado y (si hace falta) espejado."""
        img = self.det.preprocesar(frame)
        t = self.det.girar(self.det.alinear(img, self.esq, self.det.MARGEN_RECORTE), self.giro)
        return cv2.flip(t, 1) if self.espejo else t

    def vista_movimiento(self, frame):
        gris = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        peq = cv2.warpPerspective(gris, self.M_mov, (self.tam_mov, self.tam_mov), borderMode=cv2.BORDER_REPLICATE)
        return cv2.GaussianBlur(peq, (5, 5), 0)

    def sigue_valida(self, frame):
        img = self.det.preprocesar(frame)
        return self.det.puntaje_cuadricula(self.det.alinear(img, self.esq))


# =====================================================================
#  CLASIFICADOR: la CNN cargada UNA vez
# =====================================================================
class Clasificador:
    def __init__(self, det, carpeta):
        import torch
        from PIL import Image
        self.torch, self.Image, self.det = torch, Image, det
        self.m, self.tf, self.clases = det.cargar_modelo(carpeta)

    def estado(self, tableros):
        """Varios tableros alineados (cuadros seguidos) -> {casilla: {clase, prob}} promediando."""
        torch, det = self.torch, self.det
        nombres, lotes = None, []
        for t in tableros:
            recortes = det.recortar_casillas(t)
            nombres = list(recortes)
            lotes += [self.tf(self.Image.fromarray(cv2.cvtColor(recortes[c], cv2.COLOR_BGR2RGB))) for c in nombres]
        with torch.no_grad():
            p = torch.softmax(self.m(torch.stack(lotes).to(det.device)), 1).cpu().numpy()
        p = p.reshape(len(tableros), 64, -1).mean(0)
        return {c: {"clase": self.clases[int(p[k].argmax())], "prob": float(p[k].max())} for k, c in enumerate(nombres)}


# =====================================================================
#  MANO: MediaPipe Hands
# =====================================================================
class Mano:
    def __init__(self, puntos, punto, pinza, cerrada, forma):
        self.puntos, self.punto, self.pinza, self.cerrada = puntos, punto, pinza, cerrada
        self.forma = forma


class SeguidorMano:
    """Pinza = distancia pulgar-índice / tamaño de la mano (no depende de la distancia a la cámara)."""

    CONEXIONES = [(0, 1), (1, 2), (2, 3), (3, 4), (0, 5), (5, 6), (6, 7), (7, 8), (5, 9), (9, 10), (10, 11), (11, 12),
                  (9, 13), (13, 14), (14, 15), (15, 16), (13, 17), (17, 18), (18, 19), (19, 20), (0, 17)]

    def __init__(self, cerrar=0.30, abrir=0.42, ancho_proceso=640, memoria=0.35):
        import mediapipe as mp
        if not hasattr(mp, "solutions"):
            raise ImportError("Esta versión de mediapipe no trae 'solutions'. Instala:  pip install mediapipe==0.10.21")
        self.hands = mp.solutions.hands.Hands(static_image_mode=False, max_num_hands=1, model_complexity=0,
                                              min_detection_confidence=0.6, min_tracking_confidence=0.5)
        self.cerrar, self.abrir, self.ancho_proceso, self.memoria = cerrar, abrir, ancho_proceso, memoria
        self.cerrada = False
        self.ultima, self.t_ultima = None, 0.0

    def procesar(self, frame, t):
        h, w = frame.shape[:2]
        k = min(1.0, self.ancho_proceso / w)
        peq = cv2.resize(frame, (int(w * k), int(h * k))) if k < 1.0 else frame
        res = self.hands.process(cv2.cvtColor(peq, cv2.COLOR_BGR2RGB))
        if not res.multi_hand_landmarks:
            if self.ultima is not None and t - self.t_ultima < self.memoria:
                return self.ultima               # la mano cerrada a veces "desaparece" unos cuadros
            self.cerrada, self.ultima = False, None
            return None
        lm = res.multi_hand_landmarks[0].landmark
        pts = np.array([[p.x * w, p.y * h] for p in lm], np.float32)
        tam = float(np.linalg.norm(pts[0] - pts[9])) + 1e-6
        pinza = float(np.linalg.norm(pts[4] - pts[8])) / tam
        if self.cerrada and pinza > self.abrir:          # histéresis: no parpadea
            self.cerrada = False
        elif not self.cerrada and pinza < self.cerrar:
            self.cerrada = True
        self.ultima = Mano(pts, (pts[4] + pts[8]) / 2.0, pinza, self.cerrada, (h, w))
        self.t_ultima = t
        return self.ultima


# =====================================================================
#  MÁQUINA DE ESTADOS: cuándo leer el tablero en el juego
# =====================================================================
class MaquinaJugadas:
    """LISTO (quieto, estado conocido) -> OCUPADO (mano o movimiento) -> ASENTANDO (quieto 'quieto' s) -> leer."""
    LISTO, OCUPADO, ASENTANDO, LEYENDO = "LISTO", "OCUPADO", "ASENTANDO", "LEYENDO"

    def __init__(self, quieto=0.6, umbral_mov=0.015):
        self.quieto, self.umbral = quieto, umbral_mov
        self.estado = self.OCUPADO
        self.t_quieto = None
        self.hubo_mano = False

    def actualizar(self, t, mano_encima, movimiento):
        hay_actividad = mano_encima or movimiento > self.umbral
        if mano_encima:
            self.hubo_mano = True
        if self.estado == self.LEYENDO:          # la red está leyendo: se espera el resultado
            return None
        if self.estado == self.LISTO:
            if hay_actividad:
                self.estado = self.OCUPADO
            return None
        if hay_actividad:
            self.estado, self.t_quieto = self.OCUPADO, None
            return None
        if self.estado == self.OCUPADO:
            self.estado, self.t_quieto = self.ASENTANDO, t
        if t - self.t_quieto >= self.quieto:
            return "leer"
        return None

    def progreso(self, t):
        return 0.0 if self.t_quieto is None else min(1.0, (t - self.t_quieto) / self.quieto)

    def leido(self):
        self.estado, self.t_quieto, self.hubo_mano = self.LISTO, None, False

    def forzar_lectura(self):
        self.estado, self.t_quieto = self.OCUPADO, None


def medir_movimiento(prev, actual, umbral_pixel=22):
    """Fracción de píxeles de la vista pequeña del tablero que cambiaron entre dos cuadros."""
    if prev is None:
        return 1.0
    return float(np.count_nonzero(cv2.absdiff(prev, actual) > umbral_pixel)) / actual.size


# =====================================================================
#  CÁMARA: un hilo lee sin parar y siempre se usa el último cuadro
# =====================================================================
class Camara:
    def __init__(self, indice=0, ancho=1280, alto=720, video=None):
        self.indice, self.ancho, self.alto, self.video = indice, ancho, alto, video
        self.cerrojo = threading.Lock()
        self.frame, self.numero, self.error = None, 0, ""
        self.fin_video = False
        self.corriendo = True
        self.abriendo = True
        self.motor = ""
        self.aviso = ""                   # mensaje corto para Unity (cambio de cámara)
        self.negra = False                # la imagen llega negra (cámara tapada o muy oscura)
        self.fps = 0.0                    # imágenes por segundo que entrega la cámara
        self.hilo = threading.Thread(target=self._bucle, daemon=True, name="camara")
        self.hilo.start()

    FPS_OBJETIVO = 15.0

    def _abrir(self):
        if self.video:
            return cv2.VideoCapture(self.video)
        # En Windows se prueban varias combinaciones de "motor", formato y resolución:
        #  - algunas webcams abren con DirectShow + MJPG pero dan imagen NEGRA a ~1 cuadro/s;
        #  - otras sin compresión (YUY2) a 1280x720 solo dan 3-5 cuadros/s por el ancho de banda USB.
        # Se acepta la primera que dé imagen con contenido y al menos FPS_OBJETIVO cuadros/s.
        if os.name == "nt":
            opciones = [("DSHOW+MJPG", cv2.CAP_DSHOW, True), ("MSMF+MJPG", cv2.CAP_MSMF, True),
                        ("DSHOW", cv2.CAP_DSHOW, False), ("MSMF", cv2.CAP_MSMF, False),
                        ("AUTO", cv2.CAP_ANY, False)]
        else:
            opciones = [("AUTO", cv2.CAP_ANY, False)]
        resoluciones = [(self.ancho, self.alto)] + [r for r in ((960, 540), (640, 480)) if r[0] < self.ancho]
        motores = {n: (m, j) for n, m, j in opciones}

        # 1) lo que funcionó la última vez con esta cámara (camara_ok.json)
        recordada = self._recordar().get(str(self.indice))
        if isinstance(recordada, dict) and recordada.get("motor") in motores:
            m, j = motores[recordada["motor"]]
            r = self._probar(recordada["motor"], m, j, int(recordada.get("ancho", self.ancho)),
                             int(recordada.get("alto", self.alto)))
            esperado = min(self.FPS_OBJETIVO, float(recordada.get("fps", self.FPS_OBJETIVO)))
            if isinstance(r, tuple) and r[1] >= esperado * 0.8:
                return self._aceptar(r[0], recordada["motor"], r[1])
            if isinstance(r, tuple):
                r[0].release()

        # 2) probar todo hasta llegar a FPS_OBJETIVO
        mejor, reserva = None, None
        for nombre, motor, mjpg in opciones:
            for ancho, alto in resoluciones:
                if not self.corriendo:
                    return cv2.VideoCapture()
                r = self._probar(nombre, motor, mjpg, ancho, alto)
                if r is None:                    # este motor no abre la cámara
                    break
                if r is True:                    # abre pero da negro: otro motor
                    reserva = reserva or (nombre, motor, mjpg, ancho, alto)
                    break
                cap, fps = r
                if fps >= self.FPS_OBJETIVO:
                    self._recordar({"motor": nombre, "ancho": ancho, "alto": alto, "fps": round(fps, 1)})
                    return self._aceptar(cap, nombre, fps)
                cap.release()
                if mejor is None or fps > mejor[0]:
                    mejor = (fps, (nombre, motor, mjpg, ancho, alto))

        # 3) ninguna llega al objetivo: la más rápida, o si todas son negras la primera (tapada/oscura)
        for elegida, negra in ((mejor[1] if mejor else None, False), (reserva, True)):
            if elegida is None or not self.corriendo:
                continue
            r = self._probar(*elegida, aceptar_negra=True)
            if isinstance(r, tuple):
                if not negra:
                    self._recordar({"motor": elegida[0], "ancho": elegida[3], "alto": elegida[4],
                                    "fps": round(r[1], 1)})
                cap = self._aceptar(r[0], elegida[0], r[1])
                self.negra = negra
                if not negra:
                    self.aviso = "La cámara solo da %.0f imágenes/s (falta luz o es una webcam lenta)" % r[1]
                return cap
        return cv2.VideoCapture()       # cerrada: el bucle avisa del error

    def _aceptar(self, cap, nombre, fps):
        self.motor, self.negra, self.fps = nombre, False, fps
        return cap

    def _recordar(self, config=None):
        ruta = os.path.join(os.path.dirname(os.path.abspath(__file__)), "camara_ok.json")
        try:
            with open(ruta, encoding="utf-8") as f:
                datos = json.load(f)
            if not isinstance(datos, dict):
                datos = {}
        except (OSError, ValueError):
            datos = {}
        if config is not None:
            datos[str(self.indice)] = config
            try:
                with open(ruta, "w", encoding="utf-8") as f:
                    json.dump(datos, f)
            except OSError:
                pass
        return datos

    def _probar(self, nombre, motor, mjpg, ancho, alto, aceptar_negra=False):
        """None = no abrió · True = abrió pero solo da negro · (VideoCapture, fps) = funciona."""
        t0 = time.time()
        print("[ajedrez] abriendo la cámara %d (%s, %dx%d)..." % (self.indice, nombre, ancho, alto), flush=True)
        cap = cv2.VideoCapture(self.indice, motor)
        if not cap.isOpened():
            cap.release()
            return None
        if mjpg:
            cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, ancho)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, alto)
        cap.set(cv2.CAP_PROP_FPS, 30)
        # a) ¿tiene imagen? (hasta 2,5 s: las webcams dan algunos cuadros negros al arrancar)
        cuadros, maximo, f, fin = 0, 0, None, time.time() + 2.5
        while time.time() < fin and self.corriendo:
            ok, g = cap.read()
            if not ok or g is None:
                continue
            f, cuadros = g, cuadros + 1
            maximo = max(maximo, int(g[::8, ::8].max()))
            if maximo > 25 and cuadros >= 3:
                break
        if cuadros == 0:
            cap.release()
            print("[ajedrez]   abrió pero no da imagen", flush=True)
            return None
        if maximo <= 25 and not aceptar_negra:
            cap.release()
            print("[ajedrez]   da imagen negra (%d cuadros)" % cuadros, flush=True)
            return True
        # b) ¿qué tan rápida es? (1,2 s)
        n, t1 = 0, time.time()
        while time.time() - t1 < 1.2 and self.corriendo:
            ok, g = cap.read()
            if ok and g is not None:
                n, f = n + 1, g
        fps = n / max(1e-3, time.time() - t1)
        print("[ajedrez]   %s %dx%d: %.1f imágenes/s%s (%.1f s)" %
              (nombre, f.shape[1], f.shape[0], fps, "" if maximo > 25 else ", IMAGEN NEGRA",
               time.time() - t0), flush=True)
        return cap, fps

    def _bucle(self):
        cap, indice_abierto = None, None
        while self.corriendo:
            if cap is not None and indice_abierto != self.indice:
                # cambio de cámara: la actual sigue abierta hasta que la nueva funcione
                pedida = self.indice
                self.abriendo = True
                nueva = self._abrir()
                self.abriendo = False
                if nueva.isOpened():
                    cap.release()
                    cap, indice_abierto = nueva, pedida
                    self.aviso = "Cámara %d encendida" % pedida
                else:
                    self.indice = indice_abierto
                    self.aviso = "No hay cámara %d: sigo con la %d" % (pedida, indice_abierto)
                    print("[ajedrez] " + self.aviso, flush=True)
                continue
            if cap is None:
                self.abriendo = True
                cap, indice_abierto = self._abrir(), self.indice
                self.abriendo = False
                if not cap.isOpened():
                    self.error = ("No se pudo abrir la cámara %d (¿está conectada o la usa otro programa, "
                                  "como Zoom, Teams u OBS?)" % self.indice)
                    print("[ajedrez] " + self.error, flush=True)
                    cap = None
                    time.sleep(2.0)
                    continue
                self.error = ""
            ok, f = cap.read()
            if not ok:
                if self.video:
                    self.fin_video = True
                    break
                self.error = "La cámara %d no entrega imagen" % self.indice
                cap.release()
                cap = None
                time.sleep(1.0)
                continue
            with self.cerrojo:
                self.frame, self.numero = f, self.numero + 1
            if self.video:
                time.sleep(1 / 30)
            else:
                self._medir_fps()
                if self.numero % 15 == 0:
                    self.negra = int(f[::16, ::16].max()) <= 25
        if cap is not None:
            cap.release()

    def _medir_fps(self):
        # cuántas imágenes por segundo entrega la cámara (sin contar el procesamiento); se anota 3 veces
        t = time.time()
        if not hasattr(self, "_t_fps"):
            self._t_fps, self._n_fps, self._avisos_fps = t, 0, 0
        self._n_fps += 1
        if t - self._t_fps >= 5.0:
            self.fps = self._n_fps / (t - self._t_fps)
            if self._avisos_fps < 3:
                self._avisos_fps += 1
                print("[ajedrez] la cámara entrega %.1f imágenes por segundo" % self.fps, flush=True)
            self._t_fps, self._n_fps = t, 0

    def ultimo(self):
        with self.cerrojo:
            return self.numero, self.frame

    def cambiar(self, indice):
        self.indice = int(indice)

    def cerrar(self):
        self.corriendo = False


# =====================================================================
#  ENLACE CON UNITY (UDP)
# =====================================================================
class EnlaceUnity:
    def __init__(self, host="127.0.0.1", puerto_mano=5065, puerto_jugadas=5066, puerto_estado=5067,
                 puerto_video=5068, puerto_control=5070):
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.mano, self.jugadas = (host, puerto_mano), (host, puerto_jugadas)
        self.estado, self.video = (host, puerto_estado), (host, puerto_video)
        self.n_mano = self.n_jugadas = self.n_video = 0
        self.control = None
        if puerto_control:
            self.control = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            try:
                self.control.bind(("127.0.0.1", puerto_control))
            except OSError:
                raise SystemExit("[ajedrez] El puerto %d ya está en uso: ¿hay otra cámara abierta? Ciérrala primero." % puerto_control)
            self.control.setblocking(False)

    def _enviar(self, datos, destino):
        try:
            self.sock.sendto(datos, destino)
        except OSError:
            pass                      # Unity todavía no escucha: no pasa nada

    def enviar_mano(self, bx, by, cerrada):
        self._enviar(("B;%.4f;%.4f;%d" % (bx, by, 1 if cerrada else 0)).encode("ascii"), self.mano)
        self.n_mano += 1

    def enviar_puntero(self, vx, vy, cerrada):
        """Mano como puntero de pantalla (0..1, origen arriba-izquierda): modo sin tablero físico."""
        self._enviar(("V;%.4f;%.4f;%d" % (vx, vy, 1 if cerrada else 0)).encode("ascii"), self.mano)
        self.n_mano += 1

    def enviar_jugada(self, res):
        d = {k: res.get(k) for k in ("pieza", "origen", "destino", "confianza", "tipo", "observacion")}
        self._enviar(json.dumps(d, ensure_ascii=False).encode("utf-8"), self.jugadas)
        self.n_jugadas += 1
        return d

    def enviar_estado(self, d):
        self._enviar(json.dumps(d, ensure_ascii=False).encode("utf-8"), self.estado)

    def enviar_video(self, jpg):
        if len(jpg) < 64000:
            self._enviar(jpg, self.video)
            self.n_video += 1

    def comandos(self):
        salida = []
        if self.control is None:
            return salida
        while True:
            try:
                datos, _ = self.control.recvfrom(4096)
            except (BlockingIOError, OSError):
                break
            try:
                salida.append(json.loads(datos.decode("utf-8")))
            except ValueError:
                pass
        return salida


# =====================================================================
#  ESTADO QUE SE LE CUENTA A UNITY
# =====================================================================
class EstadoVivo:
    def __init__(self):
        self.modo = "menu"
        self.cam = 0
        self.fps = 0.0
        self.error = ""
        self.cal = False
        self.calidad = 0.0
        self.mano = False
        self.ix = self.iy = self.bx = self.by = 0.0
        self.agarre = False
        self.encima = False
        self.quieto = False
        self.leido = False
        self.ocupacionOk = False
        self.malas = []
        self.dudosas = 0
        self.tiposOk = 0
        self.orientacion = ""
        self.listo = False
        self.listoSeg = 0.0
        self.aviso = "Encendiendo la cámara..."
        self.jugadas = 0
        self.ultimaJugada = ""

    def a_dict(self):
        return {k: v for k, v in self.__dict__.items()}


# =====================================================================
#  DIBUJO (vista previa y ventanas de diagnóstico)
# =====================================================================
def texto(img, s, org, color=BLANCO, escala=0.55, grosor=1):
    cv2.putText(img, s, org, cv2.FONT_HERSHEY_SIMPLEX, escala, (0, 0, 0), grosor + 2, cv2.LINE_AA)
    cv2.putText(img, s, org, cv2.FONT_HERSHEY_SIMPLEX, escala, color, grosor, cv2.LINE_AA)


def dibujar_calibracion(img, cal, color=VERDE):
    for i in range(9):
        a, b = cal.tablero_a_pixel(i / 8, 0), cal.tablero_a_pixel(i / 8, 1)
        c, d = cal.tablero_a_pixel(0, i / 8), cal.tablero_a_pixel(1, i / 8)
        g = 3 if i in (0, 8) else 1
        cv2.line(img, tuple(map(int, a)), tuple(map(int, b)), color, g, cv2.LINE_AA)
        cv2.line(img, tuple(map(int, c)), tuple(map(int, d)), color, g, cv2.LINE_AA)
    for nom, (bx, by) in {"a1": (0.0625, 0.0625), "h1": (0.9375, 0.0625), "a8": (0.0625, 0.9375), "h8": (0.9375, 0.9375)}.items():
        x, y = cal.tablero_a_pixel(bx, by)
        texto(img, nom, (int(x) - 12, int(y) + 6), AMARILLO, 0.7, 2)


def dibujar_marcas(img, cal, estado, malas):
    """Punto verde = casilla bien; X roja = casilla que no coincide con la posición inicial."""
    malas = set(malas)
    for c, e in estado.items():
        bx, by = (COLUMNAS.index(c[0]) + 0.5) / 8, (int(c[1]) - 0.5) / 8
        x, y = map(int, cal.tablero_a_pixel(bx, by))
        if c in malas:
            cv2.line(img, (x - 10, y - 10), (x + 10, y + 10), ROJO, 4, cv2.LINE_AA)
            cv2.line(img, (x - 10, y + 10), (x + 10, y - 10), ROJO, 4, cv2.LINE_AA)
        elif e["clase"] != "vacia":
            cv2.circle(img, (x, y), 6, VERDE, -1, cv2.LINE_AA)


def dibujar_mano(img, mano, cal):
    for a, b in SeguidorMano.CONEXIONES:
        cv2.line(img, tuple(map(int, mano.puntos[a])), tuple(map(int, mano.puntos[b])), CIAN, 2, cv2.LINE_AA)
    px, py = map(int, mano.punto)
    cv2.circle(img, (px, py), 14, ROJO if mano.cerrada else BLANCO, -1 if mano.cerrada else 2, cv2.LINE_AA)
    if cal is not None:
        cas = Calibracion.casilla(*cal.pixel_a_tablero(px, py))
        texto(img, (cas or "fuera") + ("  AGARRANDO" if mano.cerrada else ""), (px + 18, py - 12), ROJO if mano.cerrada else BLANCO, 0.6, 2)


def vista_red(tablero, estado, det, cambios=()):
    t = det.TAM_CASILLA
    pad = int(round(det.MARGEN_RECORTE * t))
    img = tablero[pad:pad + 8 * t, pad:pad + 8 * t].copy()
    cambiadas = {c["casilla"] for c in cambios}
    for i in range(8):
        for j in range(8):
            nom = nombre(i, j)
            x0, y0 = j * t, i * t
            cv2.rectangle(img, (x0, y0), (x0 + t, y0 + t), AMARILLO if nom in cambiadas else (90, 90, 90), 3 if nom in cambiadas else 1)
            if estado:
                e = estado[nom]
                color = VERDE if e["prob"] >= 0.9 else (AMARILLO if e["prob"] >= 0.7 else ROJO)
                texto(img, ABREV.get(e["clase"], "?"), (x0 + 6, y0 + 28), color, 0.8, 2)
                texto(img, "%d%%" % round(100 * e["prob"]), (x0 + 6, y0 + t - 8), color, 0.45)
            texto(img, nom.lower(), (x0 + t - 26, y0 + 16), GRIS, 0.4)
    return cv2.resize(img, (640, 640), interpolation=cv2.INTER_AREA)


def resumen_estado(estado):
    if not estado:
        return ""
    clases = [e["clase"] for e in estado.values()]
    dudosas = sum(1 for e in estado.values() if e["prob"] < 0.7)
    return "piezas: %d  vacias: %d  dudosas: %d" % (64 - clases.count("vacia"), clases.count("vacia"), dudosas)


# =====================================================================
#  SESIÓN: junta todo y decide qué hacer en cada cuadro
# =====================================================================
class Sesion:
    def __init__(self, det, clasif, seguidor, enlace, args, sincrono=False):
        self.det, self.clasif, self.seguidor, self.enlace, self.args = det, clasif, seguidor, enlace, args
        self.ev = EstadoVivo()
        self.ev.modo = "menu" if args.unity else "juego"
        self.ev.cam = args.cam
        self.cal = None
        self.maquina = MaquinaJugadas(args.quieto, args.umbral_mov)
        self.base = None                  # estado del tablero en el juego (para comparar jugadas)
        self.estado_tablero = None        # última lectura (calibración / diagnóstico)
        self.tablero_img = None
        self.ultima_res = None
        self.lecturas_ok = 0
        self.t_listo = None
        self.orientado = False
        self.prev_mov, self.mov = None, 1.0
        self.t_quieto_desde = None
        self.t_ultimo_intento = -10.0
        self.t_ultima_lectura = -10.0
        self.t_ultima_revision = 0.0
        self.fallos_revision = 0
        self.t_ultimo_control = time.time()
        self.t_ultimo_video = 0.0
        self.t_prev = None
        self.aviso_fijo, self.aviso_fijo_hasta = "", 0.0
        self.salir = False
        self.trabajo = None
        self.pool = None if sincrono else futuros.ThreadPoolExecutor(max_workers=1)
        self.camara = None
        self.pendiente_juego = False       # entrar al juego cuando acabe la lectura en curso

    # ------------------------------------------------ trabajo pesado (en otro hilo)
    def _lanzar(self, tipo, fn, *a):
        if self.trabajo is not None:
            return False
        if self.pool is None:
            r = fn(*a)
            f = futuros.Future()
            f.set_result(r)
            self.trabajo = (tipo, f)
        else:
            self.trabajo = (tipo, self.pool.submit(fn, *a))
        return True

    def _trabajo_calibrar(self, frame):
        return Calibracion.buscar(self.det, frame, self.args.girar)

    def _trabajo_leer(self, frames, cal, orientar):
        tableros = [cal.tablero_alineado(f) for f in frames]
        estado = self.clasif.estado(tableros)
        # ya no se exige la posición inicial ni se orienta el tablero: se usa tal como se ve
        return cal, estado, tableros[0], "", False

    def _revisar_trabajo(self, t):
        if self.trabajo is None or not self.trabajo[1].done():
            return
        tipo, f = self.trabajo
        self.trabajo = None
        try:
            r = f.result()
        except Exception as e:                       # que un error en la red no tumbe todo
            self.ev.aviso = "Error leyendo el tablero: %s" % e
            return
        if tipo == "calibrar":
            cal, puntaje = r
            self.ev.calidad = round(float(puntaje), 3)
            if cal is not None:
                self.cal, self.prev_mov = cal, None
                self.orientado = False
                self.maquina.forzar_lectura()
                self.ev.aviso = "Tablero encontrado (calidad %.2f)" % puntaje
            else:
                self.ev.aviso = "No veo el tablero completo (calidad %.2f)" % puntaje
        elif tipo in ("leer", "jugada"):
            cal, estado, tablero_img, info, orientar = r
            if self.cal is None:
                return                               # se recalibró mientras tanto
            if orientar:
                self.cal = cal
                self.orientado = True
                if info:
                    self.ev.orientacion = info
            self.tablero_img = tablero_img
            if tipo == "leer":
                self._evaluar_lectura(estado, t)
            else:
                self._evaluar_jugada(estado)

    # ------------------------------------------------ calibración: ¿está todo bien?
    def _evaluar_lectura(self, estado, t):
        self.estado_tablero = estado
        malas = []                                   # sin verificación de la posición inicial
        self.ev.leido = True
        self.ev.malas = malas
        self.ev.ocupacionOk = not malas
        self.ev.dudosas = sum(1 for e in estado.values() if e["prob"] < 0.7)
        self.ev.tiposOk = 64 - self.ev.dudosas
        self.lecturas_ok = self.lecturas_ok + 1 if not malas else 0
        if malas:
            self.orientado = False               # que vuelva a intentar orientarse
            self.ev.aviso = "Revisa: " + ", ".join(malas[:8]) + ("..." if len(malas) > 8 else "")
        else:
            self.ev.aviso = "Tablero leído (%d/64 casillas reconocidas)" % self.ev.tiposOk
        if self.pendiente_juego:
            self._entrar_juego(True)

    def _actualizar_listo(self, t):
        bien = (self.cal is not None and self.ev.quieto and not self.ev.encima and self.ev.ocupacionOk
                and self.lecturas_ok >= 2 and self.ev.calidad >= self.args.calidad_minima)
        if bien:
            if self.t_listo is None:
                self.t_listo = t
            self.ev.listoSeg = round(t - self.t_listo, 2)
            self.ev.listo = True
        else:
            self.t_listo, self.ev.listo, self.ev.listoSeg = None, False, 0.0

    # ------------------------------------------------ juego: jugadas
    def _evaluar_jugada(self, nuevo):
        self.estado_tablero = nuevo
        if self.base is None:
            self.base = nuevo
            self.ev.aviso = "Posición de partida leída: " + resumen_estado(nuevo)
        else:
            res = self.det.comparar_estados(self.base, nuevo)
            if res["tipo"] != "sin_cambios" or self.maquina.hubo_mano:
                d = self.enlace.enviar_jugada(res)
                self.ev.jugadas += 1
                if res["tipo"] != "sin_cambios":
                    self.ev.ultimaJugada = "%s %s-%s" % (d["pieza"], d["origen"], d["destino"])
                    self.ev.aviso = "Jugada: %s (%s, %d%%)" % (self.ev.ultimaJugada, d["tipo"], round(100 * (d["confianza"] or 0)))
            self.base = nuevo
            self.ultima_res = res
        self.maquina.leido()

    def _entrar_juego(self, nueva):
        self.pendiente_juego = False
        self.ev.modo = "juego"
        if nueva or self.base is None:
            self.base = self.estado_tablero
        self.maquina.leido() if self.base is not None else self.maquina.forzar_lectura()
        self.ev.aviso = "¡A jugar!"

    # ------------------------------------------------ control desde Unity
    def _comando(self, c):
        self.t_ultimo_control = time.time()
        cmd = c.get("cmd")
        if cmd == "modo":
            valor = c.get("valor", "menu")
            if valor == self.ev.modo and not (valor == "juego" and c.get("forzar")):
                return                                   # Unity lo repite cada tanto: no reiniciar nada
            if valor == "juego":
                if self.trabajo is not None and self.trabajo[0] == "leer":
                    self.pendiente_juego = True
                    self.ev.modo = "calibrar"
                else:
                    self._entrar_juego(bool(c.get("nueva", True)))
            elif valor in ("menu", "calibrar", "libre"):
                self.ev.modo = valor
                self.lecturas_ok = 0
                self.t_ultima_lectura = -10.0
        elif cmd == "recalibrar":
            self.cal, self.orientado, self.lecturas_ok = None, False, 0
            self.ev.cal, self.ev.leido, self.ev.ocupacionOk, self.ev.malas = False, False, False, []
            self.t_ultimo_intento = -10.0
            self.ev.aviso = "Recalibrando: quita la mano del tablero"
        elif cmd == "girar":
            if self.cal is not None:
                self.cal = self.cal.componer(int(c.get("grados", 90)), False)
                self.lecturas_ok, self.t_ultima_lectura = 0, -10.0
        elif cmd == "espejo":
            if self.cal is not None:
                self.cal = self.cal.componer(0, True)
                self.lecturas_ok, self.t_ultima_lectura = 0, -10.0
        elif cmd == "camara":
            self.ev.cam = int(c.get("indice", 0))
            if hasattr(self, "camara") and self.camara is not None:
                self.camara.cambiar(self.ev.cam)
            self.cal = None
            self.t_ultimo_intento = -10.0
        elif cmd == "salir":
            self.salir = True

    # ------------------------------------------------ un cuadro
    def paso(self, frame, t, frames_extra=None):
        """Procesa un cuadro. frames_extra(n) devuelve n cuadros más (para promediar la lectura)."""
        ev, args = self.ev, self.args
        if self.t_prev is not None:
            ev.fps = round(0.9 * ev.fps + 0.1 / max(1e-3, t - self.t_prev), 1)
        self.t_prev = t
        for c in self.enlace.comandos():
            self._comando(c)
        self._revisar_trabajo(t)

        # -------- mano
        mano = self.seguidor.procesar(frame, t) if self.seguidor else None
        ev.mano = mano is not None
        ev.encima = False
        if mano is not None:
            h, w = frame.shape[:2]
            ev.ix, ev.iy = round(float(mano.punto[0] / w), 4), round(float(mano.punto[1] / h), 4)
            ev.agarre = bool(mano.cerrada)
            if self.cal is not None:
                bx, by = self.cal.pixel_a_tablero(*mano.punto)
                ev.bx, ev.by = round(bx, 4), round(by, 4)
                ev.encima = any(self.cal.dentro(x, y) for x, y in mano.puntos[[0, 4, 8, 12, 20]])
                if ev.modo == "juego" and -0.3 < bx < 1.3 and -0.3 < by < 1.3:
                    self.enlace.enviar_mano(bx, by, mano.cerrada)
        else:
            ev.agarre = False

        # -------- modo sin tablero físico: la mano es un puntero (como el control del Wii)
        if ev.modo == "libre":
            if mano is not None:
                vx, vy = puntero_desde_imagen(ev.ix, ev.iy, args.zona_mano)
                self.enlace.enviar_puntero(vx, vy, mano.cerrada)
            ev.quieto, ev.listo = False, False
            return self._fin(frame, mano, t)     # sin buscar tablero ni leer con la red: más rápido

        # -------- calibración automática (sin mano encima)
        ev.cal = self.cal is not None
        if self.cal is None and mano is None and t - self.t_ultimo_intento > 1.0:
            if self._lanzar("calibrar", self._trabajo_calibrar, frame.copy()):
                self.t_ultimo_intento = t
        if self.cal is None:
            ev.quieto, ev.listo = False, False
            self.t_listo = None
            return self._fin(frame, mano, t)

        # -------- quietud
        vista = self.cal.vista_movimiento(frame)
        self.mov = medir_movimiento(self.prev_mov, vista)
        self.prev_mov = vista
        activo = ev.encima or self.mov > args.umbral_mov
        if activo:
            self.t_quieto_desde = None
        elif self.t_quieto_desde is None:
            self.t_quieto_desde = t
        ev.quieto = self.t_quieto_desde is not None and t - self.t_quieto_desde >= 0.5

        if ev.modo == "juego":
            accion = self.maquina.actualizar(t, ev.encima, self.mov)
            if self.maquina.estado == MaquinaJugadas.LISTO and t - self.t_ultima_revision > 4.0:
                self.t_ultima_revision = t
                if self.cal.sigue_valida(frame) < self.det.UMBRAL_CUADRICULA:
                    self.fallos_revision += 1
                    if self.fallos_revision >= 2:
                        self.cal, self.fallos_revision = None, 0
                        ev.aviso = "Perdí el tablero (¿se movió la cámara?). Recalibrando..."
                        return self._fin(frame, mano, t)
                else:
                    self.fallos_revision = 0
            if accion == "leer":
                frames = [frame.copy()] + (frames_extra(args.cuadros - 1) if frames_extra else [])
                orientar = self.base is None and not self.orientado     # uso suelto: orientarse en la 1.a lectura
                if self._lanzar("jugada", self._trabajo_leer, frames, self.cal, orientar):
                    self.maquina.estado = MaquinaJugadas.LEYENDO
        else:
            # menú y calibración: leer el tablero de vez en cuando, solo si está quieto
            cada = 0.8 if ev.modo == "calibrar" else 2.5
            if ev.quieto and not ev.encima and t - self.t_ultima_lectura >= cada:
                frames = [frame.copy()] + (frames_extra(args.cuadros - 1) if frames_extra else [])
                if self._lanzar("leer", self._trabajo_leer, frames, self.cal, not self.orientado):
                    self.t_ultima_lectura = t
            if not ev.quieto or ev.encima:
                self.lecturas_ok = 0 if ev.encima else self.lecturas_ok
            self._actualizar_listo(t)
        return self._fin(frame, mano, t)

    def fijar_aviso(self, texto, segundos=3.0):
        self.aviso_fijo, self.aviso_fijo_hasta = texto, time.time() + segundos

    def _fin(self, frame, mano, t):
        ev = self.ev
        ev.cal = self.cal is not None
        if self.camara is not None:
            ev.cam = self.camara.indice
        if time.time() < self.aviso_fijo_hasta:
            ev.aviso = self.aviso_fijo
        elif self.camara is not None and getattr(self.camara, "negra", False):
            ev.aviso = "La cámara da imagen NEGRA: ¿tiene la tapa puesta o está muy oscuro?"
        if self.maquina.estado == MaquinaJugadas.LEYENDO and self.trabajo is None:
            self.maquina.leido()
        self.enlace.enviar_estado(ev.a_dict())
        cada = 1 / 15 if ev.modo == "calibrar" else (1 / 8 if ev.modo == "menu" else 1 / 4)
        if t - self.t_ultimo_video >= cada:
            self.t_ultimo_video = t
            self.enlace.enviar_video(self.vista_previa(frame, mano))
        if self.args.unity and time.time() - self.t_ultimo_control > self.args.espera_unity:
            self.salir = True                        # Unity se cerró: no dejar la cámara encendida
        return mano

    def vista_previa(self, frame, mano, ancho=560):
        vis = frame.copy()
        if self.cal is not None:
            dibujar_calibracion(vis, self.cal, VERDE if self.ev.listo or self.ev.modo == "juego" else AMARILLO)
            if self.ev.modo == "calibrar" and self.estado_tablero is not None:
                dibujar_marcas(vis, self.cal, self.estado_tablero, self.ev.malas)
        if mano is not None:
            dibujar_mano(vis, mano, self.cal)
        h, w = vis.shape[:2]
        k = ancho / w
        vis = cv2.resize(vis, (ancho, int(h * k)), interpolation=cv2.INTER_AREA)
        for q in (70, 55, 40):
            ok, jpg = cv2.imencode(".jpg", vis, [cv2.IMWRITE_JPEG_QUALITY, q])
            if ok and len(jpg) < 60000:
                return jpg.tobytes()
        return b""


# =====================================================================
#  PROGRAMA PRINCIPAL
# =====================================================================
def puntero_desde_imagen(ix, iy, zona=0.1):
    """Imagen (0..1) → pantalla (0..1). Se invierte x como un espejo (la webcam ve a la persona de
    frente) y el recuadro central [zona, 1-zona] cubre toda la pantalla, para no tener que llevar
    la mano hasta el borde de la imagen. Unity usa la misma cuenta para el cursor del menú."""
    def ajustar(v):
        return min(1.0, max(0.0, (v - zona) / (1.0 - 2.0 * zona)))
    return ajustar(1.0 - ix), ajustar(iy)


def diagnostico_camara(indice):
    """Mide cuántas imágenes por segundo da la cámara con cada motor, formato y resolución."""
    def fourcc_txt(v):
        v = int(v)
        return "".join(chr((v >> 8 * i) & 0xFF) for i in range(4)) if v > 0 else "?"
    motores = [("MSMF", cv2.CAP_MSMF), ("DSHOW", cv2.CAP_DSHOW)] if os.name == "nt" else [("AUTO", cv2.CAP_ANY)]
    for nombre, motor in motores:
        for mjpg in (True, False):
            for ancho, alto in ((1280, 720), (960, 540), (640, 480)):
                t0 = time.time()
                cap = cv2.VideoCapture(indice, motor)
                if not cap.isOpened():
                    print("[diag] %-5s %s %dx%d: no abre" % (nombre, "MJPG" if mjpg else "----", ancho, alto), flush=True)
                    continue
                if mjpg:
                    cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))
                cap.set(cv2.CAP_PROP_FRAME_WIDTH, ancho)
                cap.set(cv2.CAP_PROP_FRAME_HEIGHT, alto)
                cap.set(cv2.CAP_PROP_FPS, 30)
                abrir = time.time() - t0
                for _ in range(5):              # calentar (exposición automática)
                    cap.read()
                n, brillo, f, t1 = 0, 0.0, None, time.time()
                while time.time() - t1 < 2.0:
                    ok, g = cap.read()
                    if ok and g is not None:
                        n, f = n + 1, g
                        brillo = float(g[::8, ::8].mean())
                fps = n / (time.time() - t1)
                tam = "%dx%d" % (f.shape[1], f.shape[0]) if f is not None else "-"
                print("[diag] %-5s %s pide %dx%d -> da %s  %5.1f fps  brillo %3.0f  formato %s  exp %s  (abrir %.1f s)" %
                      (nombre, "MJPG" if mjpg else "----", ancho, alto, tam, fps, brillo,
                       fourcc_txt(cap.get(cv2.CAP_PROP_FOURCC)), cap.get(cv2.CAP_PROP_EXPOSURE), abrir), flush=True)
                cap.release()
    print("[diag] fin", flush=True)


def main():
    p = argparse.ArgumentParser(description="Cámara en tiempo real para el Ajedrez sobre Arena")
    p.add_argument("--diagnostico", action="store_true", help="medir fps de la cámara con cada configuración y salir")
    p.add_argument("--unity", action="store_true", help="lo enciende Unity: empieza en el menú y obedece el puerto de control")
    p.add_argument("--ventanas", action="store_true", help="mostrar ventanas de diagnóstico aunque lo encienda Unity")
    p.add_argument("--cam", type=int, default=0, help="índice de la cámara (0, 1, ...)")
    p.add_argument("--video", help="usar un video en vez de la cámara (pruebas)")
    p.add_argument("--ancho", type=int, default=1280)
    p.add_argument("--alto", type=int, default=720)
    p.add_argument("--modelo", help="carpeta entrega_unity (detector_ajedrez.py + modelo_ajedrez.pth)")
    p.add_argument("--host", default="127.0.0.1", help="IP del PC donde corre Unity")
    p.add_argument("--puerto-mano", type=int, default=5065)
    p.add_argument("--puerto-jugadas", type=int, default=5066)
    p.add_argument("--puerto-estado", type=int, default=5067)
    p.add_argument("--puerto-video", type=int, default=5068)
    p.add_argument("--puerto-control", type=int, default=5070)
    p.add_argument("--quieto", type=float, default=0.6, help="segundos de quietud antes de leer una jugada")
    p.add_argument("--zona-mano", type=float, default=0.1,
                   help="sin tablero: margen de la imagen que no se usa (0.1 = el 80%% central cubre la pantalla)")
    p.add_argument("--umbral-mov", type=float, default=0.015, help="fracción de píxeles que cambian = 'hay movimiento'")
    p.add_argument("--cuadros", type=int, default=3, help="cuadros que se promedian en cada lectura de la red")
    p.add_argument("--calidad-minima", type=float, default=0.35, help="calidad de cuadrícula para dar el visto bueno")
    p.add_argument("--pinza-cerrar", type=float, default=0.30)
    p.add_argument("--pinza-abrir", type=float, default=0.42)
    p.add_argument("--girar", type=int, default=0, choices=[0, 90, 180, 270], help="giro extra inicial del tablero")
    p.add_argument("--espera-unity", type=float, default=10.0, help="segundos sin noticias de Unity antes de apagarse")
    p.add_argument("--sin-mano", action="store_true", help="no usar MediaPipe")
    args = p.parse_args()
    if args.diagnostico:
        diagnostico_camara(args.cam)
        return 0
    ventanas = args.ventanas or not args.unity

    enlace = EnlaceUnity(args.host, args.puerto_mano, args.puerto_jugadas, args.puerto_estado, args.puerto_video,
                         args.puerto_control)
    aviso_inicio = EstadoVivo()
    aviso_inicio.aviso = "Cargando la red neuronal..."
    enlace.enviar_estado(aviso_inicio.a_dict())

    carpeta = buscar_carpeta_modelo(args.modelo)
    print("[ajedrez] modelo:", carpeta, flush=True)
    det = importar_detector(carpeta)
    clasif = Clasificador(det, carpeta)
    print("[ajedrez] CNN cargada en", det.device, flush=True)
    seguidor = None
    if not args.sin_mano:
        try:
            seguidor = SeguidorMano(args.pinza_cerrar, args.pinza_abrir)
            print("[ajedrez] MediaPipe Hands listo", flush=True)
        except Exception as e:
            print("[ajedrez] SIN seguimiento de mano:", e, flush=True)

    camara = Camara(args.cam, args.ancho, args.alto, args.video)
    sesion = Sesion(det, clasif, seguidor, enlace, args)
    sesion.camara = camara

    def extra(n):
        salida, ultimo = [], camara.ultimo()[0]
        fin = time.time() + 0.5
        while len(salida) < n and time.time() < fin:
            num, f = camara.ultimo()
            if num != ultimo and f is not None:
                salida.append(f.copy())
                ultimo = num
            else:
                time.sleep(0.005)
        return salida

    visto = 0
    t_aviso = 0.0
    while not sesion.salir:
        if args.unity and time.time() - sesion.t_ultimo_control > args.espera_unity:
            print("[ajedrez] Unity no responde: me apago", flush=True)
            break
        if camara.aviso:
            sesion.fijar_aviso(camara.aviso)
            camara.aviso = ""
        num, frame = camara.ultimo()
        if frame is None or num == visto:
            # Aunque la cámara todavía no dé imagen, se sigue escuchando a Unity y contándole qué pasa.
            for c in enlace.comandos():
                sesion._comando(c)
            if sesion.salir:
                break
            if frame is None and time.time() - t_aviso > 0.2:
                t_aviso = time.time()
                if camara.error:
                    sesion.ev.error = camara.error
                    sesion.ev.aviso = camara.error
                else:
                    sesion.ev.aviso = "Abriendo la cámara %d..." % camara.indice
                enlace.enviar_estado(sesion.ev.a_dict())
            if camara.fin_video:
                break
            time.sleep(0.003)
            continue
        visto = num
        sesion.ev.error = ""
        if sesion.ev.aviso.startswith("Abriendo la cámara") or sesion.ev.aviso.startswith("Cargando"):
            sesion.ev.aviso = ""
        t = time.time()
        mano = sesion.paso(frame, t, extra)

        if ventanas:
            vis = cv2.imdecode(np.frombuffer(sesion.vista_previa(frame, mano, 960), np.uint8), cv2.IMREAD_COLOR)
            if vis is not None:
                ev = sesion.ev
                texto(vis, "%s | %s | %.0f fps" % (ev.modo.upper(), "LISTO" if ev.listo else ("cal" if ev.cal else "sin tablero"), ev.fps), (10, 26), VERDE if ev.cal else ROJO, 0.7, 2)
                texto(vis, ev.aviso[:90], (10, 54), AMARILLO, 0.55)
                cv2.imshow("Ajedrez en vivo", vis)
            if sesion.tablero_img is not None:
                cv2.imshow("Lo que ve la red", vista_red(sesion.tablero_img, sesion.estado_tablero, det,
                                                        sesion.ultima_res["cambios"] if sesion.ultima_res else ()))
            tecla = cv2.waitKey(1) & 0xFF
            if tecla in (ord("q"), 27):
                break
            if tecla == ord("c"):
                sesion._comando({"cmd": "recalibrar"})
            elif tecla == ord("b"):
                sesion.base = None
                sesion.maquina.forzar_lectura()
            elif tecla == ord("l"):
                sesion.t_ultima_lectura = -10.0
            elif tecla == ord("o"):
                sesion.orientado = False
            elif tecla == ord("m"):
                sesion._comando({"cmd": "espejo"})
            elif tecla == ord("g"):
                sesion._comando({"cmd": "girar", "grados": 90})

    camara.cerrar()
    if ventanas:
        cv2.destroyAllWindows()
    print("[ajedrez] fin", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
