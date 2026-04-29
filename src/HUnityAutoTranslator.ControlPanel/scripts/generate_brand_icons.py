from __future__ import annotations

from pathlib import Path
from typing import Iterable

from PIL import Image


SIZES = (16, 32, 48, 64, 128, 256, 512, 1024)
ICO_SIZES = (16, 32, 48, 64, 128, 256)

# Pixel-perfect square crops from branding-preview-source.png.
# The source image is the approved two-color preview generated in Codex.
CROPS = {
    "hunity-icon-blue-white": (76, 92, 792, 808),
    "hunity-icon-white-blue": (878, 92, 1594, 808),
}


def is_edge_background(pixel: tuple[int, int, int, int]) -> bool:
    r, g, b, _ = pixel
    return r >= 245 and g >= 245 and b >= 245


def make_edge_background_transparent(image: Image.Image) -> Image.Image:
    rgba = image.convert("RGBA")
    pixels = rgba.load()
    width, height = rgba.size
    visited: set[tuple[int, int]] = set()
    stack: list[tuple[int, int]] = []

    for x in range(width):
        stack.append((x, 0))
        stack.append((x, height - 1))
    for y in range(1, height - 1):
        stack.append((0, y))
        stack.append((width - 1, y))

    while stack:
        x, y = stack.pop()
        if (x, y) in visited or not (0 <= x < width and 0 <= y < height):
            continue

        visited.add((x, y))
        if not is_edge_background(pixels[x, y]):
            continue

        r, g, b, _ = pixels[x, y]
        pixels[x, y] = (r, g, b, 0)
        stack.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))

    return rgba


def write_variant(output_roots: Iterable[Path], source: Image.Image, name: str, crop_box: tuple[int, int, int, int]) -> None:
    cropped = make_edge_background_transparent(source.crop(crop_box))

    for root in output_roots:
        root.mkdir(parents=True, exist_ok=True)
        for stale_svg in root.glob(f"{name}.svg"):
            stale_svg.unlink()

        ico_images: list[Image.Image] = []
        for size in SIZES:
            image = cropped.resize((size, size), Image.Resampling.LANCZOS)
            image.save(root / f"{name}-{size}.png")
            if size == 1024:
                image.save(root / f"{name}.png")
            if size in ICO_SIZES:
                ico_images.append(image)

        ico_images[-1].save(
            root / f"{name}.ico",
            sizes=[(size, size) for size in ICO_SIZES],
            append_images=ico_images[:-1],
        )


def main() -> None:
    script_root = Path(__file__).resolve().parent
    control_panel_root = script_root.parent
    source_path = script_root / "branding-preview-source.png"
    output_roots = (
        control_panel_root / "public" / "branding",
        control_panel_root / "src" / "assets" / "branding",
    )

    source = Image.open(source_path)
    for name, crop_box in CROPS.items():
        write_variant(output_roots, source, name, crop_box)


if __name__ == "__main__":
    main()
