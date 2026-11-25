"""
Detector de movimientos de ajedrez - Proyecto Ajedrez PDI

Recibe dos fotos consecutivas del tablero (antes y después de una jugada),
clasifica las 64 casillas de cada una con la CNN entrenada y devuelve en JSON
qué pieza se movió, de dónde a dónde y con qué confianza.

Uso desde la terminal:
    python detector_ajedrez.py foto_antes.jpg foto_despues.jpg
    python detector_ajedrez.py foto_antes.jpg foto_despues.jpg --salida resultado.json
    python detector_ajedrez.py foto_antes.jpg foto_despues.jpg --completo

Por defecto imprime solo {"pieza", "origen", "destino", "confianza"}.
Con --completo agrega el tipo de jugada, la observación y las casillas que cambiaron.

Necesita en la misma carpeta (o en la que indique --modelo):
    modelo_ajedrez.pth   (pesos de la red)
    config_modelo.json   (clases, tamaño y normalización)
"""
import os, sys, json, argparse
import cv2
import numpy as np
import torch
import torch.nn as nn
from PIL import Image
from torchvision import transforms

# ---------------------------------------------------------------- configuración
LADO_MAX = 1600
TAM_CASILLA = 100
MARGEN_RECORTE = 0.15
ROTACION_EXTRA = 0
UMBRAL_CUADRICULA = 0.30
device = "cuda" if torch.cuda.is_available() else "cpu"

PATRON = np.array([[1 if (i + j) % 2 == 0 else -1 for j in range(8)] for i in range(8)], dtype=np.float32)


# ---------------------------------------------------------------- preprocesamiento
def estimar_ruido(gris):
    return float(np.std(gris.astype(np.float32) - cv2.GaussianBlur(gris, (5, 5), 0).astype(np.float32)))

def preprocesar(img, umbral_ruido=8.0, umbral_contraste=40.0):
    h, w = img.shape[:2]
    escala = LADO_MAX / max(h, w)
    if escala < 1:
        img = cv2.resize(img, (int(w * escala), int(h * escala)), interpolation=cv2.INTER_AREA)
    gris = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    if estimar_ruido(gris) > umbral_ruido:
        img = cv2.bilateralFilter(img, 7, 50, 50)
    lab = cv2.cvtColor(img, cv2.COLOR_BGR2LAB)
    L, A, B = cv2.split(lab)
    if L.std() < umbral_contraste:
        L = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8)).apply(L)
        img = cv2.cvtColor(cv2.merge([L, A, B]), cv2.COLOR_LAB2BGR)
    return img


# ---------------------------------------------------------------- detección del tablero
def ordenar_esquinas(pts):
    pts = np.array(pts, dtype=np.float32).reshape(4, 2)
    s, d = pts.sum(1), np.diff(pts, axis=1).ravel()
    return np.array([pts[np.argmin(s)], pts[np.argmin(d)], pts[np.argmax(s)], pts[np.argmax(d)]], dtype=np.float32)

def matriz_alineado(esq, margen=0.0):
    pad = margen * TAM_CASILLA
    lado = 8 * TAM_CASILLA
    destino = np.array([[pad, pad], [pad + lado, pad], [pad + lado, pad + lado], [pad, pad + lado]], dtype=np.float32)
    return cv2.getPerspectiveTransform(esq, destino), int(round(lado + 2 * pad))

def alinear(img, esq, margen=0.0):
    M, tam = matriz_alineado(esq, margen)
    return cv2.warpPerspective(img, M, (tam, tam), borderMode=cv2.BORDER_REPLICATE)

def mejor_rejilla(perfil):
    perfil = np.maximum.reduce([np.roll(perfil, d) for d in (-2, -1, 0, 1, 2)])
    mejor = (-1, 0, TAM_CASILLA)
    for paso in range(int(0.75 * TAM_CASILLA), int(1.25 * TAM_CASILLA) + 1):
        for inicio in range(0, len(perfil) - 8 * paso):
            valor = perfil[inicio:inicio + 8 * paso + 1:paso].sum()
            if valor > mejor[0]:
                mejor = (valor, inicio, paso)
    return mejor[1], mejor[2]

