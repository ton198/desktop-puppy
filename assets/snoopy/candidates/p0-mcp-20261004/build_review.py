from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import numpy as np, json, shutil
root=Path('assets/snoopy/candidates/p0-mcp-20261004')
proxy=Path(r'C:\Users\ton20\Documents\works\projects\image-generation-mcp\output\candidates')
items=[('drag-lift','020615Z-031012b5b354'),('landing','020742Z-5c4c00350493'),('greeting-mid','020851Z-0c1d6f746a1c'),('click-smug','020959Z-09e2cb87bf2f'),('hit-startle','021106Z-962dd21fec53'),('hit-recoil','021200Z-72334d71dabe'),('hit-recover','021303Z-1bfe73905459'),('special-anticipate','021408Z-2342f532f582'),('special-tongue','021515Z-9a84380ba197'),('special-settle','021627Z-55d092338c42')]
master=Image.open('assets/snoopy/frames/idle.png').convert('RGBA')
def anchors(im):
 a=np.array(im); alpha=a[:,:,3]; visible=alpha>128
 red=(a[:,:,0]>170)&(a[:,:,1]<100)&(a[:,:,2]<100)&visible
 yy,xx=np.where(red); vy,vx=np.where(visible)
 return dict(collar=[float(xx.mean()),float(yy.mean())],top=int(vy.min()),bbox=[int(vx.min()),int(vy.min()),int(vx.max()+1),int(vy.max()+1)])
ma=anchors(master); records=[]
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',17)
sheet=Image.new('RGB',(5*250,2*390),'#edece7'); draw=ImageDraw.Draw(sheet)
for i,(name,suffix) in enumerate(items):
 src=proxy/f'imagegen-20261005T{suffix}.png'; meta=src.with_suffix('.json')
 shutil.copy2(src,root/f'{name}.raw.png'); shutil.copy2(meta,root/f'{name}.metadata.json')
 im=Image.open(src).convert('RGBA'); an=anchors(im)
 # Uniform registration from head-top and collar, not independent per-frame cropping.
 scale=(ma['collar'][1]-ma['top'])/(an['collar'][1]-an['top'])
 dx=ma['collar'][0]-scale*an['collar'][0]; dy=ma['collar'][1]-scale*an['collar'][1]
 normalized=im.transform(master.size,Image.Transform.AFFINE,(1/scale,0,-dx/scale,0,1/scale,-dy/scale),Image.Resampling.BICUBIC)
 normalized.save(root/f'{name}.aligned.png')
 rec=dict(name=name,raw_anchors=an,scale=scale,translation=[dx,dy],aligned_anchors=anchors(normalized),size=list(im.size),alpha=list(im.getchannel('A').getextrema()),status='visual candidate; not production approved')
 records.append(rec)
 thumb=normalized.crop((250,80,1000,1200)).resize((225,336),Image.Resampling.LANCZOS)
 x=(i%5)*250+12;y=(i//5)*390+35
 sheet.paste(thumb,(x,y),thumb);draw.text((x,y-26),name,font=font,fill='#172033')
 draw.line((x,y+315,x+225,y+315),fill='#db6f6f')
sheet.save(root/'contact-sheet-aligned.png')
(root/'technical-review.json').write_text(json.dumps(dict(master=ma,frames=records),indent=2),encoding='utf-8')
print(json.dumps(records,indent=2))
