from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageSequence
import json, numpy as np
r=Path('assets/snoopy/candidates/greeting-special-v2-20261004')
seqs=json.loads((r/'sequences.json').read_text());checks=json.loads((r/'review-checks.json').read_text())
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
sheets=[]
for bg,label in [('#edece7','light'),('#253043','dark')]:
 sheet=Image.new('RGB',(1260,4*345),bg);d=ImageDraw.Draw(sheet)
 for row,(group,seq) in enumerate(seqs.items()):
  names=list(dict.fromkeys(n for n,_ in seq))
  for col,name in enumerate(names):
   im=Image.open(r/'review-frames'/f'{name}.png').crop((250,80,1050,1200)).resize((200,280),Image.Resampling.LANCZOS)
   sheet.paste(im,(col*210+5,row*345+45),im);d.text((col*210+5,row*345+20),name,font=font,fill='white' if label=='dark' else '#172033')
 sheet.save(r/f'group-summary-{label}.png')
# Fresh, independent verification from saved outputs rather than trusting build logs.
master=np.array(Image.open(r/'review-frames'/'idle.png'));results=[]
for name in sorted({n for seq in seqs.values() for n,_ in seq}):
 p=r/'review-frames'/f'{name}.png';im=Image.open(p);a=np.array(im)
 assert im.size==(1254,1254) and im.mode=='RGBA',name
 assert im.getchannel('A').getextrema()==(0,255),name
 assert np.array_equal(a[920:],master[920:]),name+' legs drift'
 assert np.array_equal(a[:190],master[:190]),name+' head top drift'
 if not name.startswith('greeting'):assert np.array_equal(a[600:],master[600:]),name+' body drift'
 results.append(dict(name=name,size=list(im.size),alpha_extrema=[0,255],legs_equal_master=True,head_top_equal_master=True))
assert len(results)==16 # 12 selected new, 3 reused, 1 idle
playback=[]
for key,seq in seqs.items():
 for mode,mult in [('normal',1),('slow',3)]:
  im=Image.open(r/f'{key}-{mode}.gif');frames=[f.copy() for f in ImageSequence.Iterator(im)];durs=[f.info['duration'] for f in frames]
  assert im.n_frames==len(seq),(key,im.n_frames,len(seq))
  assert durs==[ms*mult for _,ms in seq],(key,durs)
  playback.append(dict(group=key,mode=mode,frames=len(frames),durations=durs))
assert len(list(r.glob('*.metadata.json')))==16
assert all(c['master_equal_outside_group_mask'] for c in checks)
reused={'tongue-peak','glance-peak','smile-peak'}
for c in checks:
 c['visual_status']='rejected: visually too similar to keyframe' if c['rejected'] else ('conditional: motion timing and arm excursion await user review' if c['name'].startswith('greeting') else 'static group review accepted; runtime pending')
(r/'review-checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2),encoding='utf8')
(r/'verification.json').write_text(json.dumps(dict(frames=results,playback=playback,new_selected=12,new_rejected=1,reused=3,completed_local_date='2026-10-05',runtime_verified=False,browser_playback_verified=False),ensure_ascii=False,indent=2),encoding='utf8')
text='''# Greeting / Special 第二版素材候选

整理及恢复检查完成：2026-10-05（America/New_York）。目录沿用开始生成时的20261004命名，不代表重新生成日期。

## 生成记录
- 使用用户指定的 image_generation MCP edit_image，全部请求透明背景。
- 13张新生成候选，入选12张，1张侧眼过渡因变化不足未入选；另复用旧版3张Special关键帧。
- Greeting 5张：起手、过渡、举手到位、内摆、外摆。
- Special新增7张：吐舌3张、侧眼挑眉2张、偷笑2张。
- 原始PNG和完整提示词/参考路径/模型元数据均保留为 *.raw.png、*.metadata.json。
- 每次MCP返回预览均进行视觉检查，整组输出后再看姿势总览和放大的眉眼细节。中断恢复后未重复调用生图，因为全部生成结果已存在。

## 第二次后处理检查
最初矩形蒙版截掉了Greeting高位指尖，并在眉眼交界产生截断笔画。与原始结果比较确认是后处理错误，非生图服务失败。
- Greeting蒙版扩大至抬手最高点；固定母版头顶、脸部、脚部及编辑区之外区域。
- 表情改为识别独立黑色眼睛/眉毛连通部件，连同抗锯齿边缘提取；排除头部外轮廓。不再用水平边界切断眼线。
- 吐舌组固定同一只眼眨眼，只有预备帧重新睁眼；随后只改变嘴部。
- 挑眉组眼神与挑眉分步进行，挑眉只改一侧眉毛。
- 偷笑组保留同一笑容和眉毛，只改变眼睛。
- 最终高位手指和眉眼细节已重新视觉检查，无此前蒙版截断。

## 验收状态
- Greeting：静态手型比上一版统一，但起手切入和外摆伸直幅度仍列为动态待验收。不声称完全流畅。
- Special三组：通过静态组内一致性初验；吐舌、侧眼挑眉、偷笑各自独立，不混为一个动画。
- 本轮自动浏览器检查被file协议安全策略阻止，未尝试绕过。已生成可由用户直接打开的本地HTML及正常/慢速GIF；没有声称完整实时观看或WPF程序内验收通过。
- 未修改C#/WPF功能，未覆盖旧版素材，未接入运行资源。

## 预览及验证
- index.html：4组正常速度、3倍慢速、上一帧/下一帧检查及背景切换。
- *-normal.gif / *-slow.gif：带检查背景的GIF，不是透明运行素材。
- *-strip.png：每组独立姿势总览。
- group-summary-light.png / group-summary-dark.png：最终4组双背景总览。
- review-frames/：透明PNG候选；glance-entry-rejected.png明确未入选。
- build_review.py：可重建全部审查资源；不修改原始素材。
- verification.json：从已保存文件独立检查16张序列用图（含待机、12张新入选、3张复用）、8个GIF的帧数/时长、透明格式和固定区域。
- 所有序列用PNG均1254×1254 RGBA，alpha范围0..255；腿部y>=920及头顶y<190逐像素与母版一致；所有Special身体y>=600与母版一致。全部局部编辑区外不变量检查通过。
'''
(r/'README.md').write_text(text,encoding='utf8')
p=r/'index.html';html=p.read_text(encoding='utf8');html=html.replace('<p>下方有正常速度', '<p><strong>验收提示：</strong>Greeting起手切入、外摆幅度仍待动态确认；Special三组通过静态一致性初验，均未接入程序。</p><p>下方有正常速度');p.write_text(html,encoding='utf8')
print('PASS: 16 sequence PNGs (12 new + 3 reused + idle), 8 GIF frame counts/durations, 16 metadata files, fixed-head/feet/body and group-mask invariants. 1 rejected candidate kept. Runtime/browser playback NOT verified.')
