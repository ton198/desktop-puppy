from pathlib import Path
import json
from PIL import Image, ImageOps, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
FONT = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 18)
SMALL = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 14)
sprites = {}
checks = {}
for pose in ('peek', 'transition', 'blink'):
    im = Image.open(ROOT / f'{pose}-left.raw.png').convert('RGBA')
    sprites[pose] = im
    im.save(ROOT / f'{pose}-left.png')
    ImageOps.mirror(im).save(ROOT / f'{pose}-right.png')
    alpha = im.getchannel('A')
    checks[pose] = {'size': list(im.size), 'alpha_extrema': list(alpha.getextrema()), 'visible_bounds': list(alpha.point(lambda v: 255 if v > 128 else 0).getbbox())}
    assert alpha.getextrema() == (0, 255), 'Missing transparent background'
    assert im.size == (1254, 1254), 'Frames must share canvas'

# The preview models a vertical screen clip at source x=700.
# It reveals about 44% of the head, while all paws/body stay behind it.
CUT = 700
SCALE = .19
EXTENT = round(1254 * SCALE)
def thumb(im):
    return im.resize((EXTENT, EXTENT), Image.Resampling.LANCZOS)

for name, bg, fg in [('light', '#eef2f6', '#233044'), ('dark', '#222936', '#f4f6fa')]:
    sheet = Image.new('RGB', (1000, 670), bg)
    draw = ImageDraw.Draw(sheet)
    draw.text((24, 15), 'Edge peek - LEFT / RIGHT - transparent PNG candidates', font=FONT, fill=fg)
    for row, pose in enumerate(('transition', 'peek', 'blink')):
        y = 60 + row * 196
        draw.text((24, y), pose.upper(), font=FONT, fill=fg)
        for side, x in [('left', 145), ('right', 355)]:
            im = sprites[pose] if side == 'left' else ImageOps.mirror(sprites[pose])
            small = im.resize((180, 180), Image.Resampling.LANCZOS)
            sheet.paste(small, (x, y), small)
            draw.text((x + 48, y + 173), side, font=SMALL, fill=fg)
        for side, x in [('left', 655), ('right', 845)]:
            piece = sprites[pose].crop((CUT, 0, 1254, 1254))
            if side == 'right': piece = ImageOps.mirror(piece)
            piece = piece.resize((82, 180), Image.Resampling.LANCZOS)
            edge = x if side == 'left' else x + 82
            draw.line((edge, y, edge, y + 174), fill='#8795aa', width=2)
            sheet.paste(piece, (x, y), piece)
        draw.text((650, y + 174), 'Half-head screen clip', font=SMALL, fill=fg)
    sheet.save(ROOT / f'contact-sheet-{name}.png')

original = Image.open(ROOT.parents[3] / 'src/DesktopPuppy/Pet/Animation/Assets/idle.png').convert('RGBA')
original = ImageOps.mirror(original)
frames, durations = [], []
def render(progress, pose, label, normal=False):
    out = Image.new('RGB', (900, 350), '#eaf0f6')
    draw = ImageDraw.Draw(out)
    draw.text((22, 12), 'Left and right screen edges - illustrative playback', fill='#233044', font=FONT)
    draw.text((22, 42), label, fill='#233044', font=SMALL)
    for side, box in [('left', (30, 75, 435, 330)), ('right', (465, 75, 870, 330))]:
        panel = Image.new('RGBA', (405, 255), '#ffffff')
        im = thumb(original if normal else sprites[pose])
        x = round(12 * (1-progress) - CUT * SCALE * progress)
        if side == 'right':
            im = ImageOps.mirror(im)
            x = 405 - EXTENT - x
        panel.alpha_composite(im, (x, 4))
        out.paste(panel.convert('RGB'), (box[0], box[1]))
        edge = box[0] if side == 'left' else box[2]-1
        draw.line((edge, box[1], edge, box[3]), fill='#5478a5', width=2)
    return out

def add(progress, pose, label, duration=50, normal=False):
    frames.append(render(progress, pose, label, normal))
    durations.append(duration)

add(0, 'transition', 'Full pet near edge', 900, True)
for i in range(1, 6):
    p = i/5
    add(1-(1-p)**3, 'transition' if i < 3 else 'peek', 'Docking: 250 ms')
add(1, 'peek', 'Docked: only half of the head visible', 1100)
add(1, 'blink', 'Docked blink', 120)
add(1, 'peek', 'Hover the visible head to reveal', 800)
for i in range(1, 6):
    p = i/5
    add((1-p)**3, 'peek' if i < 3 else 'transition', 'Hover reveal: 250 ms')
add(0, 'transition', 'Full pet restored; clicks and dragging available', 1400, True)
for i in range(1, 6):
    p = i/5
    add(1-(1-p)**3, 'transition' if i < 3 else 'peek', 'Pointer left: tuck back after 800 ms')
add(1, 'peek', 'Docked again', 900)
frames[0].save(ROOT / 'edge-peek-preview.gif', save_all=True, append_images=frames[1:], duration=durations, loop=0, disposal=2)
checks['preview'] = {'cut_source_x': CUT, 'head_reference_bounds_approximate': [335, 146, 980, 625], 'visible_head_width_fraction': round((980-CUT)/(980-335), 3), 'frame_count': len(frames), 'purpose': 'illustrative; not production runtime acceptance'}
(ROOT / 'verification.json').write_text(json.dumps(checks, indent=2), encoding='utf-8')
print(json.dumps(checks, indent=2))
