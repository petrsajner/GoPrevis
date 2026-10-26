"""Create a verified portable ZIP without diagnostic output or local caches."""
from pathlib import Path
import hashlib
import json
import zipfile

REPO = Path(__file__).resolve().parents[2]
SOURCE = REPO / 'build/GoPrevis-0.19.1-Windows'
TARGET = REPO / 'build/GoPrevis-0.19.1-Windows.zip'


def include(path):
    rel = path.relative_to(SOURCE)
    if rel.parts[0] == 'output' or '__pycache__' in rel.parts:
        return False
    if len(rel.parts) > 1 and rel.parts[:2] == ('docs', 'manual'):
        return False
    return path.name != '.gdignore' and not path.name.endswith(('.bak', '.tmp', '.user'))


def sha256(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(chunk)
    return value.hexdigest()


required = ['GoPrevis.exe', 'GoPrevis.pck', 'GoPrevis-User-Guide-EN.pdf',
            'docs/GoPrevis-User-Guide-EN.pdf', 'examples/city-street-15s.goprevis',
            'examples/car-chase.goprevis', 'examples/lights.goprevis', 'tools/ffmpeg/ffmpeg.exe']
for name in required:
    assert (SOURCE / name).is_file(), name
files = sorted(p for p in SOURCE.rglob('*') if p.is_file() and include(p))
with zipfile.ZipFile(TARGET, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
    for path in files:
        archive.write(path, SOURCE.name + '/' + path.relative_to(SOURCE).as_posix())
with zipfile.ZipFile(TARGET) as archive:
    assert archive.testzip() is None
    names = archive.namelist()
    for name in required:
        assert SOURCE.name + '/' + name in names
    assert not any('/output/' in name or 'kocka a mys' in name for name in names)
report = {'version': '0.19.1', 'file': TARGET.name, 'files': len(files),
          'bytes': TARGET.stat().st_size, 'sha256': sha256(TARGET),
          'zipCrc': 'passed', 'published': False}
(TARGET.with_suffix('.zip.json')).write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(json.dumps(report, indent=2))