def refinar_cuadricula(img, esq):
    M, tam = matriz_alineado(esq, 1.0)
    grueso = cv2.warpPerspective(img, M, (tam, tam), borderMode=cv2.BORDER_REPLICATE)
    gris = cv2.GaussianBlur(cv2.cvtColor(grueso, cv2.COLOR_BGR2GRAY), (5, 5), 0).astype(np.float32)
    x0, px = mejor_rejilla(np.abs(cv2.Sobel(gris, cv2.CV_32F, 1, 0, ksize=3)).sum(axis=0))
    y0, py = mejor_rejilla(np.abs(cv2.Sobel(gris, cv2.CV_32F, 0, 1, ksize=3)).sum(axis=1))
    nuevas = np.array([[[x0, y0]], [[x0 + 8 * px, y0]], [[x0 + 8 * px, y0 + 8 * py]], [[x0, y0 + 8 * py]]], dtype=np.float32)
    return cv2.perspectiveTransform(nuevas, np.linalg.inv(M)).reshape(4, 2)

def brillo_casillas(tablero):
    gris = cv2.cvtColor(tablero, cv2.COLOR_BGR2GRAY).astype(np.float32)
    t, m = TAM_CASILLA, int(TAM_CASILLA * 0.25)
    return np.array([[gris[i*t + m:(i+1)*t - m, j*t + m:(j+1)*t - m].mean() for j in range(8)] for i in range(8)])

def puntaje_cuadricula(tablero):
    b = brillo_casillas(tablero)
    if b.std() < 1e-6:
        return 0.0
    return float(abs(np.corrcoef(b.ravel(), PATRON.ravel())[0, 1]))

def candidatos_contornos(img):
    gris = cv2.GaussianBlur(cv2.cvtColor(img, cv2.COLOR_BGR2GRAY), (5, 5), 0)
    bordes = cv2.dilate(cv2.Canny(gris, 50, 150), np.ones((3, 3), np.uint8), iterations=2)
    contornos, _ = cv2.findContours(bordes, cv2.RETR_LIST, cv2.CHAIN_APPROX_SIMPLE)
    area_img = img.shape[0] * img.shape[1]
    salida = []
    for c in sorted(contornos, key=cv2.contourArea, reverse=True)[:15]:
        if cv2.contourArea(c) < 0.10 * area_img:
            break
        aprox = cv2.approxPolyDP(c, 0.02 * cv2.arcLength(c, True), True)
        if len(aprox) == 4 and cv2.isContourConvex(aprox):
            salida.append(ordenar_esquinas(aprox))
    return salida

def candidato_esquinas_internas(img):
    gris = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    ok, esquinas = cv2.findChessboardCornersSB(gris, (7, 7), cv2.CALIB_CB_EXHAUSTIVE)
    if not ok:
        return []
    ideal = np.array([[j + 1, i + 1] for i in range(7) for j in range(7)], dtype=np.float32)
    H, _ = cv2.findHomography(ideal, esquinas.reshape(-1, 2), cv2.RANSAC)
    if H is None:
        return []
    externas = cv2.perspectiveTransform(np.array([[[0, 0]], [[8, 0]], [[8, 8]], [[0, 8]]], dtype=np.float32), H)
    return [ordenar_esquinas(externas)]

def girar(img, grados):
    k = {0: None, 90: cv2.ROTATE_90_CLOCKWISE, 180: cv2.ROTATE_180, 270: cv2.ROTATE_90_COUNTERCLOCKWISE}[grados]
    return img if k is None else cv2.rotate(img, k)

def detectar_tablero(img):
    candidatos = candidatos_contornos(img) + candidato_esquinas_internas(img)
    mejor_p, mejor_e = 0.0, None
    for esq in candidatos:
        esq = refinar_cuadricula(img, esq)
        p = puntaje_cuadricula(alinear(img, esq))
        if p > mejor_p:
            mejor_p, mejor_e = p, esq
    if mejor_p < UMBRAL_CUADRICULA:
        return None
    tablero = alinear(img, mejor_e)
    giro = 90 if np.corrcoef(brillo_casillas(tablero).ravel(), PATRON.ravel())[0, 1] < 0 else 0
    giro = (giro + ROTACION_EXTRA) % 360
    return girar(alinear(img, mejor_e, MARGEN_RECORTE), giro)

def nombre_casilla(i, j):
    return "ABCDEFGH"[j] + str(8 - i)

