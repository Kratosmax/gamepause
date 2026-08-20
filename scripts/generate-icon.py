from pathlib import Path
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
ASSET_DIR = ROOT / "src" / "GamePause.App" / "Assets"
PREVIEW_DIR = ROOT / "temp" / "icon"


def rounded_rectangle(draw, bounds, radius, fill, outline=None, width=1):
    draw.rounded_rectangle(bounds, radius=radius, fill=fill, outline=outline, width=width)


def create_icon():
    scale = 4
    image = Image.new("RGBA", (256 * scale, 256 * scale), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    # Layered rounded tiles keep the mark readable at taskbar and tray sizes.
    rounded_rectangle(
        draw,
        (16 * scale, 19 * scale, 240 * scale, 243 * scale),
        55 * scale,
        "#b8c9d8",
    )
    rounded_rectangle(
        draw,
        (12 * scale, 12 * scale, 236 * scale, 236 * scale),
        55 * scale,
        "#f7fbff",
        "#d9e5ee",
        4 * scale,
    )

    rounded_rectangle(
        draw,
        (38 * scale, 39 * scale, 210 * scale, 211 * scale),
        43 * scale,
        "#38c9b0",
    )

    for left in (79, 135):
        rounded_rectangle(
            draw,
            (left * scale, 78 * scale, (left + 27) * scale, 174 * scale),
            11 * scale,
            "#ffffff",
        )

    center_x, center_y, radius = 193 * scale, 62 * scale, 22 * scale
    snow_color = "#2378f2"
    line_width = 6 * scale
    for dx, dy in ((1, 0), (0, 1), (1, 1), (1, -1)):
        length = radius if dx == 0 or dy == 0 else int(radius * 0.72)
        draw.line(
            (
                center_x - dx * length,
                center_y - dy * length,
                center_x + dx * length,
                center_y + dy * length,
            ),
            fill=snow_color,
            width=line_width,
        )

    image = image.resize((256, 256), Image.Resampling.LANCZOS)
    return image


def main():
    ASSET_DIR.mkdir(parents=True, exist_ok=True)
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)
    icon = create_icon()
    icon.save(PREVIEW_DIR / "game-pause-icon.png")
    icon.save(
        ASSET_DIR / "game-pause.ico",
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )


if __name__ == "__main__":
    main()
