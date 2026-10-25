"""Generate project-authored static blocking poses for GenPrevis' 21-bone mannequin.

There are no animation clips or third-party captures in this catalog. Limb
orientations are solved analytically at fixed lengths, then stored as local
quaternions. This is a curated blocking library, not a contact/cloth/finger solver.
"""
from __future__ import annotations
import copy
import json
import math
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / "assets/content/manifests/human-poses.json"

def vec(value): return np.array(value, dtype=float)
def unit(value):
    value = vec(value)
    length = np.linalg.norm(value)
    return value / length if length > 1e-10 else vec((0, 1, 0))
def mul(a, b):
    x,y,z,w=a; X,Y,Z,W=b
    return unitq((w*X+x*W+y*Z-z*Y, w*Y-x*Z+y*W+z*X, w*Z+x*Y-y*X+z*W, w*W-x*X-y*Y-z*Z))
def unitq(value):
    value=np.array(value,dtype=float); return value/np.linalg.norm(value)
def inv(q): return np.array((-q[0],-q[1],-q[2],q[3]))
def axis(axis, deg):
    half=math.radians(deg)*.5; return np.r_[unit(axis)*math.sin(half),math.cos(half)]
I=vec((0,0,0,1))
def euler(x=0,y=0,z=0): return mul(mul(axis((0,1,0),y),axis((1,0,0),x)),axis((0,0,1),z))
def anatomical_euler(x=0,y=0,z=0):
    # Authored torso/head X values mean forward flexion/downward gaze. With the
    # mannequin facing -Z this is the negative mathematical X rotation.
    return euler(-x,y,z)
def rotate(q,v):
    qv=q[:3];v=vec(v);return v+2*np.cross(qv,np.cross(qv,v)+q[3]*v)
def align(a,b):
    a=unit(a);b=unit(b);d=float(np.dot(a,b))
    if d < -.999999:
        other=(1,0,0) if abs(a[0])<.9 else (0,1,0)
        return axis(np.cross(a,other),180)
    return unitq(np.r_[np.cross(a,b),1+d])

BONES=[("Hips",None,(0,.94,0)),("Spine","Hips",(0,.14,0)),("Chest","Spine",(0,.18,0)),("Neck","Chest",(0,.19,0)),("Head","Neck",(0,.12,0))]
for side,sign in [("Left",1),("Right",-1)]:
    BONES += [(side+"Shoulder","Chest",(sign*.095,.105,0)),(side+"UpperArm",side+"Shoulder",(sign*.10,0,0)),(side+"LowerArm",side+"UpperArm",(sign*.265,0,0)),(side+"Hand",side+"LowerArm",(sign*.235,0,0)),(side+"UpperLeg","Hips",(sign*.105,-.035,0)),(side+"LowerLeg",side+"UpperLeg",(0,-.410,0)),(side+"Foot",side+"LowerLeg",(0,-.405,0)),(side+"Toe",side+"Foot",(0,-.03,-.14))]
PARENTS={n:p for n,p,o in BONES};OFFSETS={n:vec(o) for n,p,o in BONES}

def fk(local,hip):
    pos={};glob={}
    for n,parent,offset in BONES:
        if parent is None: pos[n]=vec(hip);glob[n]=local[n]
        else:pos[n]=pos[parent]+rotate(glob[parent],offset);glob[n]=mul(glob[parent],local[n])
    return pos,glob

def solve(base,target,l1,l2,hint):
    delta=target-base;distance=float(np.linalg.norm(delta));direction=unit(delta)
    distance=max(abs(l1-l2)+.0001,min(l1+l2-.0001,distance))
    along=(l1*l1-l2*l2+distance*distance)/(2*distance)
    height=math.sqrt(max(0,l1*l1-along*along))
    bend=vec(hint)-direction*np.dot(hint,direction)
    if np.linalg.norm(bend)<1e-6:bend=np.cross(direction,(1,0,0))
    joint=base+direction*along+unit(bend)*height
    end=base+direction*distance
    return joint,end

