"""Assemble local, unpublished release-review materials from real captures.

Usage: python tools/release/prepare_review.py --draft <draft directory>
No provider or publication API is used.
"""
import argparse
import hashlib
import json
import shutil
import subprocess
import zipfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[2]
FONT_DIR = Path('C:/Windows/Fonts')


def font(size, bold=False):
    return ImageFont.truetype(str(FONT_DIR / ('segoeuib.ttf' if bold else 'segoeui.ttf')), size)


def prepare(draft):
    media = draft / 'media-v2'
    shot = media / 'street-shot'
    pdf = REPO / 'docs/GoPrevis-User-Guide-EN.pdf'
    shutil.copy2(pdf, draft / pdf.name)
    header = Image.new('RGB', (1920, 64), '#17233b')
    draw = ImageDraw.Draw(header)
    for i, label in enumerate(['COLOR', 'DEPTH', 'POSE']):
        draw.text((i * 640 + 22, 14), label, fill='white', font=font(27, True))
    header.save(media / 'pass-labels.png')
    command = [str(REPO / '.tools/ffmpeg/ffmpeg.exe'), '-hide_banner', '-loglevel', 'error', '-y']
    for name in ['color', 'depth', 'pose']:
        command += ['-i', str(shot / (name + '.mp4'))]
    command += ['-loop', '1', '-i', str(media / 'pass-labels.png'), '-filter_complex',
                '[0:v]scale=640:360,setsar=1[a];[1:v]scale=640:360,setsar=1[b];'
                '[2:v]scale=640:360,setsar=1[c];[a][b][c]hstack=inputs=3[row];'
                '[3:v][row]vstack=inputs=2[v]', '-map', '[v]', '-t', '15', '-r', '24',
                '-an', '-c:v', 'libx264', '-preset', 'medium', '-crf', '20', '-pix_fmt',
                'yuv420p', '-movflags', '+faststart', str(media / 'street-color-depth-pose.mp4')]
    subprocess.run(command, check=True)
    strip = Image.new('RGB', (1600, 1040), '#17233b')
    draw = ImageDraw.Draw(strip)
    draw.text((28, 16), 'GoPrevis | Street flyover', font=font(34, True), fill='white')
    draw.text((28, 61), 'Actual 15-second 3D guide shot | 24 fps', font=font(22), fill='#c8d0e3')
    for i, frame in enumerate([0, 72, 144, 216, 288, 359]):
        im = Image.open(shot / 'color' / f'{frame:06}.png').convert('RGB')
        im = im.resize((768, 432), Image.Resampling.LANCZOS)
        # Six images in a two-row / three-column storyboard.
        im = im.resize((504, 284), Image.Resampling.LANCZOS)
        x, y = 28 + (i % 3) * 520, 122 + (i // 3) * 349
        strip.paste(im, (x, y))
        draw.text((x, y + 293), f'{frame / 24:.2f} s', font=font(22), fill='white')
    strip = strip.crop((0, 0, 1600, 845))
    strip.save(media / 'street-storyboard.png')
    final = Image.open(shot / 'color/000359.png')
    final.save(media / 'street-final-frame.png')
    manifest = {
        'product': 'GoPrevis', 'version': '0.19.1', 'date': '2026-10-26',
        'status': 'draft_prepared_for_review', 'published': False,
        'publicationAuthorized': False, 'noNewApiCalls': True,
        'example': 'city-street-15s.goprevis', 'durationSeconds': 15, 'fps': 24,
        'frames': 360, 'dimensions': [1280, 720], 'uiCaptureSize': [1280, 800],
        'aiWindow': 'Unsubmitted description; no new AI generation is depicted.',
        'manual': {'file': pdf.name, 'language': 'English', 'pages': 41, 'chapters': 22},
        'positioning': 'Complete manual previs workflow without AI or an API key; optional AI Director uses the user provider key.'
    }
    (draft / 'REVIEW-MANIFEST.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    (draft / 'REVIEW-README.txt').write_text(
        'GoPrevis - local release review, 26 October 2026\n\n'
        'Status: draft for Petr Sajner; not published.\n'
        '1. GoPrevis-release-EN.txt - English announcement copy.\n'
        '2. GoPrevis-User-Guide-EN.pdf - complete English beginner manual.\n'
        '3. UI/ - real 1280x800 application captures.\n'
        '4. Video/ - actual street-shot RGB, depth and pose plus a labeled comparison.\n'
        '5. Images/ - actual storyboard and final frame.\n\n'
        'The AI window shows an unsubmitted example description. No new paid AI request\n'
        'was used to prepare these materials. AI Director is optional; manual tools and\n'
        'exports require no API key. These videos are 3D guides, not final AI-generated video.\n'
        'Full PNG sequences and per-frame pose data remain in the local media-v2/street-shot folder.\n', encoding='utf-8')
    files = [(draft / name, name) for name in ['GoPrevis-release-EN.txt', pdf.name, 'REVIEW-MANIFEST.json',
                                              'REVIEW-README.txt', 'PRODUCT-FACTS.json']]
    files += [(p, 'UI/' + p.name) for p in sorted(media.glob('0*.png'))]
    files += [(shot / (name + '.mp4'), 'Video/' + name + '.mp4') for name in ['color', 'depth', 'pose']]
    files += [(media / 'street-color-depth-pose.mp4', 'Video/street-color-depth-pose.mp4')]
    files += [(media / name, 'Images/' + name) for name in ['street-storyboard.png', 'street-final-frame.png']]
    target = draft / 'GoPrevis-0.19.1-Release-Review.zip'
    with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED) as archive:
        for source, name in files:
            archive.write(source, name)
    with zipfile.ZipFile(target) as archive:
        assert archive.testzip() is None
        assert len(archive.namelist()) == len(files)
    print(json.dumps({'zip': str(target), 'files': len(files), 'bytes': target.stat().st_size,
                      'sha256': hashlib.sha256(target.read_bytes()).hexdigest()}, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--draft', type=Path, required=True)
    prepare(parser.parse_args().draft.resolve())
