from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw


ICON_SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def _draw_fallback(size: int = 1024) -> Image.Image:
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    margin = size // 12
    radius = size // 6
    draw.rounded_rectangle(
        (margin, margin, size - margin, size - margin),
        radius=radius,
        fill=(24, 25, 28, 255),
        outline=(238, 238, 238, 255),
        width=max(4, size // 64),
    )
    draw.polygon(
        (
            (size * 0.39, size * 0.29),
            (size * 0.39, size * 0.71),
            (size * 0.72, size * 0.50),
        ),
        fill=(255, 28, 28, 255),
    )
    return image


def build_icon(source: Path, destination: Path) -> None:
    if source.is_file():
        image = Image.open(source).convert("RGBA")
    else:
        image = _draw_fallback()

    side = max(image.size)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.alpha_composite(image, ((side - image.width) // 2, (side - image.height) // 2))
    destination.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(destination, format="ICO", sizes=[(size, size) for size in ICON_SIZES])


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    build_icon(args.source, args.destination)


if __name__ == "__main__":
    main()