BASES={
 "stand":dict(hip=.94,feet=[(.13,-.85,0),(-.13,-.85,0)]),
 "wide":dict(hip=.90,feet=[(.30,-.81,-.04),(-.30,-.81,.04)]),
 "contrapposto":dict(hip=.94,feet=[(.11,-.85,0),(-.26,-.82,-.12)],spine=(0,0,-4)),
 "chair":dict(hip=.58,feet=[(.12,-.505,-.43),(-.12,-.505,-.43)]),
 "chair-wide":dict(hip=.58,feet=[(.25,-.505,-.40),(-.25,-.505,-.40)],knees=[(.65,0,-1),(-.65,0,-1)]),
 "chair-ankles":dict(hip=.58,feet=[(-.07,-.505,-.45),(.07,-.505,-.39)],knees=[(.2,0,-1),(-.2,0,-1)]),
 "chair-cross-left":dict(hip=.58,feet=[(-.20,-.075,-.40),(-.13,-.505,-.40)],knees=[(1,0,-.4),(0,0,-1)]),
 "chair-cross-right":dict(hip=.58,feet=[(.13,-.505,-.40),(.20,-.075,-.40)],knees=[(0,0,-1),(-1,0,-.4)]),
 "stool":dict(hip=.78,feet=[(.13,-.705,-.25),(-.13,-.705,-.25)]),
 "stool-perch":dict(hip=.78,feet=[(.13,-.705,-.25),(-.15,-.38,-.35)]),
 "low-seat":dict(hip=.36,feet=[(.21,-.285,-.43),(-.21,-.285,-.43)],knees=[(.5,0,-1),(-.5,0,-1)]),
 "floor-legs":dict(hip=.15,feet=[(.13,-.075,-.78),(-.13,-.075,-.78)]),
 "floor-cross":dict(hip=.18,feet=[(-.23,-.10,-.29),(.23,-.10,-.28)],knees=[(1,0,-.5),(-1,0,-.5)]),
 "floor-knees":dict(hip=.15,feet=[(.13,-.075,-.40),(-.13,-.075,-.40)],knees=[(0,1,-.2),(0,1,-.2)]),
 "floor-side":dict(hip=.15,feet=[(.49,-.075,.15),(.39,-.075,.27)],knees=[(1,0,-1),(1,0,-1)],spine=(0,0,-8)),
 "kneel":dict(hip=.52,feet=[(.13,-.445,.37),(-.13,-.445,.37)]),
 "kneel-low":dict(hip=.30,feet=[(.13,-.225,.37),(-.13,-.225,.37)]),
 "kneel-left":dict(hip=.57,feet=[(.18,-.495,-.35),(-.14,-.495,.34)]),
 "kneel-right":dict(hip=.57,feet=[(.14,-.495,.34),(-.18,-.495,-.35)]),
 "squat":dict(hip=.43,feet=[(.25,-.355,-.03),(-.25,-.355,-.03)],knees=[(.35,0,-1),(-.35,0,-1)],spine=(18,0,0)),
 "crouch":dict(hip=.65,feet=[(.22,-.575,-.20),(-.22,-.575,.14)],spine=(25,0,0)),
 "lunge-left":dict(hip=.71,feet=[(.18,-.635,-.49),(-.18,-.635,.49)],spine=(8,0,0)),
 "lunge-right":dict(hip=.71,feet=[(.18,-.635,.49),(-.18,-.635,-.49)],spine=(8,0,0)),
 "back":dict(hip=.14,body=(90,0,0),feet=[(.13,-.815,0),(-.13,-.815,0)],lying=True),
 "back-knees":dict(hip=.14,body=(90,0,0),feet_world=[(.13,-.055,-.40),(-.13,-.055,-.40)],knees=[(0,1,-.4),(0,1,-.4)],lying=True),
 "side-left":dict(hip=.17,body=(0,0,90),feet=[(.13,-.79,-.12),(-.13,-.79,-.08)],lying=True),
 "side-right":dict(hip=.17,body=(0,0,-90),feet=[(.13,-.79,-.08),(-.13,-.79,-.12)],lying=True),
 "fetal-left":dict(hip=.18,body=(0,0,90),feet=[(.13,-.35,-.30),(-.13,-.35,-.35)],lying=True),
 "fetal-right":dict(hip=.18,body=(0,0,-90),feet=[(.13,-.35,-.35),(-.13,-.35,-.30)],lying=True),
 "prone":dict(hip=.14,body=(-90,0,0),feet=[(.13,-.815,0),(-.13,-.815,0)],lying=True),
 "plank":dict(hip=.42,body=(-70,0,0),feet_world=[(.13,-.345,.73),(-.13,-.345,.73)],arms_world=[(.22,-.34,-.40),(-.22,-.34,-.40)],lying=True),
 "crawl":dict(hip=.48,body=(-70,0,0),feet_world=[(.17,-.40,.35),(-.17,-.40,.35)],arms_world=[(.22,-.40,-.40),(-.22,-.40,-.40)],lying=True),
 "walk-left":dict(hip=.94,feet=[(.13,-.85,-.25),(-.13,-.65,.40)],knees=[(0,0,-1),(0,0,-1)]),
 "walk-right":dict(hip=.94,feet=[(.13,-.65,.40),(-.13,-.85,-.25)],knees=[(0,0,-1),(0,0,-1)]),
 "run-left":dict(hip=.88,feet=[(.13,-.45,-.50),(-.13,-.50,.50)],spine=(15,0,0)),
 "run-right":dict(hip=.88,feet=[(.13,-.50,.50),(-.13,-.45,-.50)],spine=(15,0,0)),
 "kick-left":dict(hip=.94,feet=[(.13,-.25,-.69),(-.13,-.85,0)],knees=[(0,0,-1),(0,0,-1)]),
 "kick-right":dict(hip=.94,feet=[(.13,-.85,0),(-.13,-.25,-.69)],knees=[(0,0,-1),(0,0,-1)]),
}

