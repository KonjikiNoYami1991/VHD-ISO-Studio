#!/usr/bin/env python3
"""Genera docs/images/social-preview.png (1280x640, formato social preview di GitHub).

Usa il logo dell'app e, se presente, uno screenshot reale (docs/images/auto-dark-it.png).
Uso: python tools/make_social.py   (richiede Pillow e il font Inter, con ripiego su DejaVu)
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
IMG = ROOT / "docs" / "images"
OUT = IMG / "social-preview.png"
W, H = 1280, 640


def font(names, size):
    for n in names:
        for d in ("/usr/share/fonts/opentype/inter", "/usr/share/fonts/truetype/dejavu", "C:/Windows/Fonts"):
            p = Path(d) / n
            if p.exists():
                return ImageFont.truetype(str(p), size)
    return ImageFont.load_default(size)


BOLD = ["Inter-Bold.otf", "DejaVuSans-Bold.ttf", "segoeuib.ttf"]
SEMI = ["Inter-SemiBold.otf", "DejaVuSans-Bold.ttf", "seguisb.ttf"]
REG = ["Inter-Regular.otf", "DejaVuSans.ttf", "segoeui.ttf"]


def gradient(w, h, c1, c2):
    g = Image.new("RGB", (w, h))
    px = g.load()
    for y in range(h):
        for x in range(w):
            t = (x / w) * 0.75 + (y / h) * 0.25
            px[x, y] = tuple(int(c1[i] + (c2[i] - c1[i]) * t) for i in range(3))
    return g


def rounded(im, r):
    m = Image.new("L", im.size, 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, im.size[0] - 1, im.size[1] - 1), r, fill=255)
    out = im.convert("RGBA")
    out.putalpha(m)
    return out


def main():
    bg = gradient(W, H, (30, 27, 75), (14, 116, 144)).convert("RGBA")   # indigo-950 -> cyan-700
    d = ImageDraw.Draw(bg)
    # cerchi decorativi tenui
    deco = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dd = ImageDraw.Draw(deco)
    dd.ellipse((-160, 380, 360, 900), outline=(255, 255, 255, 28), width=40)
    dd.ellipse((980, -220, 1460, 260), outline=(255, 255, 255, 22), width=34)
    bg.alpha_composite(deco)

    logo = Image.open(ROOT / "src/VhdIsoStudio/Assets/app.png").convert("RGBA").resize((112, 112), Image.LANCZOS)
    bg.alpha_composite(logo, (64, 70))
    d.text((196, 78), "VHD ISO Studio", font=font(BOLD, 60), fill="white")
    d.text((198, 150), "VHD/VHDX  →  WIM  →  ISO  ·  oscdimg GUI", font=font(SEMI, 26), fill=(224, 231, 255))

    y = 236
    for line in ("Monta, cattura e crea ISO avviabili BIOS + UEFI",
                 "Mount, capture & build bootable BIOS + UEFI ISOs"):
        d.text((68, y), line, font=font(REG, 27), fill=(240, 244, 255))
        y += 42

    chips = ["Tutti i parametri oscdimg", "Modalità automatica", "100% portabile",
             "IT / EN", "3 temi + alto contrasto", ".NET 10 · x64 · ARM64"]
    f = font(SEMI, 21)
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    x, y = 68, 350
    pos = []
    for c in chips:
        tw = ld.textlength(c, font=f)
        if x + tw + 40 > 640:
            x, y = 68, y + 58
        ld.rounded_rectangle((x, y, x + tw + 32, y + 42), 21, fill=(255, 255, 255, 38), outline=(255, 255, 255, 120), width=2)
        pos.append((x + 16, y + 8, c))
        x += tw + 44
    bg.alpha_composite(layer)
    d = ImageDraw.Draw(bg)
    for px_, py_, c in pos:
        d.text((px_, py_), c, font=f, fill="white")

    d.text((68, 560), "Windows 10 / 11  ·  open source  ·  MIT", font=font(REG, 21), fill=(199, 210, 254))

    shot_path = IMG / "auto-dark-it.png"
    if shot_path.exists():
        shot = Image.open(shot_path).convert("RGB")
        sw = 600
        shot = shot.resize((sw, int(shot.height * sw / shot.width)), Image.LANCZOS)
        shot = rounded(shot, 18)
        sx, sy = 650, 120
        shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        ImageDraw.Draw(shadow).rounded_rectangle((sx + 10, sy + 18, sx + shot.width + 10, sy + shot.height + 18), 18, fill=(0, 0, 0, 140))
        bg.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(16)))
        bg.alpha_composite(shot, (sx, sy))
        ImageDraw.Draw(bg).rounded_rectangle((sx, sy, sx + shot.width - 1, sy + shot.height - 1), 18, outline=(255, 255, 255, 90), width=2)

    IMG.mkdir(parents=True, exist_ok=True)
    bg.convert("RGB").save(OUT, optimize=True)
    print(f"Scritta: {OUT} ({OUT.stat().st_size // 1024} KB)")


if __name__ == "__main__":
    main()
