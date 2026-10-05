"""Rebuild review assets from immutable sources; not an application animation player."""
from pathlib import Path
import json, hashlib
import numpy as np
from PIL import Image, ImageDraw, ImageFont
ROOT=Path(__file__).resolve().parents[1]
CFG=json.loads((ROOT/'alignment.json').read_text(encoding='utf-8-sig'))
SIZE=tuple(CFG['canvas'])
frames=ROOT/'frames'; previews=ROOT/'previews'
frames.mkdir(exist_ok=True); previews.mkdir(exist_ok=True)

def load(name):
    im=Image.open(ROOT/'sources'/f'{name}.png').convert('RGBA')
    assert im.size==SIZE
    return im

def mask_boxes(boxes, feather=6):
    # Feather inward, so changes are strictly bounded by the declared region.
    out=np.zeros((SIZE[1],SIZE[0]),dtype=np.float32)
    for x0,y0,x1,y1 in boxes:
        y,x=np.mgrid[y0:y1,x0:x1]
        ramp=np.minimum.reduce([x-x0+1,x1-x,y-y0+1,y1-y])/feather
        out[y0:y1,x0:x1]=np.maximum(out[y0:y1,x0:x1],np.clip(ramp,0,1))
    return Image.fromarray((out*255).astype('uint8'))

def aligned(im,p):
    r=CFG['reference']; sx=p['scale_x']; result=Image.new('RGBA',SIZE)
    # Separate head and body vertical scales: one uniform translation cannot
    # simultaneously align the head top, red collar, and feet of this AI redraw.
    for y0,y1,sa,sb,ta,tb in [
        (0,r['collar_y'],p['head_top'],p['collar_y'],r['head_top'],r['collar_y']),
        (r['collar_y'],SIZE[1],p['collar_y'],p['foot_y'],r['collar_y'],r['foot_y'])]:
        sy=(sb-sa)/(tb-ta)
        coeff=(1/sx,0,p['collar_x']-(r['collar_x']+p['dx'])/sx,
               0,sy,sa+(y0-ta-p['dy'])*sy)
        strip=im.transform((SIZE[0],y1-y0),Image.Transform.AFFINE,coeff,Image.Resampling.BICUBIC)
        result.paste(strip,(0,y0))
    return result

def match_lower_torso_edge(im, master):
    # Only the lower torso, never the raised paw: gradually connect its contour
    # to the fixed rump. Row-local resampling avoids a hard contour ledge.
    src=np.array(im); ref=np.array(master); result=im.copy()
    for y in range(780,840):
        a=np.where(src[y,600:800,3]>128)[0]
        b=np.where(ref[y,600:800,3]>128)[0]
        if not len(a) or not len(b): continue
        source_edge=int(a[-1])+600; target_edge=int(b[-1])+600
        t=min(1.0,(y-780)/45); ease=t*t*(3-2*t)
        edge=source_edge+ease*(target_edge-source_edge)
        scale=(edge-600)/(source_edge-600)
        row=im.transform((SIZE[0]-600,1),Image.Transform.AFFINE,
                         (1/scale,0,600,0,1,y),Image.Resampling.BICUBIC)
        result.paste(row,(600,y))
    return result

base=load('idle'); out={'idle':base}
# Arm-free lower interior on a fixed master silhouette. The old hanging paw
# is removed with clean white body texture sampled from the master itself;
# no AI-redrawn rump/tail contour is copied into this plate.
clean=base.copy(); texture=base.crop((540,730,600,790))
for y in range(790,920,60):
    for x in range(600,754,60): clean.paste(texture,(x,y))
# Taper the erasure inward below the hanging paw: the hip contour curves left.
neutral=Image.composite(clean,base,mask_boxes([(600,790,754,890),(600,874,740,920)],6))
eye_mask=mask_boxes([CFG['eye_roi']],8)
for name in ['blink-half','blink-closed','happy']:
    out[name]=Image.composite(load(name),base,eye_mask)
for name,p in CFG['poses'].items():
    a=aligned(load(name),p)
    out[name+'-aligned']=a
    a=match_lower_torso_edge(a,base)
    boxes=[(435,650,875,840)]
    if name=='greeting-high': boxes += [(700,580,855,660)]
    candidate=Image.composite(a,neutral,mask_boxes(boxes,6))
    pixels=np.array(candidate); master=np.array(base)
    pixels[833:,755:]=master[833:,755:]  # exact fixed rump and tail
    pixels[920:]=master[920:]            # exact fixed leg junction and feet
    out[name]=Image.fromarray(pixels)