# Wrist targets are in the pelvis' oriented local frame. These are simple hands,
# so their semantic labels describe the limb placement, not detailed finger poses.
ARMS={
 "relaxed":[(.24,-.055,-.015),(-.24,-.055,-.015)],
 "hips":[(.15,.075,-.01),(-.15,.075,-.01)],
 "crossed":[(-.10,.30,-.16),(.10,.30,-.12)],
 "behind":[(.04,.035,.20),(-.04,.035,.20)],
 "pockets":[(.16,-.015,-.045),(-.16,-.015,-.045)],
 "lap":[(.12,.035,-.36),(-.12,.035,-.36)],
 "lap-joined":[(.03,.06,-.35),(-.03,.06,-.35)],
 "thighs":[(.19,.01,-.26),(-.19,.01,-.26)],
 "armrests":[(.37,.13,-.22),(-.37,.13,-.22)],
 "desk":[(.22,.26,-.35),(-.22,.26,-.35)],
 "steering":[(.22,.33,-.38),(-.22,.33,-.38)],
 "phone":[(.10,.28,-.31),(-.24,-.055,-.015)],
 "phone-two":[(.075,.28,-.31),(-.075,.28,-.31)],
 "phone-ear":[(.09,.59,-.045),(-.24,-.055,-.015)],
 "read":[(.15,.29,-.30),(-.15,.29,-.30)],
 "chin":[(.04,.49,-.15),(-.10,.23,-.15)],
 "tired":[(.09,.57,-.13),(-.19,.01,-.26)],
 "knees-hug":[(.14,.25,-.35),(-.14,.25,-.35)],
 "floor-support":[(.35,-.04,.20),(-.35,-.04,.20)],
 "point-left":[(.19,.43,-.49),(-.24,-.055,-.015)],
 "point-right":[(.24,-.055,-.015),(-.19,.43,-.49)],
 "point-side-left":[(.69,.43,-.02),(-.24,-.055,-.015)],
 "point-side-right":[(.24,-.055,-.015),(-.69,.43,-.02)],
 "reach-up-left":[(.23,.90,-.06),(-.24,-.055,-.015)],
 "reach-up-right":[(.24,-.055,-.015),(-.23,.90,-.06)],
 "reach-up":[(.25,.90,-.07),(-.25,.90,-.07)],
 "hands-head":[(.12,.66,.02),(-.12,.66,.02)],
 "hands-behind-head":[(.10,.64,.11),(-.10,.64,.11)],
 "surrender":[(.37,.77,-.04),(-.37,.77,-.04)],
 "wave-left":[(.42,.78,-.07),(-.24,-.055,-.015)],
 "wave-right":[(.24,-.055,-.015),(-.42,.78,-.07)],
 "open":[(.58,.39,-.20),(-.58,.39,-.20)],
 "explain":[(.40,.29,-.28),(-.30,.33,-.28)],
 "shrug":[(.40,.58,-.14),(-.40,.58,-.14)],
 "stop":[(.16,.47,-.43),(-.24,-.055,-.015)],
 "prayer":[(.025,.37,-.22),(-.025,.37,-.22)],
 "clap":[(.025,.31,-.35),(-.025,.31,-.35)],
 "cheer":[(.48,.79,-.04),(-.48,.79,-.04)],
 "guard":[(.12,.56,-.22),(-.16,.50,-.28)],
 "punch-left":[(.16,.42,-.48),(-.14,.50,-.19)],
 "punch-right":[(.14,.50,-.19),(-.16,.42,-.48)],
 "carry-box":[(.22,.15,-.37),(-.22,.15,-.37)],
 "carry-low":[(.18,-.005,-.25),(-.18,-.005,-.25)],
 "carry-wide":[(.38,.17,-.23),(-.38,.17,-.23)],
 "cradle":[(.05,.18,-.27),(-.17,.27,-.22)],
 "offer":[(.10,.26,-.44),(-.10,.26,-.44)],
 "handshake":[(.08,.18,-.40),(-.24,-.055,-.015)],
 "hug":[(.35,.43,-.35),(-.35,.43,-.35)],
 "push":[(.21,.45,-.47),(-.21,.45,-.47)],
 "pull":[(.21,.28,-.31),(-.21,.28,-.31)],
 "sleep":[(.12,.65,-.02),(-.10,.62,-.03)],
 "back-chest":[(.06,.27,-.12),(-.06,.27,-.10)],
 "back-spread":[(.57,.35,-.02),(-.57,.35,-.02)],
 "walk-left":[(.24,-.02,.14),(-.24,.10,-.26)],
 "walk-right":[(.24,.10,-.26),(-.24,-.02,.14)],
 "run-left":[(.25,.20,.20),(-.19,.38,-.30)],
 "run-right":[(.19,.38,-.30),(-.25,.20,.20)],
}

