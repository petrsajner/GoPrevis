"""Prepare audited creator-supplied FBX conversions; no GobKit sources are used.

The input GLBs are produced by the Godot FBX importer from the original archives
cached in output/library-expansion-2026-10-26. Vertex/skin/animation bytes remain
unchanged here. The JSON changes name joints and add one static source-axis node.
"""
from pathlib import Path
import hashlib,json,math,struct

ROOT=Path(__file__).resolve().parents[2]
CACHE=ROOT/'output/library-expansion-2026-10-26'
DEST=ROOT/'assets/content/animals/common'
MODELS=[('cat','Cat','quaternius-cat',.62,-90,'CatArmature|Idle'),
        ('dog-common','Dog','quaternius-dog',.85,-90,'DogArmature|Idle'),
        ('pig','Pig','quaternius-pig',.95,0,'Armature|Idle'),
        ('sheep','Sheep','quaternius-sheep',1.15,0,'Armature|Idle'),
        ('rabbit','Rabbit','rabbit-cdmir',.7,0,'Armature|Guarding'),
        ('eagle','Eagle','quaternius-eagle',.8,180,'EagleArmature|Idle')]
CAT_NAMES={'Bone':'Hips','Bone_001':'Spine','Bone_002':'Neck','Bone_003':'Head',
 'Bone_014':'FrontUpperLeg_R','Bone_015':'FrontLowerLeg_R','Bone_016':'FrontFoot_R',
 'Bone_017':'FrontUpperLeg_L','Bone_018':'FrontLowerLeg_L','Bone_019':'FrontFoot_L',
 'Bone_008':'BackUpperLeg_L','Bone_009':'BackLowerLeg_L','Bone_010':'BackFoot_L',
 'Bone_011':'BackUpperLeg_R','Bone_012':'BackLowerLeg_R','Bone_013':'BackFoot_R',
 'Bone_004':'TailBase','Bone_005':'Tail1','Bone_006':'Tail2','Bone_007':'Tail3',
 'IKFrontRight':'Control_FrontFoot_R','IKFrontLeft':'Control_FrontFoot_L',
 'IKBackRight':'Control_BackFoot_R','IKBackLeft':'Control_BackFoot_L'}
EAGLE_NAMES={'Bone':'Hips','Bone_001':'Spine','Bone_002':'Neck','Bone_003':'Head',
 'Bone_004':'TailBase','Bone_005':'Tail1','Bone_006':'Tail2',
 'Bone_007':'WingUpper_R','Bone_008':'WingLower_R','Bone_009':'WingWrist_R','Bone_010':'WingTip_R','Bone_011':'WingEnd_R',
 'Bone_012':'WingUpper_L','Bone_013':'WingLower_L','Bone_014':'WingWrist_L','Bone_015':'WingTip_L',
 'Bone_016':'UpperLeg_R','Bone_017':'LowerLeg_R','Bone_018':'Foot_R','Bone_019':'Toes_R',
 'Bone_020':'UpperLeg_L','Bone_021':'LowerLeg_L','Bone_022':'Foot_L','Bone_023':'Toes_L'}

def pack(g,binary):
 data=json.dumps(g,separators=(',',':')).encode();data+=b' '*(-len(data)%4);binary+=b'\0'*(-len(binary)%4)
 return struct.pack('<III',0x46546c67,2,28+len(data)+len(binary))+struct.pack('<II',len(data),0x4e4f534a)+data+struct.pack('<II',len(binary),0x004e4942)+binary

