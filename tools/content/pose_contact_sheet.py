"""Assemble the actual Godot pose PNGs into review contact sheets."""
import json
import math
import textwrap
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parents[2]
POSES=json.loads((ROOT/'assets/content/manifests/human-poses.json').read_text(encoding='utf-8'))
FONT=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',12)

def sheet(poses,name,columns=8):
    cellw,cellh=204,198
    canvas=Image.new('RGB',(columns*cellw,math.ceil(len(poses)/columns)*cellh),'#18181d');draw=ImageDraw.Draw(canvas)
    for i,pose in enumerate(poses):
        x=(i%columns)*cellw;y=(i//columns)*cellh
        image=Image.open(ROOT/'assets/content/thumbnails'/f'{pose["Id"]}.png').convert('RGB')
        assert image.size==(192,160)
        assert len(image.getcolors(192*160) or [])>=4
        canvas.paste(image,(x+6,y+2))
        title='\n'.join(textwrap.wrap(pose['Name'],width=29)[:2])
        draw.multiline_text((x+7,y+163),title,fill='#e1dfeb',font=FONT,spacing=0)
    destination=ROOT/'output'/name;canvas.save(destination,quality=93);print(destination)

sheet(POSES,'pose-contact-sheet.jpg')
for category in ['Sitting','Floor sitting','Kneeling','Crouching','Lying','Action']:
    sheet([p for p in POSES if p['Category']==category],'pose-'+category.lower().replace(' ','-')+'-contact.jpg',columns=6)
