"""ShotLite app icon, drawn as vector shapes so every size is crisp.

Usage: draw_icon.py <out.ico> <out-1024.png> [preview.png]
"""
import sys
from PIL import Image, ImageDraw

TOP_LEFT = (56, 132, 255)      # vivid blue
BOTTOM_RIGHT = (139, 61, 255)  # violet
WHITE = (255, 255, 255, 255)
TIP = (255, 201, 60, 255)      # amber pencil tip
SS = 8                         # supersampling factor


def render(size: int) -> Image.Image:
    n = size * SS
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))

    # Diagonal gradient tile with rounded corners.
    grad = Image.new("RGBA", (n, n))
    gp = grad.load()
    for y in range(n):
        for x in range(n):
            t = (x + y) / (2 * (n - 1))
            gp[x, y] = tuple(round(TOP_LEFT[i] + (BOTTOM_RIGHT[i] - TOP_LEFT[i]) * t) for i in range(3)) + (255,)
    mask = Image.new("L", (n, n), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, n - 1, n - 1), radius=round(n * 0.23), fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    U = lambda v: v * n  # unit -> pixels

    # Stroke widths: proportional, but never thinner than ~1.6 px at the target size.
    sw = max(0.095, 1.6 / size)
    m, a = 0.19, 0.21  # bracket inset and arm length
    w = round(U(sw))

    def bracket(points):
        pts = [(U(x), U(y)) for x, y in points]
        d.line(pts, fill=WHITE, width=w, joint="curve")
        r = w / 2
        for x, y in (pts[0], pts[-1]):
            d.ellipse((x - r, y - r, x + r, y + r), fill=WHITE)

    lo, hi = m, 1 - m
    bracket([(lo, lo + a), (lo, lo), (lo + a, lo)])
    bracket([(hi - a, lo), (hi, lo), (hi, lo + a)])
    bracket([(lo, hi - a), (lo, hi), (lo + a, hi)])
    bracket([(hi - a, hi), (hi, hi), (hi, hi - a)])

    # Pencil: white body along the diagonal, amber tip pointing to the bottom-left.
    bw = max(0.15, 2.6 / size)
    body_start, body_end, tip = (0.67, 0.33), (0.44, 0.56), (0.335, 0.665)
    d.line([(U(body_start[0]), U(body_start[1])), (U(body_end[0]), U(body_end[1]))], fill=WHITE, width=round(U(bw)))
    r = U(bw) / 2
    d.ellipse((U(body_start[0]) - r, U(body_start[1]) - r, U(body_start[0]) + r, U(body_start[1]) + r), fill=WHITE)
    # tip triangle: perpendicular offsets of the body end
    ox, oy = bw / 2 * 0.7071, bw / 2 * 0.7071
    tri = [(body_end[0] - ox, body_end[1] - oy), (body_end[0] + ox, body_end[1] + oy), tip]
    d.polygon([(U(x), U(y)) for x, y in tri], fill=TIP)

    return img.resize((size, size), Image.LANCZOS)


def main():
    out_ico, out_png = sys.argv[1], sys.argv[2]
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = {s: render(s) for s in sizes}
    big = render(1024)
    big.save(out_png)
    frames[256].save(out_ico, sizes=[(s, s) for s in sizes], append_images=[frames[s] for s in sizes if s != 256])

    if len(sys.argv) > 3:
        preview_sizes = [16, 20, 24, 32, 48]
        pad, cell = 10, 48
        width = pad + len(preview_sizes) * (cell + pad) + 128 + pad
        sheet = Image.new("RGBA", (width, 2 * (128 + 2 * pad)), (0, 0, 0, 0))
        sd = ImageDraw.Draw(sheet)
        for row, bg in enumerate([(32, 32, 32, 255), (243, 243, 243, 255)]):
            y0 = row * (128 + 2 * pad)
            sd.rectangle((0, y0, width, y0 + 128 + 2 * pad), fill=bg)
            x = pad
            for s in preview_sizes:
                sheet.alpha_composite(frames[s], (x, y0 + pad))
                x += cell + pad
            sheet.alpha_composite(render(128), (x, y0 + pad))
        sheet.resize((sheet.width * 2, sheet.height * 2), Image.NEAREST).save(sys.argv[3])


if __name__ == "__main__":
    main()
