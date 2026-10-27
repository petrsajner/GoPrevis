"""Create a verified portable ZIP without diagnostic output or local caches."""
from pathlib import Path
import argparse
import hashlib
import json
import subprocess
import zipfile

REPO = Path(__file__).resolve().parents[2]
SOURCE = REPO / 'build/GoPrevis-0.20.0-Windows'
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--public', action='store_true', help='Ship user documentation and runtime files without development reports or debug symbols.')
parser.add_argument('--output', type=Path, default=REPO / 'build/GoPrevis-0.20.0-Windows.zip')
args = parser.parse_args()
TARGET = args.output.resolve()
TARGET.parent.mkdir(parents=True, exist_ok=True)


def include(path):
    rel = path.relative_to(SOURCE)
    if rel.parts[0] == 'output' or '__pycache__' in rel.parts:
        return False
    if len(rel.parts) > 1 and rel.parts[:2] == ('docs', 'manual'):
        return False
    if args.public:
        if rel.parts[0] in {'validation', 'NAVRH-GOPREVIS.md', 'VALIDATION.md', 'BUILD-INFO.json'}:
            return False
        if path.suffix.lower() == '.pdb':
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
    if args.public:
        identity = {
            'product': 'GoPrevis', 'version': '0.20.0', 'platform': 'Windows x64',
            'releaseChannel': 'alpha', 'developmentStatus': 'under active development',
            'releaseTag': 'v0.20.0-alpha',
            'repository': 'https://github.com/petrsajner/GoPrevis',
            'sourceRevision': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=REPO, text=True).strip(),
            'applicationCodeRevision': 'f5ed5fc06f1c0182d6cb48cd6d5ea6159519d6f4',
            'files': {name: sha256(SOURCE / name) for name in
                      ['GoPrevis.exe', 'GoPrevis.pck', 'data_GoPrevis_windows_x86_64/GoPrevis.dll', 'GoPrevis-User-Guide-EN.pdf']},
        }
        archive.writestr(SOURCE.name + '/BUILD-INFO.json', json.dumps(identity, indent=2) + '\n')
with zipfile.ZipFile(TARGET) as archive:
    assert archive.testzip() is None
    names = archive.namelist()
    for name in required:
        assert SOURCE.name + '/' + name in names
    assert not any('/output/' in name or 'kocka a mys' in name for name in names)
report = {'version': '0.20.0', 'file': TARGET.name, 'files': len(names),
          'bytes': TARGET.stat().st_size, 'sha256': sha256(TARGET),
          'zipCrc': 'passed', 'publicDistribution': args.public, 'published': False}
(TARGET.with_suffix('.zip.json')).write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(json.dumps(report, indent=2))