def main():
 DEST.mkdir(parents=True,exist_ok=True);models=[];motions=[];audit=[]
 source_audit={a['id']:a for a in json.loads((CACHE/'animal-fbx-audit.json').read_text())}
 for slug,name,source,height,yaw,idle in MODELS:
  raw=(CACHE/(source+'.glb')).read_bytes();length=struct.unpack_from('<I',raw,12)[0];g=json.loads(raw[20:20+length]);binary=raw[28+length:]
  mapping=CAT_NAMES if slug in ['cat','dog-common'] else EAGLE_NAMES if slug=='eagle' else {}
  for node in g['nodes']:
   old=node.get('name','');node['name']=mapping.get(old,'Control_'+old[3:] if old.startswith('IK_') else 'Control_Main' if old=='Main' else old)
  if yaw:
   angle=math.radians(yaw)*.5
   for scene in g['scenes']:
    scene['nodes']=[len(g['nodes'])];g['nodes'].append({'name':'SourceOrientation','rotation':[0,math.sin(angle),0,math.cos(angle)],'children':[]})
   # A single exported scene: its previous roots are captured from the source.
   original=json.loads(raw[20:20+length])
   for i,scene in enumerate(g['scenes']):g['nodes'][scene['nodes'][0]]['children']=original['scenes'][i]['nodes']
  family='animal-common-'+slug;asset='animal-'+slug;path='res://assets/content/animals/common/'+slug+'.glb'
  blob=pack(g,binary);(DEST/(slug+'.glb.bytes')).write_bytes(blob)
  source_url='https://opengameart.org/content/rabbit-0' if slug=='rabbit' else 'https://opengameart.org/content/animated-animales-low-poly' if source in ['quaternius-cat','quaternius-dog','quaternius-eagle'] else 'https://opengameart.org/content/lowpoly-animated-farm-animal-pack'
  author='CDmir / TinyWorlds' if slug=='rabbit' else 'Quaternius'
  models.append({'Id':asset,'Name':name,'Category':'Animals','ActorFamily':family,'SourcePath':path,'Scale':1,'DefaultAnimation':idle,'Animated':True,'Attribution':author,'License':'CC0-1.0','SourceUrl':source_url,'Sha256':hashlib.sha256(blob).hexdigest()})
  source_animations=source_audit[source]['animations']
  for animation in g.get('animations',[]):
   original=animation['name'];info=next((a for a in source_animations if a['name']==original),None)
   if not info or info['duration']<.1 or info['tracks']==0:continue
   label=original.split('|')[-1].replace('_000','').replace('_001',' variation').replace('_',' ');lower=label.lower();category='Walk' if 'walk' in lower else 'Run' if 'running' in lower else 'Idle' if any(n in lower for n in ['idle','guarding','sitting']) else 'Action';loop=category in ['Walk','Run','Idle'] or 'flying' in lower
   motions.append({'Id':asset+'-'+original.lower().replace('|','-').replace('_','-'),'Name':name+' · '+label,'ActorFamily':family,'SourcePath':path,'SourceAnimation':original,'SourceScale':1,'Duration':info['duration'],'Loop':loop,'Category':category,'Description':name+' native authored animation','Attribution':author,'License':'CC0-1.0','SourceUrl':source_url,'Tags':['animal','native','authored',slug,category.lower()]})
  audit.append({'Id':asset,'Name':name,'Source':str((CACHE/(source+'.glb')).relative_to(ROOT)),'SourceGlbSha256':hashlib.sha256(raw).hexdigest(),'PackedGlbSha256':hashlib.sha256(blob).hexdigest(),'TargetHeightMetres':height,'SourceYawCorrectionDegrees':yaw,'Author':author,'SourceUrl':source_url,'License':'CC0-1.0'})
 for filename,items in [('common-animal-models.json',models),('common-animal-motions.json',motions)]:
  (ROOT/'assets/content/manifests'/filename).write_text(json.dumps(items,indent=2)+'\n',encoding='utf-8')
 (DEST/'provenance.json').write_text(json.dumps({'RetrievedDate':'2026-10-26','Models':audit},indent=2)+'\n',encoding='utf-8')
 print('COMMON_ANIMALS_PREPARED',len(models),'models',len(motions),'clips; scales await rendered-bounds normalization')

if __name__=='__main__':main()