def recortar_casillas(tablero_margen):
    t = TAM_CASILLA
    pad = int(round(MARGEN_RECORTE * t))
    return {nombre_casilla(i, j): tablero_margen[i*t:i*t + t + 2*pad, j*t:j*t + t + 2*pad]
            for i in range(8) for j in range(8)}


# ---------------------------------------------------------------- CNN
class CNNAjedrez(nn.Module):
    def __init__(self, num_clases=7):
        super().__init__()
        def bloque(entrada, salida):
            return nn.Sequential(nn.Conv2d(entrada, salida, 3, padding=1), nn.BatchNorm2d(salida),
                                 nn.ReLU(inplace=True), nn.MaxPool2d(2))
        self.features = nn.Sequential(bloque(3, 32), bloque(32, 64), bloque(64, 128), bloque(128, 256),
                                      nn.AdaptiveAvgPool2d(1))
        self.classifier = nn.Sequential(nn.Flatten(), nn.Dropout(0.5), nn.Linear(256, 128),
                                        nn.ReLU(inplace=True), nn.Dropout(0.3), nn.Linear(128, num_clases))
    def forward(self, x):
        return self.classifier(self.features(x))

def cargar_modelo(carpeta):
    global TAM_CASILLA, MARGEN_RECORTE, ROTACION_EXTRA
    with open(os.path.join(carpeta, "config_modelo.json"), encoding="utf-8") as f:
        cfg = json.load(f)
    TAM_CASILLA = cfg.get("tam_casilla", TAM_CASILLA)
    MARGEN_RECORTE = cfg.get("margen_recorte", MARGEN_RECORTE)
    ROTACION_EXTRA = cfg.get("rotacion_extra", ROTACION_EXTRA)
    m = CNNAjedrez(len(cfg["clases"])).to(device)
    m.load_state_dict(torch.load(os.path.join(carpeta, "modelo_ajedrez.pth"), map_location=device))
    m.eval()
    tf = transforms.Compose([transforms.Resize((cfg["tam_cnn"], cfg["tam_cnn"])), transforms.ToTensor(),
                             transforms.Normalize(cfg["media"], cfg["desviacion"])])
    return m, tf, cfg["clases"]

def leer_estado(ruta, m, tf, clases):
    img = cv2.imread(ruta)
    if img is None:
        raise FileNotFoundError(f"No se pudo abrir la foto: {ruta}")
    tablero = detectar_tablero(preprocesar(img))
    if tablero is None:
        return None
    recortes = recortar_casillas(tablero)
    nombres = list(recortes)
    x = torch.stack([tf(Image.fromarray(cv2.cvtColor(recortes[c], cv2.COLOR_BGR2RGB))) for c in nombres]).to(device)
    with torch.no_grad():
        p = torch.softmax(m(x), 1).cpu().numpy()
    return {c: {"clase": clases[p[k].argmax()], "prob": float(p[k].max())} for k, c in enumerate(nombres)}


