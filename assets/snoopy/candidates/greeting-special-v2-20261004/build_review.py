from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageSequence
import numpy as np, json, shutil
r=Path('assets/snoopy/candidates/greeting-special-v2-20261004'); proxy=Path(r'C:\Users\ton20\Documents\works\projects\image-generation-mcp\output\candidates');out=r/'review-frames';out.mkdir(exist_ok=True)
items=[('greeting-raised','024150Z-3364044ec543'),('greeting-mid','024256Z-9156a9bad664'),('greeting-start','024404Z-b6a7456460fc'),('greeting-in','024512Z-10520552adb3'),('greeting-out','024606Z-2b790371a3d7'),('tongue-ready','024704Z-99cd722c0771'),('tongue-wink','024805Z-47de3f22bcaa'),('tongue-short','024913Z-f215fd376353'),('glance-entry-rejected','025027Z-0d849a2179de'),('glance-brow','025137Z-f10be73f0a29'),('smile-half','025247Z-7237bf5f5376'),('smile-release','025352Z-ec6f1370aca0'),('glance-entry','025503Z-a68f96eff35b'),('tongue-peak','021515Z-9a84380ba197'),('glance-peak','021408Z-2342f532f582'),('smile-peak','021627Z-55d092338c42')]
master=Image.open('assets/snoopy/frames/idle.png').convert('RGBA'); master.save(out/'idle.png')
def anchor(im):
 a=np.array(im);v=a[:,:,3]>128;red=(a[:,:,0]>170)&(a[:,:,1]<100)&(a[:,:,2]<100)&v;yy,xx=np.where(red);vy,vx=np.where(v);return dict(collar=[float(xx.mean()),float(yy.mean())],top=int(vy.min()),foot=int(vy.max()))
ma=anchor(master);checks=[];images={};inner_alpha=master.getchannel('A').filter(ImageFilter.MinFilter(31))
for name,suffix in items:
 src=proxy/f'imagegen-20261005T{suffix}.png';shutil.copy2(src,r/f'{name}.raw.png');shutil.copy2(src.with_suffix('.json'),r/f'{name}.metadata.json')
 im=Image.open(src).convert('RGBA');an=anchor(im);s=(ma['collar'][1]-ma['top'])/(an['collar'][1]-an['top']);dx=ma['collar'][0]-s*an['collar'][0];dy=ma['collar'][1]-s*an['collar'][1];aligned=im.transform(master.size,Image.Transform.AFFINE,(1/s,0,-dx/s,0,1/s,-dy/s),Image.Resampling.BICUBIC)
 images[name]=aligned
# All facial groups share fixed mouth, left eye, eyebrow areas where motion is not intended.
def rectmask(boxes):
 m=Image.new('L',master.size);d=ImageDraw.Draw(m)
 for box in boxes:
  if box=='eyes':
   d.rectangle((505,270,615,395),fill=255);d.rectangle((610,300,710,395),fill=255)
  else:d.rectangle(box,fill=255)
 inner=inner_alpha;m=Image.fromarray(np.minimum(np.array(m),np.array(inner)));return Image.fromarray(np.minimum(np.array(m),np.array(m.filter(ImageFilter.GaussianBlur(2)))))
eye_both=(505,270,720,405); eye_right=(605,270,720,405); brows=(510,195,725,290); brow_right=(630,195,725,290); mouth=(470,425,700,565)
def feature_mask(im,part):
 a=np.array(im);x0,y0,x1,y1=500,190,740,415
 black=(a[y0:y1,x0:x1,:3].max(axis=2)<160)&(a[y0:y1,x0:x1,3]>128)
 seen=np.zeros_like(black);selected=np.zeros_like(black);h,w=black.shape
 for yy,xx in zip(*np.where(black)):
  if seen[yy,xx]:continue
  stack=[(int(yy),int(xx))];seen[yy,xx]=True;pts=[]
  while stack:
   y,x=stack.pop();pts.append((y,x))
   for dy,dx in [(0,1),(0,-1),(1,0),(-1,0)]:
    ny,nx=y+dy,x+dx
    if 0<=ny<h and 0<=nx<w and black[ny,nx] and not seen[ny,nx]:seen[ny,nx]=True;stack.append((ny,nx))
  ys=[q[0] for q in pts];xs=[q[1] for q in pts]
  if min(xs)==0 or max(xs)==w-1 or min(ys)==0 or max(ys)==h-1:continue
  top=min(ys)+y0;cx=(min(xs)+max(xs))/2+x0
  wanted=(top>=270 and (part=='eyes' or (part=='right-eye' and cx>610))) or (part=='right-brow' and top<270 and cx>630)
  if wanted:
   for y,x in pts:selected[y,x]=True
 m=Image.new('L',im.size);m.paste(Image.fromarray(selected.astype('uint8')*255),(x0,y0));return m.filter(ImageFilter.MaxFilter(9))