def minimum_geometry(local,hip):
    pos,glob=fk(local,hip); minimum=100.
    segments=[("Hips",(0,.13,0),.115),("Spine",(0,.18,0),.13),("Chest",(0,.145,0),.148),("Neck",(0,.12,0),.048)]
    for side,sign in [("Left",1),("Right",-1)]:
        segments += [(side+"Shoulder",(sign*.10,0,0),.055),(side+"UpperArm",(sign*.265,0,0),.047),(side+"LowerArm",(sign*.235,0,0),.038),(side+"Hand",(sign*.10,0,0),.036),(side+"UpperLeg",(0,-.410,0),.068),(side+"LowerLeg",(0,-.405,0),.051)]
        for joint in ["UpperArm","LowerArm","Hand","UpperLeg","LowerLeg","Foot"]:
            minimum=min(minimum,pos[side+joint][1]-(.058 if 'Leg' in joint else .043))
        for x in [-.064,.064]:
            for y in [-.070,.020]:
                for z in [-.185,.065]: minimum=min(minimum,(pos[side+"Foot"]+rotate(glob[side+"Foot"],(x,y,z)))[1])
    for name,end,radius in segments:
        length=np.linalg.norm(end);height=max(2*radius,length*.92)
        center=pos[name]+rotate(glob[name],vec(end)*.5)
        extent=radius+(height*.5-radius)*abs(rotate(glob[name],unit(end))[1])
        minimum=min(minimum,center[1]-extent)
    center=pos['Head']+rotate(glob['Head'],(0,.087,0));extent=math.sqrt(sum((rotate(glob['Head'],axisvec)[1]*r)**2 for axisvec,r in [((1,0,0),.108),((0,1,0),.1275),((0,0,1),.108)]));minimum=min(minimum,center[1]-extent)
    return minimum

