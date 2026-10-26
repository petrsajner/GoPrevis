"""Fetch the publisher's free Standard animation archives and CC0 animal sources.

No account/key, payment, paid archive, GobKit source or user-project file is used.
Downloads are development-only caches under ignored output/.
"""
from pathlib import Path
import http.cookiejar,json,re,shutil,subprocess,urllib.parse,urllib.request,zipfile

ROOT=Path(__file__).resolve().parents[2]
CACHE=ROOT/'output/library-expansion-2026-10-26'
ANIMALS=[('Animal Pack Vol.2 by @Quaternius.zip','https://opengameart.org/sites/default/files/Animal%20Pack%20Vol.2%20by%20%40Quaternius.zip'),
         ('Farm Animals by @Quaternius.zip','https://opengameart.org/sites/default/files/Farm%20Animals%20by%20%40Quaternius.zip'),
         ('rabbit-FBX.7z','https://opengameart.org/sites/default/files/rabbit-FBX.7z')]

def safe_zip(path,target):
 with zipfile.ZipFile(path) as archive:
  assert archive.testzip() is None
  for item in archive.infolist():
   if not (target/item.filename).resolve().is_relative_to(target.resolve()):raise ValueError('Unsafe archive member')
  archive.extractall(target)

def animation(slug,number):
 target=CACHE/(slug+'-standard.zip')
 if not target.exists():
  opener=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()));opener.addheaders=[('User-Agent','Mozilla/5.0')];url='https://quaternius.itch.io/'+slug
  page=opener.open(url,timeout=45).read().decode();csrf=re.search(r'name="csrf_token"\s+value="([^"]+)"',page).group(1)
  def post(endpoint,values,referer):return json.loads(opener.open(urllib.request.Request(endpoint,data=urllib.parse.urlencode(values).encode(),headers={'Referer':referer,'X-Requested-With':'XMLHttpRequest'}),timeout=45).read())
  claim=post(url+'/download_url',{'csrf_token':csrf,'source':'view_game'},url)['url'];page=opener.open(claim,timeout=45).read().decode();ids=re.findall(r'data-upload_id="(\d+)"',page)
  if len(ids)!=1:raise ValueError('Expected only the free Standard upload; refusing ambiguous downloads')
  download=post(url+'/file/'+ids[0],{'csrf_token':csrf,'source':'game_download'},claim)['url'];target.write_bytes(opener.open(download,timeout=120).read())
 with zipfile.ZipFile(target) as archive:
  assert archive.testzip() is None;name=next(n for n in archive.namelist() if n.endswith(f'/Unreal-Godot/UAL{number}_Standard.glb'));(CACHE/f'UAL{number}_Standard.glb').write_bytes(archive.read(name))
 print('Verified free archive:',target.name,flush=True)

def main():
 CACHE.mkdir(parents=True,exist_ok=True)
 for number,slug in enumerate(['universal-animation-library','universal-animation-library-2'],1):animation(slug,number)
 sources=[]
 for name,url in ANIMALS:
  target=CACHE/name
  if not target.exists():target.write_bytes(urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'Mozilla/5.0'}),timeout=120).read())
  if name.endswith('.zip'):
   output=CACHE/(target.stem+'-extracted');safe_zip(target,output)
   for fbx in output.rglob('*.fbx'):sources.append({'id':'quaternius-'+fbx.stem.lower(),'name':fbx.stem,'path':str(fbx.resolve()),'publisher':'Quaternius','license':'CC0-1.0'})
  else:
   output=CACHE/'rabbit-extracted';output.mkdir(exist_ok=True)
   # Windows/macOS bsdtar supports 7z; a 7z CLI can be used on other developer hosts.
   seven=shutil.which('7z') or shutil.which('7za')
   command=[seven,'x','-y','-o'+str(output),str(target)] if seven else ['tar','-xf',str(target),'-C',str(output)]
   subprocess.run(command,check=True);sources.append({'id':'rabbit-cdmir','name':'Rabbit','path':str((output/'rabbit.fbx').resolve()),'publisher':'CDmir / TinyWorlds','license':'CC0-1.0'})
 (CACHE/'animal-source-inputs.json').write_text(json.dumps(sources,indent=2)+'\n',encoding='utf-8')

if __name__=='__main__':main()
