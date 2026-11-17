#!/usr/bin/env python3
"""Genera Assets/app.ico (multi-risoluzione 16..256) per VHD ISO Studio.

Disegno: quadrato arrotondato con gradiente indaco->ciano, un disco ottico
stilizzato (ISO) e una piccola "unità" (VHD) in basso a destra.
Uso: python tools/make_icon.py  (richiede Pillow)
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

S = 1024  # disegno in alta risoluzione e poi ridimensiono (antialiasing)
OUT = Path(__file__).resolve().parent.parent / "src" / "VhdIsoStudio" / "Assets" / "app.ico"


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def build() -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    # sfondo gradiente diagonale
    grad = Image.new("RGBA", (S, S))
    gp = grad.load()
    c1, c2 = (79, 70, 229), (6, 182, 212)  # indigo-600 -> cyan-500
    for y in range(S):
        for x in range(S):
            gp[x, y] = (*lerp(c1, c2, (x + y) / (2 * S)), 255)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((40, 40, S - 40, S - 40), radius=220, fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    # disco ottico
    cx, cy, r = 470, 470, 330
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(255, 255, 255, 240))
    for rr, col in ((300, (224, 231, 255, 255)), (250, (199, 210, 254, 255)), (200, (224, 242, 254, 255))):
        d.ellipse((cx - rr, cy - rr, cx + rr, cy + rr), outline=col, width=10)
    d.ellipse((cx - 95, cy - 95, cx + 95, cy + 95), fill=(79, 70, 229, 255))
    d.ellipse((cx - 45, cy - 45, cx + 45, cy + 45), fill=(255, 255, 255, 255))
    # riflesso
    d.pieslice((cx - 280, cy - 280, cx + 280, cy + 280), 200, 245, fill=(255, 255, 255, 90))

    # "unità" VHD con ombra
    sh = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle((560, 640, 930, 900), radius=60, fill=(15, 23, 42, 140))
    img.alpha_composite(sh.filter(ImageFilter.GaussianBlur(18)))
    d.rounded_rectangle((545, 615, 915, 875), radius=60, fill=(15, 23, 42, 255))
    d.rounded_rectangle((545, 615, 915, 875), radius=60, outline=(255, 255, 255, 255), width=14)
    d.rounded_rectangle((605, 790, 855, 815), radius=12, fill=(6, 182, 212, 255))
    d.ellipse((610, 680, 660, 730), fill=(34, 197, 94, 255))
    d.rounded_rectangle((690, 690, 855, 720), radius=12, fill=(148, 163, 184, 255))
    return img


def main() -> None:
    OUT.parent.mkdir(parents=True, exist_ok=True)
    base = build()
    sizes = [(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)]
    base.save(OUT, format="ICO", sizes=sizes)
    base.resize((256, 256), Image.LANCZOS).save(OUT.with_name("app.png"))
    print(f"Icona scritta: {OUT}")


if __name__ == "__main__":
    main()
