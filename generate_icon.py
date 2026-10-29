import os
from PIL import Image, ImageDraw, ImageFont

def create_icon():
    size = 512
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # 1. Основная подложка приложения: темный матовый скругленный квадрат
    # Цвет: #0F172A (глубокий сланец)
    bg_color = (15, 23, 42, 255)
    border_color = (51, 65, 85, 255) # #334155
    
    pad = 24
    draw.rounded_rectangle(
        [pad, pad, size - pad, size - pad],
        radius=96,
        fill=bg_color,
        outline=border_color,
        width=8
    )

    # 2. Планшет / Календарная сетка табеля
    # Координаты календаря
    cx1, cy1, cx2, cy2 = 80, 84, 432, 428
    cal_bg = (30, 41, 59, 255) # #1E293B
    draw.rounded_rectangle([cx1, cy1, cx2, cy2], radius=24, fill=cal_bg)

    # Верхняя планка календаря (акцентный строгий синий #0284C7)
    header_h = 76
    draw.rounded_rectangle([cx1, cy1, cx2, cy1 + header_h], radius=24, fill=(2, 132, 199, 255))
    # Срезаем нижние скругления шапки
    draw.rectangle([cx1, cy1 + 24, cx2, cy1 + header_h], fill=(2, 132, 199, 255))

    # Люверсы / кольца календаря
    ring_color = (241, 245, 249, 255) # #F1F5F9
    draw.rounded_rectangle([150, 64, 180, 110], radius=10, fill=ring_color)
    draw.rounded_rectangle([332, 64, 362, 110], radius=10, fill=ring_color)

    # 3. Минималистичные блоки сменного графика (табельные смены)
    # 4 строки (смены/бригады) x 4 столбца (дни цикла)
    grid_top = cy1 + header_h + 24
    grid_left = cx1 + 28
    block_w = 64
    block_h = 44
    gap_x = 18
    gap_y = 16

    # Цвета статусов смен:
    c_shift_16 = (14, 165, 233, 255) # #0EA5E9 (Заступление 16ч - яркий небесно-голубой)
    c_shift_8  = (139, 92, 246, 255) # #8B5CF6 (Сдача 8ч - лавандовый)
    c_off      = (51, 65, 85, 255)   # #334155 (Выходной - нейтральный серый)
    c_sub      = (16, 185, 129, 255) # #10B981 (Подмена / дежурство - изумрудный)

    # Матрица сменности:
    matrix = [
        [c_shift_16, c_shift_8,  c_off,      c_off],
        [c_off,      c_shift_16, c_shift_8,  c_off],
        [c_off,      c_off,      c_sub,      c_shift_8],
        [c_shift_8,  c_off,      c_off,      c_shift_16]
    ]

    for row_idx, row in enumerate(matrix):
        for col_idx, color in enumerate(row):
            bx1 = grid_left + col_idx * (block_w + gap_x)
            by1 = grid_top + row_idx * (block_h + gap_y)
            bx2 = bx1 + block_w
            by2 = by1 + block_h
            draw.rounded_rectangle([bx1, by1, bx2, by2], radius=10, fill=color)

    # 4. Круглый бейдж в правом нижнем углу: символ 24ч (круглосуточный баланс поста)
    badge_cx, badge_cy, badge_r = 404, 404, 80
    # Подложка бейджа с отсечкой
    draw.ellipse(
        [badge_cx - badge_r - 6, badge_cy - badge_r - 6, badge_cx + badge_r + 6, badge_cy + badge_r + 6],
        fill=bg_color
    )
    # Сам бейдж (изумрудный зеленый #107C41 / #10B981 - символ нормы баланса 24.0ч)
    badge_color = (16, 124, 65, 255)
    draw.ellipse(
        [badge_cx - badge_r, badge_cy - badge_r, badge_cx + badge_r, badge_cy + badge_r],
        fill=badge_color,
        outline=(255, 255, 255, 255),
        width=6
    )

    # Текст '24' внутри бейджа (строгий геометрический шрифт)
    try:
        font = ImageFont.truetype("arialbd.ttf", 64)
        draw.text((badge_cx, badge_cy - 4), "24", font=font, fill=(255, 255, 255, 255), anchor="mm")
    except Exception:
        # Fallback если шрифт недоступен
        draw.text((badge_cx, badge_cy), "24", fill=(255, 255, 255, 255), anchor="mm")

    # Сохраняем в Assets
    assets_dir = r"D:\Projects\ShiftRoster\Assets"
    os.makedirs(assets_dir, exist_ok=True)

    png_path = os.path.join(assets_dir, "icon.png")
    ico_path = os.path.join(assets_dir, "app.ico")
    app_ico_legacy = os.path.join(assets_dir, "avalonia-logo.ico")

    # Сохранение PNG 512x512
    img.save(png_path, format="PNG")
    print(f"Saved PNG: {png_path}")

    # Сохранение мульти-размерного ICO (256, 128, 64, 48, 32, 24, 16)
    icon_sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (24, 24), (16, 16)]
    img.save(ico_path, format="ICO", sizes=icon_sizes)
    print(f"Saved ICO: {ico_path}")

    # Также обновляем avalonia-logo.ico для обратной совместимости в окне
    img.save(app_ico_legacy, format="ICO", sizes=icon_sizes)
    print(f"Updated: {app_ico_legacy}")

if __name__ == "__main__":
    create_icon()