def features(source,base,part):
 old=feature_mask(base,part);new=feature_mask(source,part);m=Image.fromarray(np.maximum(np.array(old),np.array(new)))
 # Component masks exclude the head outline and copy only eyes/eyebrows plus antialias padding.
 return Image.composite(source,base,m)
face_masks={}
for name,_ in items:
 if name.startswith('greeting-'):
  m=Image.new('L',master.size);d=ImageDraw.Draw(m);d.rectangle((435,650,945,880),fill=255);d.rectangle((700,590,945,650),fill=255);d.rectangle((500,880,754,919),fill=255);m=Image.fromarray(np.minimum(np.array(m),np.array(m.filter(ImageFilter.GaussianBlur(2)))))
  result=Image.composite(images[name],master,m)
 elif name.startswith('tongue-'):
  peak=features(images['tongue-peak'],master,'eyes')
  peak=Image.composite(images['tongue-peak'],peak,rectmask([mouth]))
  if name=='tongue-peak':result=peak
  elif name=='tongue-ready':result=Image.composite(images[name],features(images[name],peak,'right-eye'),rectmask([mouth]))
  else:result=Image.composite(images[name],peak,rectmask([mouth]))
 elif name.startswith('glance-'):
  peak=features(images['glance-peak'],master,'eyes');peak=Image.composite(images['glance-peak'],peak,rectmask([mouth]))
  if name=='glance-peak':result=peak
  else:result=features(images[name],peak,'right-brow' if name=='glance-brow' else 'eyes')
 else:
  peak=features(images['smile-peak'],master,'eyes');peak=Image.composite(images['smile-peak'],peak,rectmask([mouth]))
  result=peak if name=='smile-peak' else features(images[name],peak,'eyes')
 result.save(out/f'{name}.png')
 # Strong invariant against master outside total permitted group area.
 total=rectmask([eye_both,brows,mouth]) if not name.startswith('greeting-') else m
 a=np.array(result);b=np.array(master);assert np.array_equal(a[np.array(total)==0],b[np.array(total)==0]),name
 checks.append(dict(name=name,size=list(result.size),alpha=list(result.getchannel('A').getextrema()),master_equal_outside_group_mask=True,reused=name in {'tongue-peak','glance-peak','smile-peak'},rejected=name.endswith('rejected')))
seqs={
'greeting':[('idle',800),('greeting-start',90),('greeting-mid',100),('greeting-raised',130),('greeting-in',130),('greeting-raised',90),('greeting-out',130),('greeting-raised',90),('greeting-in',130),('greeting-raised',90),('greeting-out',130),('greeting-raised',140),('greeting-mid',100),('greeting-start',90),('idle',700)],
'special-tongue':[('idle',800),('tongue-ready',120),('tongue-wink',100),('tongue-short',90),('tongue-peak',450),('tongue-short',90),('tongue-wink',100),('tongue-ready',120),('idle',700)],
'special-glance':[('idle',800),('glance-entry',120),('glance-peak',170),('glance-brow',450),('glance-peak',170),('glance-entry',120),('idle',700)],
'special-smile':[('idle',800),('smile-half',100),('smile-peak',450),('smile-release',140),('smile-half',100),('idle',700)]}
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',16);crop=(250,80,1050,1200)
for seq,poses in seqs.items():
 for factor,mode in [(1,'normal'),(3,'slow')]:
  frames=[];dur=[]
  for name,ms in poses:
   im=Image.open(out/f'{name}.png').crop(crop);bg=Image.new('RGB',(540,370),'#edece7');d=ImageDraw.Draw(bg);d.text((12,10),seq+' / '+name,font=font,fill='#172033');big=im.resize((214,300),Image.Resampling.LANCZOS);bg.paste(big,(20,48),big);small=im.resize((100,140),Image.Resampling.LANCZOS);bg.paste(small,(365,205),small);frames.append(bg);dur.append(ms*factor)
  frames[0].save(r/f'{seq}-{mode}.gif',save_all=True,append_images=frames[1:],duration=dur,loop=0,disposal=2)
 # unique pose strip, including idle
 names=list(dict.fromkeys(n for n,_ in poses));sheet=Image.new('RGB',(len(names)*210,340),'#edece7');d=ImageDraw.Draw(sheet)
 for col,name in enumerate(names):
  im=Image.open(out/f'{name}.png').crop(crop).resize((200,280),Image.Resampling.LANCZOS);sheet.paste(im,(col*210+5,35),im);d.text((col*210+5,9),name,font=font,fill='#172033')
 sheet.save(r/f'{seq}-strip.png')
(r/'review-checks.json').write_text(json.dumps(checks,indent=2),encoding='utf8');(r/'sequences.json').write_text(json.dumps(seqs,indent=2),encoding='utf8');print('Copied 13 new raw candidates (1 rejected) and 3 reused keyframes. Created 4 grouped sequences with normal/slow GIFs and static strips. Mask invariants verified.')
