from pathlib import Path
from PIL import Image,ImageOps,ImageDraw,ImageFont
import numpy as np
from scipy.ndimage import label,find_objects
import shutil,json
root=Path(__file__).resolve().parents[4]; assets=root/'src/DesktopPuppy/Pet/Animation/Assets'
out=Path(__file__).parent;cand=root/'assets/snoopy/candidates/edge-reveal-v3-20261009'
sheet=Image.open(cand/'generated-sheet.png').convert('RGBA')
labels,n=label(np.asarray(sheet)[:,:,3]>16)
boxes=[]
for k,s in enumerate(find_objects(labels),1):
    if (labels[s]==k).sum()>1000: boxes.append((s[1].start,s[0].start,s[1].stop,s[0].stop))
boxes=sorted(boxes,key=lambda b:(b[1]//444,b[0]))
assert len(boxes)==8
cells=[sheet.crop(b) for b in boxes]
def bbox(im):return im.getchannel('A').point(lambda v:255 if v>128 else 0).getbbox()
original=Image.open(assets/'idle.png').convert('RGBA').crop((250,80,1050,1200))
normal=Image.new('RGBA',(1120,1120));normal.alpha_composite(original,(160,0));normal=normal.resize((1254,1254),Image.Resampling.LANCZOS)
edge=Image.open(assets/'edge-transition-left.png').convert('RGBA');a=bbox(ImageOps.mirror(normal));b=bbox(edge)
frames={}
def place(im,target):
    ink=im.crop(bbox(im));scale=(target[3]-target[1])/ink.height
    ink=ink.resize((round(ink.width*scale),round(ink.height*scale)),Image.Resampling.LANCZOS)
    result=Image.new('RGBA',(1254,1254));result.alpha_composite(ink,(round((target[0]+target[2]-ink.width)/2),round(target[3]-ink.height)))
    return result
for i,ci in enumerate([None,4,3,2,1,0],1):
    t=(i-1)/6;target=[a[k]*(1-t)+b[k]*t for k in range(4)]
    left=ImageOps.mirror(normal) if ci is None else place(cells[ci],target)
    for side,im in [('left',left),('right',ImageOps.mirror(left))]:
        key=f'edge-reveal-{i}-{side}';im.save(assets/f'{key}.png');im.save(cand/f'{key}.png');frames[key]=im
for i,ci in enumerate([4,5,6],1):
    # Fit ink within the intersection of fixed-window clips over this pose's interval.
    ranges=[(.12,.35),(.35,.65),(.65,.88)]
    lo,hi=ranges[i-1];ratio=1254/1120
    left=(161+45*hi+8)*ratio;right=(914+45*lo-8)*ratio
    ink=cells[ci].crop(cells[ci].getchannel('A').getbbox())
    scale=min((right-left)/ink.width,1010*ratio/ink.height)
    ink=ink.resize((round(ink.width*scale),round(ink.height*scale)),Image.Resampling.LANCZOS)
    im=Image.new('RGBA',(1254,1254));im.alpha_composite(ink,(round((left+right-ink.width)/2),round(1049*ratio-ink.height)))
    key=f'turn-{i}';im.save(assets/f'{key}.png');im.save(cand/f'{key}.png');frames[key]=im
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',18)
contact=Image.new('RGB',(1120,470),'#eef2f6');d=ImageDraw.Draw(contact)
for row,side in enumerate(['left','right']):
    for col,i in enumerate(range(6,0,-1)):
        im=frames[f'edge-reveal-{i}-{side}'].resize((165,165),Image.Resampling.LANCZOS)
        contact.paste(im,(col*185+8,row*220+30),im);d.text((col*185+8,row*220+198),f'{side} reveal {7-i}',font=font,fill='#233044')
contact.save(out/'reveal-frames.png')
seq=[]
for side in ['left','right']:
    ims=[Image.open(assets/f'edge-transition-{side}.png').convert('RGBA')]+[frames[f'edge-reveal-{i}-{side}'] for i in range(6,0,-1)]
    for im in ims:
        bg=Image.new('RGB',(240,240),'#eef2f6');thumb=im.resize((230,230),Image.Resampling.LANCZOS);bg.paste(thumb,(5,5),thumb);seq.append(bg)
seq[0].save(out/'reveal-preview.gif',save_all=True,append_images=seq[1:],duration=[500]+[60]*6+[500]+[60]*6,loop=0)
turn=[ImageOps.mirror(normal),frames['turn-1'],frames['turn-2'],frames['turn-3'],normal]
imgs=[]
for im in turn+turn[-2:0:-1]:
    bg=Image.new('RGB',(220,220),'#eef2f6');thumb=im.resize((210,210),Image.Resampling.LANCZOS);bg.paste(thumb,(5,5),thumb);imgs.append(bg)
imgs[0].save(out/'turn-preview.gif',save_all=True,append_images=imgs[1:],duration=[600,60,80,60,600,60,80,60],loop=0)
checks={k:{'size':list(im.size),'ink_bounds':list(bbox(im)),'alpha':list(im.getchannel('A').getextrema())} for k,im in frames.items()}
(cand/'verification.json').write_text(json.dumps(checks,indent=2))
print('Saved 12 reveal frames, 3 turn frames and previews.')