def build(name,category,base='stand',arms='relaxed',description='',**changes):
    cfg=copy.deepcopy(BASES[base]);cfg.update(changes);hip=vec((0,cfg['hip'],0));body=euler(*cfg.get('body',(0,0,0)))
    local={n:I.copy() for n,p,o in BONES};local['Hips']=body;local['Spine']=anatomical_euler(*cfg.get('spine',(0,0,0)));local['Chest']=anatomical_euler(*cfg.get('chest',(0,0,0)));local['Head']=anatomical_euler(*cfg.get('head',(0,0,0)))
    for j,(side,sign) in enumerate([('Left',1),('Right',-1)]):
        pos,glob=fk(local,hip);start=pos[side+'UpperLeg']
        target=hip+vec(cfg['feet_world'][j]) if 'feet_world' in cfg else hip+rotate(body,cfg['feet'][j])
        hint=cfg.get('knees',[(0,0,-1),(0,0,-1)])[j]
        if not 'feet_world' in cfg: hint=rotate(body,hint)
        knee,end=solve(start,target,.410,.405,hint)
        upper=align((0,-1,0),knee-start);lower=align((0,-1,0),end-knee)
        local[side+'UpperLeg']=mul(inv(body),upper);local[side+'LowerLeg']=mul(inv(upper),lower)
        foot=I.copy()
        if cfg.get('lying'):
            footdir=rotate(body,(0,-1,0));footdir[1]=0
            if np.linalg.norm(footdir)>1e-5:foot=axis((0,1,0),math.degrees(math.atan2(-footdir[0],-footdir[2])))
        local[side+'Foot']=mul(inv(lower),foot)
        pos,glob=fk(local,hip);start=pos[side+'UpperArm'];targets=cfg.get('arms_targets',ARMS[arms])
        target=hip+vec(cfg['arms_world'][j]) if 'arms_world' in cfg else hip+rotate(body,targets[j])
        elbowhint=vec(cfg.get('elbows',[(1,-.35,-.15),(-1,-.35,-.15)])[j]);elbowhint=rotate(body,elbowhint)
        elbow,end=solve(start,target,.265,.235,elbowhint)
        rest=(sign,0,0);upper=align(rest,elbow-start);lower=align(rest,end-elbow)
        local[side+'UpperArm']=mul(inv(glob[side+'Shoulder']),upper);local[side+'LowerArm']=mul(inv(upper),lower);local[side+'Hand']=I.copy()
    # Ground to the visible skin, never to an imaginary standing capsule. For chair
    # poses only a tiny numerical foot correction is expected; sitting height is
    # retained while floor/sleep poses use their actual lowest support point.
    shift=.006-minimum_geometry(local,hip)
    if cfg.get('floating'):shift=max(0,shift)
    hip[1]+=shift
    slug='pose-'+''.join(c.lower() if c.isalnum() else '-' for c in name).strip('-')
    while '--' in slug:slug=slug.replace('--','-')
    tags=[category.lower(),base.replace('-',' '),arms.replace('-',' ')]+name.lower().replace('/',' ').split()
    description=description or f"Static {name.lower()} blocking pose. No animation clip is added."
    data={'Name':name,'CatalogId':slug,'RootPosition':[round(float(v),7) for v in hip-vec((0,.94,0))], 'BoneRotations':{bone:[round(float(v),8) for v in unitq(local[bone])] for bone,p,o in BONES}}
    return {'Id':slug,'Name':name,'Category':category,'Tags':sorted(set(tags)),'Description':description,'Pose':data}

