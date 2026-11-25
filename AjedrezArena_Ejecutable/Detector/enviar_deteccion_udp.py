"""
========================================================================
  Ajedrez sobre Arena - Puente Python -> Unity para las JUGADAS (UDP)
=====================================================================
Envía a Unity la jugada que detectó la cámara. En Unity activa
"Usar Deteccion Camara" en el componente ArranqueAjedrez (puerto 5066).

Unity valida la jugada con las reglas del ajedrez antes de ejecutarla: si la
detección no es una jugada legal, NO se mueve nada y se muestra el motivo.

DESDE EL CUADERNILLO (lo más simple)
    from enviar_deteccion_udp import EmisorDeteccion
    emisor = EmisorDeteccion()                      # Unity en este mismo PC
    emisor.enviar(resultado)                        # resultado = dict que devuelve el detector
    # o directamente desde dos fotos:
    emisor.detectar_y_enviar("antes.jpg", "despues.jpg", carpeta_entrega="C:/.../entrega_unity")

DESDE LA TERMINAL
    python enviar_deteccion_udp.py fotos antes.jpg despues.jpg --entrega C:/.../entrega_unity
    python enviar_deteccion_udp.py jugada e2e4          # prueba SIN cámara (confianza 1)
    python enviar_deteccion_udp.py jugada e2e4 --confianza 0.5   # fuerza la confirmación (Enter)
    python enviar_deteccion_udp.py partida              # juega la Ópera de Morphy completa, sin cámara

FORMATOS QUE ENTIENDE UNITY (un paquete por jugada)
    {"pieza":"caballo","origen":"B1","destino":"C3","confianza":0.97}      <- salida del detector
    M;caballo;B1;C3;0.97
    e2e4        (o e7e8q para coronar)

Si Unity corre en OTRO equipo, usa host="IP_DEL_PC_DE_UNITY".
Requisitos: solo la librería estándar. (Para el modo "fotos" hace falta el detector: torch, opencv, ...)
"""
import argparse
import json
import os
import socket
import sys
import time

OPERA_MORPHY = [
    "e2e4", "e7e5", "g1f3", "d7d6", "d2d4", "c8g4", "d4e5", "g4f3", "d1f3", "d6e5",
    "f1c4", "g8f6", "f3b3", "d8e7", "b1c3", "c7c6", "c1g5", "b7b5", "c3b5", "c6b5",
    "c4b5", "b8d7", "e1c1", "a8d8", "d1d7", "d8d7", "h1d1", "e7e6", "b5d7", "f6d7",
    "b3b8", "d7b8", "d1d8",
]


class EmisorDeteccion:
    def __init__(self, host="127.0.0.1", puerto=5066):
        self.destino = (host, puerto)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    def enviar(self, resultado):
        """resultado: dict del detector (o un str con alguno de los formatos de arriba)."""
        texto = resultado if isinstance(resultado, str) else json.dumps(resultado, ensure_ascii=False)
        self.sock.sendto(texto.encode("utf-8"), self.destino)
        return texto

    def detectar_y_enviar(self, foto_antes, foto_despues, carpeta_entrega, completo=True):
        """Corre el detector de la entrega sobre dos fotos y manda el resultado a Unity."""
        carpeta_entrega = os.path.abspath(carpeta_entrega)
        sys.path.insert(0, carpeta_entrega)
        import detector_ajedrez as det          # el de la entrega (necesita torch, opencv, ...)
        res = det.detectar_movimiento(foto_antes, foto_despues, carpeta_entrega)
        if not completo:
            res = {k: res[k] for k in ["pieza", "origen", "destino", "confianza"]}
        self.enviar(res)
        return res


def main():
    p = argparse.ArgumentParser(description="Envía jugadas detectadas a Unity por UDP")
    p.add_argument("--host", default="127.0.0.1")
    p.add_argument("--puerto", type=int, default=5066)
    sub = p.add_subparsers(dest="modo", required=True)

    f = sub.add_parser("fotos", help="detecta la jugada entre dos fotos y la envía")
    f.add_argument("foto_antes")
    f.add_argument("foto_despues")
    f.add_argument("--entrega", required=True, help="carpeta entrega_unity (con detector_ajedrez.py)")

    j = sub.add_parser("jugada", help="envía una jugada escrita a mano, p. ej. e2e4")
    j.add_argument("jugada")
    j.add_argument("--confianza", type=float, default=1.0)

    s = sub.add_parser("partida", help="juega la Ópera de Morphy (17 jugadas) sin cámara")
    s.add_argument("--pausa", type=float, default=3.0, help="segundos entre jugadas")

    a = p.parse_args()
    emisor = EmisorDeteccion(a.host, a.puerto)

    if a.modo == "fotos":
        print(json.dumps(emisor.detectar_y_enviar(a.foto_antes, a.foto_despues, a.entrega), ensure_ascii=False))
    elif a.modo == "jugada":
        m = a.jugada.strip().lower()
        d = {"pieza": None, "origen": m[0:2].upper(), "destino": m[2:4].upper(), "confianza": a.confianza}
        if len(m) > 4:
            d["coronacion"] = {"q": "reina", "r": "torre", "b": "alfil", "n": "caballo"}.get(m[4])
        print(emisor.enviar(d))
    else:
        for i, m in enumerate(OPERA_MORPHY, 1):
            d = {"pieza": None, "origen": m[0:2].upper(), "destino": m[2:4].upper(), "confianza": 0.95}
            print(i, emisor.enviar(d))
            time.sleep(a.pausa)


if __name__ == "__main__":
    main()