# ---------------------------------------------------------------- comparación
def comparar_estados(antes, despues):
    cambios = [{"casilla": c, "antes": antes[c]["clase"], "despues": despues[c]["clase"],
                "prob": round((antes[c]["prob"] + despues[c]["prob"]) / 2, 3)}
               for c in antes if antes[c]["clase"] != despues[c]["clase"]]
    vaciadas = [x for x in cambios if x["despues"] == "vacia"]
    ocupadas = [x for x in cambios if x["despues"] != "vacia"]
    res = {"pieza": None, "origen": None, "destino": None, "confianza": 0.0, "tipo": "no_determinado",
           "observacion": "", "cambios": cambios}
    if cambios:
        res["confianza"] = round(float(np.mean([x["prob"] for x in cambios])), 3)

    if not cambios:
        res["tipo"], res["observacion"] = "sin_cambios", "Las dos fotos tienen el mismo estado"
    elif len(cambios) == 2 and len(vaciadas) == 1 and len(ocupadas) == 1:
        o, d = vaciadas[0], ocupadas[0]
        res.update(pieza=o["antes"], origen=o["casilla"], destino=d["casilla"])
        if d["despues"] == o["antes"]:
            res["tipo"] = "normal" if d["antes"] == "vacia" else "captura"
            if res["tipo"] == "captura":
                res["observacion"] = f"Se capturó un(a) {d['antes']}"
        elif o["antes"] == "peon" and d["casilla"][1] in "18":
            res["tipo"], res["observacion"] = "promocion", f"El peón se convirtió en {d['despues']}"
        else:
            res["tipo"] = "inconsistente"
            res["observacion"] = f"Sale un(a) {o['antes']} y aparece un(a) {d['despues']}: revisar la clasificación"
    elif len(cambios) == 1 and len(vaciadas) == 1:
        res.update(pieza=vaciadas[0]["antes"], origen=vaciadas[0]["casilla"], tipo="captura_misma_clase")
        res["observacion"] = "Captura de una pieza de la misma clase: el destino no se puede identificar sin color"
    elif len(cambios) == 4 and len(vaciadas) == 2 and len(ocupadas) == 2:
        rey_o = [x for x in vaciadas if x["antes"] == "rey"]
        rey_d = [x for x in ocupadas if x["despues"] == "rey"]
        if rey_o and rey_d and len({x["casilla"][1] for x in cambios}) == 1 and \
           abs("ABCDEFGH".index(rey_o[0]["casilla"][0]) - "ABCDEFGH".index(rey_d[0]["casilla"][0])) == 2:
            res.update(pieza="rey", origen=rey_o[0]["casilla"], destino=rey_d[0]["casilla"], tipo="enroque")
            torre_o = [x for x in vaciadas if x["antes"] == "torre"]
            torre_d = [x for x in ocupadas if x["despues"] == "torre"]
            if torre_o and torre_d:
                res["observacion"] = f"Torre de {torre_o[0]['casilla']} a {torre_d[0]['casilla']}"
    elif len(cambios) == 3 and len(vaciadas) == 2 and len(ocupadas) == 1 \
            and all(x["antes"] == "peon" for x in vaciadas) and ocupadas[0]["despues"] == "peon":
        d = ocupadas[0]
        o = [x for x in vaciadas if x["casilla"][0] != d["casilla"][0]]
        o = o[0] if o else vaciadas[0]
        capturado = [x for x in vaciadas if x is not o][0]
        res.update(pieza="peon", origen=o["casilla"], destino=d["casilla"], tipo="captura_al_paso",
                   observacion=f"Se capturó el peón de {capturado['casilla']}")
    if res["tipo"] == "no_determinado":
        res["observacion"] = f"{len(cambios)} casillas cambiaron y no corresponden a un movimiento conocido"
    return res


def detectar_movimiento(ruta_antes, ruta_despues, carpeta_modelo):
    m, tf, clases = cargar_modelo(carpeta_modelo)
    a = leer_estado(ruta_antes, m, tf, clases)
    b = leer_estado(ruta_despues, m, tf, clases)
    if a is None or b is None:
        cual = [os.path.basename(r) for r, e in [(ruta_antes, a), (ruta_despues, b)] if e is None]
        return {"pieza": None, "origen": None, "destino": None, "confianza": 0.0, "tipo": "error",
                "observacion": f"No se detectó el tablero en: {cual}", "cambios": []}
    return comparar_estados(a, b)


if __name__ == "__main__":
    p = argparse.ArgumentParser(description="Detecta la jugada entre dos fotos del tablero")
    p.add_argument("foto_antes")
    p.add_argument("foto_despues")
    p.add_argument("--modelo", default=os.path.dirname(os.path.abspath(__file__)),
                   help="carpeta con modelo_ajedrez.pth y config_modelo.json (por defecto, la del script)")
    p.add_argument("--salida", help="ruta de un archivo .json donde guardar el resultado")
    p.add_argument("--completo", action="store_true", help="incluir tipo, observación y casillas que cambiaron")
    args = p.parse_args()

    try:
        res = detectar_movimiento(args.foto_antes, args.foto_despues, args.modelo)
        codigo = 0 if res["tipo"] != "error" else 2
    except Exception as e:
        res = {"pieza": None, "origen": None, "destino": None, "confianza": 0.0, "tipo": "error",
               "observacion": f"{type(e).__name__}: {e}", "cambios": []}
        codigo = 1

    salida = res if args.completo else {k: res[k] for k in ["pieza", "origen", "destino", "confianza"]}
    texto = json.dumps(salida, ensure_ascii=False)
    if args.salida:
        with open(args.salida, "w", encoding="utf-8") as f:
            f.write(texto)
    print(texto)
    sys.exit(codigo)