for name,im in out.items(): im.save(frames/f'{name}.png')

# Shared crop and resize: do not independently crop each pose.
CROP=tuple(CFG['crop']); thumb_size=(198,300)
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',16)
def thumb(im): return im.crop(CROP).resize(thumb_size,Image.Resampling.LANCZOS)
def panel(ims,labels):
    image=Image.new('RGB',(len(ims)*230,365),'#e9edf0'); d=ImageDraw.Draw(image)
    for i,(im,label) in enumerate(zip(ims,labels)):
        d.text((i*230+14,14),label,fill='#252c35',font=font)
        bg=Image.new('RGBA',(230,320),'#f4f2eb')
        bg.alpha_composite(thumb(im),(16,10))
        image.paste(bg.convert('RGB'),(230*i,42))
        baseline=round(42+10+(CFG['reference']['foot_y']-CROP[1])*thumb_size[1]/(CROP[3]-CROP[1]))
        d.line((i*230,baseline,(i+1)*230,baseline),fill='#dc8677')
    return image
seqs={'blink':['idle','blink-half','blink-closed','blink-half','idle'],
      'greeting':['idle','greeting-low','greeting-high','greeting-low','idle']}
for kind,names in seqs.items():
    variants=[]
    for name in names:
        raw=load(name); adjusted=out[name]
        variants.append(panel([raw,adjusted],['Original','Stabilized candidate']))
    variants[0].save(previews/f'{kind}-comparison.gif',save_all=True,append_images=variants[1:],
                     duration=CFG[kind+'_ms'],loop=0,optimize=False,disposal=2)
    # Desktop-scale, two contrasting backgrounds; opaque GIF preview only.
    animation=[]
    for name in names:
        bg=Image.new('RGB',(520,260),'#f4f2eb'); bg.paste('#242b35',(260,0,520,260))
        spr=out[name].crop(CROP).resize((139,210),Image.Resampling.LANCZOS)
        bg.paste(spr,(60,25),spr); bg.paste(spr,(320,25),spr)
        animation.append(bg)
    animation[0].save(previews/f'{kind}-desktop.gif',save_all=True,append_images=animation[1:],
                      duration=CFG[kind+'_ms'],loop=0,optimize=False,disposal=2)
(previews/'greeting-rump-fixed.gif').write_bytes((previews/'greeting-desktop.gif').read_bytes())
panel([out[n] for n in ['idle','blink-half','blink-closed','greeting-low','greeting-high']],
      ['Idle','Half blink','Closed blink','Low arm','High arm']).save(previews/'contact-sheet.png')
panel([base,Image.blend(base,out['greeting-high-aligned'],.5),out['greeting-high']],
      ['Master','Aligned overlay 50%','Torso composite candidate']).save(previews/'alignment-review.png')

# Independently verifiable invariants.
base_arr=np.array(base)
roi=CFG['eye_roi']; allowed=np.array(eye_mask)>0
for n in ['blink-half','blink-closed','happy']:
    assert np.array_equal(np.array(out[n])[~allowed],base_arr[~allowed]),n
for n in CFG['poses']:
    arr=np.array(out[n]); assert np.array_equal(arr[920:],base_arr[920:])
    assert np.array_equal(arr[833:,755:],base_arr[833:,755:])
    assert np.array_equal(arr[:570],base_arr[:570])
report={'canvas':SIZE,'crop':CROP,'frames':{},'invariants':[
    'Blink pixels outside eye ROI are identical to the master.',
    'Stabilized greeting pixels above y570, below y920 and rump/tail x>=755,y>=833 are identical to the master.',
    'All frames use one canvas and one review crop.'
], 'status':'review candidates, not production-approved'}
for n,im in out.items():
    a=np.array(im); bbox=Image.fromarray((a[:,:,3]>128).astype('uint8')*255).getbbox()
    report['frames'][n]={'size':im.size,'alpha_bbox_gt128':bbox}
for entry in json.loads((ROOT/'sources/manifest.json').read_text(encoding='utf-8')):
    assert hashlib.sha256((ROOT/'sources'/f"{entry['name']}.png").read_bytes()).hexdigest()==entry['sha256']
for kind in seqs:
    with Image.open(previews/f'{kind}-desktop.gif') as gif:
        assert gif.n_frames==5
        for i in range(5):
            gif.seek(i); assert gif.info['duration']==CFG[kind+'_ms'][i]
(previews/'verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('PASS: 8 immutable originals; 8 RGBA frames; eye/head/rump/tail/feet invariants; GIF frame counts and timings.')