def main():
    poses=[]
    def add(name,cat,base='stand',arms='relaxed',**kw):poses.append(build(name,cat,base,arms,**kw))
    for name,base,arms,kw in [
        ('Stand relaxed','stand','relaxed',{}),('Stand hands on hips','stand','hips',{}),('Stand arms crossed','stand','crossed',{}),('Stand hands behind back','stand','behind',{}),('Stand hands in pockets','stand','pockets',{}),('Stand weight on one leg','contrapposto','relaxed',{}),('Stand wide','wide','relaxed',{}),('Stand look left','stand','relaxed',{'head':(0,55,0)}),('Stand look right','stand','relaxed',{'head':(0,-55,0)}),('Stand look up','stand','relaxed',{'head':(-30,0,0)}),('Stand look down','stand','relaxed',{'head':(30,0,0)}),('Stand lean forward','stand','relaxed',{'spine':(22,0,0)}),('Stand lean sideways','wide','relaxed',{'spine':(0,0,18)}),('Stand waiting','contrapposto','behind',{}),('Stand formal','stand','prayer',{})]:add(name,'Standing',base,arms,**kw)
    chair_note='Static seated pose for an approximately 0.45 m chair seat at average adult scale. Position the chair beneath the pelvis; no automatic prop-contact solving.'
    for name,base,arms,kw in [
        ('Sit upright','chair','lap',{}),('Sit hands together','chair','lap-joined',{}),('Sit hands on thighs','chair','thighs',{}),('Sit at dining table','chair','desk',{}),('Sit at desk','chair','desk',{'spine':(8,0,0),'head':(12,0,0)}),('Sit on armchair','chair','armrests',{}),('Sit relaxed','chair-wide','armrests',{'spine':(-10,0,0)}),('Sit leaning forward','chair-wide','thighs',{'spine':(23,0,0)}),('Sit thinking','chair','chin',{'spine':(8,0,0)}),('Sit tired','chair','tired',{'spine':(15,0,0),'head':(15,0,0)}),('Sit reading','chair','read',{'head':(20,0,0)}),('Sit using phone','chair','phone-two',{'head':(24,0,0)}),('Sit talking','chair','explain',{}),('Sit arms crossed','chair','crossed',{}),('Sit ankles crossed','chair-ankles','lap-joined',{}),('Sit left ankle over knee','chair-cross-left','lap',{}),('Sit right ankle over knee','chair-cross-right','lap',{}),('Sit looking left','chair','thighs',{'head':(0,50,0),'chest':(0,15,0)}),('Sit looking right','chair','thighs',{'head':(0,-50,0),'chest':(0,-15,0)}),('Sit driving','chair','steering',{}),('Sit on high stool','stool','thighs',{}),('Sit perched on stool','stool-perch','lap',{}),('Sit on low stool','low-seat','lap',{}),('Sit holding a box','chair','carry-box',{}),('Sit holding a child','chair','cradle',{}),('Sit hands behind head','chair-wide','hands-behind-head',{'spine':(-8,0,0)})]:add(name,'Sitting',base,arms,description=chair_note,**kw)
    for name,base,arms,kw in [('Floor sit legs extended','floor-legs','thighs',{}),('Floor sit cross-legged','floor-cross','lap',{}),('Floor sit meditation','floor-cross','prayer',{}),('Floor sit reading','floor-cross','read',{'head':(15,0,0)}),('Floor sit knees up','floor-knees','knees-hug',{}),('Floor sit leaning back','floor-legs','floor-support',{'spine':(-12,0,0)}),('Floor sit legs to side','floor-side','lap',{}),('Floor sit talking','floor-cross','explain',{})]:add(name,'Floor sitting',base,arms,**kw)
    for name,base,arms,kw in [('Kneel upright','kneel','relaxed',{}),('Kneel hands on thighs','kneel','thighs',{}),('Kneel low','kneel-low','lap',{}),('Kneel praying','kneel','prayer',{}),('Kneel on left knee','kneel-right','relaxed',{}),('Kneel on right knee','kneel-left','relaxed',{}),('Kneel reaching forward','kneel','offer',{'spine':(12,0,0)}),('Kneel holding object','kneel','carry-box',{}),('Kneel inspecting ground','kneel-left','phone',{'spine':(22,0,0),'head':(25,0,0)})]:add(name,'Kneeling',base,arms,**kw)
    for name,base,arms,kw in [('Squat low','squat','lap',{}),('Squat arms forward','squat','offer',{}),('Squat hands on knees','squat','thighs',{}),('Crouch ready','crouch','guard',{}),('Crouch looking down','crouch','relaxed',{'head':(25,0,0)}),('Crouch reaching forward','crouch','offer',{}),('Lunge left leg forward','lunge-left','hips',{}),('Lunge right leg forward','lunge-right','hips',{})]:add(name,'Crouching',base,arms,**kw)
    for name,base,arms,kw in [('Lie on back','back','relaxed',{}),('Lie on back hands on chest','back','back-chest',{}),('Lie on back arms spread','back','back-spread',{}),('Lie on back knees bent','back-knees','relaxed',{}),('Lie on left side','side-left','back-chest',{}),('Lie on right side','side-right','back-chest',{}),('Sleep on left side','fetal-left','sleep',{}),('Sleep on right side','fetal-right','sleep',{}),('Lie on stomach','prone','relaxed',{}),('Lie on stomach hands near head','prone','sleep',{})]:add(name,'Lying',base,arms,**kw)
    for name,arms,kw in [('Point forward left hand','point-left',{}),('Point forward right hand','point-right',{}),('Point left','point-side-left',{}),('Point right','point-side-right',{}),('Wave left hand','wave-left',{}),('Wave right hand','wave-right',{}),('Stop gesture','stop',{}),('Open arms','open',{}),('Explain with hands','explain',{}),('Shrug','shrug',{}),('Hands raised','surrender',{}),('Hands on head','hands-head',{}),('Hands behind head','hands-behind-head',{}),('Clapping pose','clap',{}),('Cheer','cheer',{}),('Praying hands','prayer',{}),('Phone at ear','phone-ear',{}),('Use phone standing','phone-two',{'head':(20,0,0)}),('Read standing','read',{'head':(18,0,0)}),('Reach overhead left','reach-up-left',{}),('Reach overhead right','reach-up-right',{}),('Reach overhead both hands','reach-up',{})]:add(name,'Gestures','stand',arms,**kw)
    for name,base,arms,kw in [('Walk left foot forward','walk-left','walk-left',{}),('Walk right foot forward','walk-right','walk-right',{}),('Run left knee forward','run-left','run-left',{'floating':True}),('Run right knee forward','run-right','run-right',{'floating':True}),('Kick left','kick-left','guard',{}),('Kick right','kick-right','guard',{}),('Boxing guard','wide','guard',{}),('Punch left','wide','punch-left',{'chest':(0,-12,0)}),('Punch right','wide','punch-right',{'chest':(0,12,0)}),('Plank support','plank','relaxed',{}),('Hands and knees','crawl','relaxed',{})]:add(name,'Action',base,arms,**kw)
    for name,arms,kw in [('Carry box','carry-box',{}),('Carry object low','carry-low',{}),('Carry large object','carry-wide',{}),('Cradle child','cradle',{}),('Offer an object','offer',{}),('Handshake ready','handshake',{}),('Hug ready','hug',{}),('Push forward','push',{'spine':(10,0,0)}),('Pull an object','pull',{'spine':(-8,0,0)})]:add(name,'Interaction','stand',arms,**kw)
    ids=[p['Id'] for p in poses];assert len(ids)==len(set(ids))
    for p in poses:
        assert len(p['Pose']['BoneRotations'])==21
        assert all(abs(np.linalg.norm(q)-1)<1e-6 for q in p['Pose']['BoneRotations'].values())
        assert all(math.isfinite(x) for q in p['Pose']['BoneRotations'].values() for x in q)
    DEST.parent.mkdir(parents=True,exist_ok=True);DEST.write_text(json.dumps(poses,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'Authored {len(poses)} static poses; categories: '+', '.join(f'{c}: {sum(p["Category"]==c for p in poses)}' for c in sorted({p['Category'] for p in poses})))

if __name__=='__main__':main()
