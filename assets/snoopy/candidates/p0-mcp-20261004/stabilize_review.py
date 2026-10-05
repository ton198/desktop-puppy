from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont
import numpy as np,json
root=Path(__file__).parent
master=Image.open('assets/snoopy/frames/idle.png').convert('RGBA')
records=json.loads((root/'technical-review.json').read_text())
out=root/'review-frames';out.mkdir(exist_ok=True)
face_names={'click-smug','hit-startle','hit-recover','special-anticipate','special-tongue','special-settle'}
checks=[]
for rec in records['frames']:
 name=rec['name'];im=Image.open(root/f'{name}.aligned.png').convert('RGBA');mask=None
 if name in face_names:
  roi=Image.new('L',master.size);d=ImageDraw.Draw(roi);d.rectangle((480,195,715,390),fill=255);d.rectangle((440,415,720,565),fill=255)
  inner=master.getchannel('A').filter(ImageFilter.MinFilter(31))
  mask=Image.fromarray(np.minimum(np.array(roi),np.array(inner)))
  mask=Image.fromarray(np.minimum(np.array(mask),np.array(mask.filter(ImageFilter.GaussianBlur(3)))))
  result=Image.composite(im,master,mask)
 elif name=='drag-lift':
  mask=Image.new('L',master.size);d=ImageDraw.Draw(mask);d.rectangle((0,930,1253,1253),fill=255)
  for y in range(900,930):d.line((0,y,1253,y),fill=int((y-900)/30*255))
  result=Image.composite(im,master,mask)
 elif name=='greeting-mid':
  mask=Image.new('L',master.size);d=ImageDraw.Draw(mask);d.rectangle((435,630,900,919),fill=255)
  # Preserve rump/tail and all legs, matching prior greeting review invariants.
  d.rectangle((755,833,1253,1253),fill=0)
  mask=Image.fromarray(np.minimum(np.array(mask),np.array(mask.filter(ImageFilter.GaussianBlur(3)))))
  result=Image.composite(im,master,mask)
 elif name=='landing':
  raw=Image.open(root/f'{name}.raw.png').convert('RGBA');s=rec['scale'];dx=rec['translation'][0]
  foot=records['master']['bbox'][3]-1;rawfoot=rec['raw_anchors']['bbox'][3]-1;dy=foot-s*rawfoot
  result=raw.transform(master.size,Image.Transform.AFFINE,(1/s,0,-dx/s,0,1/s,-dy/s),Image.Resampling.BICUBIC)
 else:
  # Intentional recoil motion uses foot baseline, not fixed head position.
  ar=np.array(im);ys=np.where(ar[:,:,3]>128)[0];dy=(records['master']['bbox'][3]-1)-int(ys.max())
  result=im.transform(master.size,Image.Transform.AFFINE,(1,0,0,0,1,-dy),Image.Resampling.BICUBIC)
 # Clear faint isolated exterior artifacts outside the visible character bounding box.
 if mask is None:
  ar=np.array(result);ys,xs=np.where(ar[:,:,3]>128);keep=np.zeros(ar.shape[:2],bool);keep[max(0,ys.min()-3):ys.max()+4,max(0,xs.min()-3):xs.max()+4]=True;ar[~keep]=0;result=Image.fromarray(ar)
 result.save(out/f'{name}.png')
 a=np.array(result);b=np.array(master)
 invariant=True if mask is None else bool(np.array_equal(a[np.array(mask)==0],b[np.array(mask)==0]))
 checks.append(dict(name=name,unchanged_outside_edit_mask=invariant if mask is not None else None,alpha_extrema=list(result.getchannel('A').getextrema()),status='candidate awaiting visual review'))
# Fixed review crop and two backgrounds, no per-pose scaling.
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',16)
for bg,label in [('#edece7','light'),('#253043','dark')]:
 sheet=Image.new('RGB',(1250,780),bg);d=ImageDraw.Draw(sheet)
 for i,rec in enumerate(records['frames']):
  name=rec['name'];im=Image.open(out/f'{name}.png');thumb=im.crop((250,80,1000,1200)).resize((225,336),Image.Resampling.LANCZOS);x=(i%5)*250+12;y=(i//5)*390+35;sheet.paste(thumb,(x,y),thumb);d.text((x,y-26),name,font=font,fill='white' if label=='dark' else '#172033')
 sheet.save(root/f'contact-sheet-{label}.png')
# Build playback review GIFs at approximate existing 190x150 pet area, and readable larger panels.
sequences={'drag':[('idle',900),('drag-lift',700),('landing',120),('idle',700)],'greeting':[('idle',900),('greeting-low',100),('greeting-mid',100),('greeting-high',450),('greeting-mid',100),('greeting-low',100),('idle',700)],'click':[('idle',900),('click-smug',550),('happy',350),('idle',700)],'hit':[('idle',900),('hit-startle',90),('hit-recoil',150),('hit-recover',650),('idle',700)],'special':[('idle',900),('special-anticipate',150),('special-tongue',650),('special-settle',350),('idle',700)]}
for seq,poses in sequences.items():
 frames=[];dur=[]
 for name,ms in poses:
  p=out/f'{name}.png';p=p if p.exists() else Path('assets/snoopy/frames')/f'{name}.png';im=Image.open(p).convert('RGBA').crop((250,80,1000,1200))
  canvas=Image.new('RGB',(500,370),'#edece7');d=ImageDraw.Draw(canvas);d.text((12,10),seq+' / '+name,font=font,fill='#172033')
  big=im.resize((201,300),Image.Resampling.LANCZOS);canvas.paste(big,(20,48),big)
  small=im.resize((94,140),Image.Resampling.LANCZOS);canvas.paste(small,(330,185),small)
  frames.append(canvas);dur.append(ms)
 frames[0].save(root/f'{seq}-review.gif',save_all=True,append_images=frames[1:],duration=dur,loop=0,disposal=2)
(root/'stabilization-checks.json').write_text(json.dumps(checks,indent=2),encoding='utf-8')
print(json.dumps(checks,indent=2))
