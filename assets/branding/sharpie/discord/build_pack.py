"""Build static Discord emoji from the approved primary SVG; no image-generation drift.
Requires Python 3, Pillow and Inkscape. Run from any directory. Uses disk-backed work/.
"""
from pathlib import Path
import copy, hashlib, json, os, subprocess, zipfile
import xml.etree.ElementTree as ET
from PIL import Image, ImageDraw, ImageFont
HERE=Path(__file__).resolve().parent
ASSETS=HERE.parent
REPO=HERE.parents[3]
SCR=REPO/'work/sharpie-emoji';SCR.mkdir(parents=True,exist_ok=True)
for k in ('TMPDIR','TMP','TEMP'):os.environ[k]=str(SCR)
for d in ('svg','png-128','png-512'): (HERE/d).mkdir(exist_ok=True)
NS='http://www.w3.org/2000/svg';ET.register_namespace('',NS)
INK='#17333E';MINT='#00DDB3';WHITE='#FFFFFF'
source=ASSETS/'approved/svg/sharpie-primary.svg'
base=ET.parse(source).getroot()
# Fit the visible primary silhouette, not its padded 512-unit identity canvas.
measures=json.loads((ASSETS/'approved/provenance/measurements.json').read_text())
bounds=next(v['rendered_alpha_bounds_512'] for v in measures['variants'] if v['name']=='sharpie-primary')
span=max(bounds[2]-bounds[0],bounds[3]-bounds[1])*128/126
cx=(bounds[0]+bounds[2])/2;cy=(bounds[1]+bounds[3])/2
base.set('viewBox',f'{cx-span/2:.6f} {cy-span/2:.6f} {span:.6f} {span:.6f}')
# The primary's clipped facial group is the stable derivative boundary.
detail=base.find(f'{{{NS}}}g/{{{NS}}}g')
assert detail is not None and detail[7].get('id')=='forehead-m'
assert detail[8].get('id')=='eye-left' and detail[13].get('id')=='nose-mouth'
original_eyes=[copy.deepcopy(x) for x in list(detail)[8:13]]
original_mouth=copy.deepcopy(detail[13])
def node(tag,**attrs):return ET.Element('{'+NS+'}'+tag,{k.replace('_','-'):str(v) for k,v in attrs.items()})
def path(d,fill=INK,**attrs):return node('path',d=d,fill=fill,**attrs)
def line(d,width=6,fill=INK):return path(d,'none',stroke=fill,stroke_width=width,stroke_linecap='round',stroke_linejoin='round')
def ellipse(x,y,rx,ry,fill=INK):return node('ellipse',cx=x,cy=y,rx=rx,ry=ry,fill=fill)
def reflect(el):
 g=node('g',transform='translate(338 0) scale(-1 1)');g.append(copy.deepcopy(el));return g
HEART='M108 230 C90 208 77 229 89 242 L108 260 L127 242 C139 229 126 208 108 230 Z'
def eyes(kind):
 if kind=='open':return copy.deepcopy(original_eyes)
 if kind=='wink':return [copy.deepcopy(original_eyes[0]),copy.deepcopy(original_eyes[1]),copy.deepcopy(original_eyes[3]),line('M205 242 Q221 225 239 242')]
 if kind in ('happy','sad','sleep','angry','smug','confused'):
  curves={'happy':'M86 245 Q108 216 130 245','sad':'M86 239 Q108 257 130 239','sleep':'M86 239 Q108 253 130 239','angry':'M86 225 L130 243','smug':'M86 232 L130 232 Q127 253 111 255 Q94 253 86 232','confused':'M86 240 Q108 219 130 232'}
  e=line(curves[kind]) if kind!='smug' else path(curves[kind]);return [e,reflect(e)]
 if kind=='love':
  h=path(HEART,MINT);return [h,reflect(h)]
 if kind=='surprise':return [ellipse(108,239,15,23),ellipse(230,239,15,23),ellipse(112,232,4,5,WHITE),ellipse(234,232,4,5,WHITE)]
 raise ValueError(kind)
def mouth(kind):
 if kind=='default':return [copy.deepcopy(original_mouth)]
 nose=path('M157 269 C155 266 159 265 169 265 C179 265 183 266 181 269 L169 280 Z')
 parts=[nose]
 if kind=='grin':parts+=[path('M140 287 Q169 301 198 287 Q192 316 169 318 Q146 316 140 287 Z'),path('M149 294 Q169 301 189 294 L185 300 L153 300 Z',WHITE)]
 elif kind=='o':parts+=[ellipse(169,300,11,15)]
 elif kind=='frown':parts+=[line('M148 306 Q169 287 190 306',5)]
 elif kind=='flat':parts+=[line('M150 299 L188 299',5)]
 elif kind=='smirk':parts+=[line('M150 299 Q177 311 194 289',5)]
 elif kind=='small':parts+=[ellipse(169,298,5,4)]
 return parts
specs=[('neutral','open','default'),('happy','happy','default'),('laugh','happy','grin'),('wink','wink','default'),('love','love','default'),('sad','sad','frown'),('cry','sad','frown'),('surprised','surprise','o'),('thinking','open','flat'),('sleepy','sleep','small'),('annoyed','angry','flat'),('smug','smug','smirk'),('confused','confused','small'),('sweat','open','default'),('sparkle','open','default'),('grin','open','grin')]
manifest=[]
for name,ek,mk in specs:
 r=copy.deepcopy(base);r.find('{'+NS+'}title').text='Sharpie '+name
 r.find('{'+NS+'}desc').text='Static Discord emoji derived from approved Sharpie primary. '+name.capitalize()+' expression.'
 d=r.find(f'{{{NS}}}g/{{{NS}}}g')
 for child in list(d)[8:]:d.remove(child)
 for el in eyes(ek)+mouth(mk):d.append(el)
 if name=='cry':
  tear=path('M100 252 C91 266 88 279 100 281 C113 280 110 266 100 252 Z',MINT);d.extend([tear,reflect(tear)])
 if name=='thinking':d.extend([line('M82 204 L124 194',5),line('M212 201 L250 211',5)])
 if name=='annoyed':d.extend([line('M83 209 L130 222',5),line('M208 222 L255 209',5)])
 if name=='confused':d.extend([line('M82 209 L125 200',5),line('M212 203 Q236 198 253 212',5)])
 if name=='sweat':d.append(path('M267 208 C252 231 252 247 266 250 C283 247 279 228 267 208 Z',MINT))
 if name=='sparkle':
  for x in (105,233):d.append(path(f'M{x} 221 L{x+5} 234 L{x+17} 239 L{x+5} 244 L{x} 257 L{x-5} 244 L{x-17} 239 L{x-5} 234 Z',WHITE))
 f=HERE/'svg'/f'sharpie_{name}.svg';ET.ElementTree(r).write(f,encoding='unicode',xml_declaration=True)
 # Render at 512 then downsample with alpha preserved for Discord.
 subprocess.run(['inkscape',str(f),'--export-width=512',f'--export-filename={HERE}/png-512/sharpie_{name}.png'],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 im=Image.open(HERE/'png-512'/f'sharpie_{name}.png').convert('RGBA');im.resize((128,128),Image.Resampling.LANCZOS).save(HERE/'png-128'/f'sharpie_{name}.png',optimize=True)
 p=HERE/'png-128'/f'sharpie_{name}.png';assert p.stat().st_size<256*1024
 small=Image.open(p);assert small.size==(128,128) and small.mode=='RGBA' and small.getpixel((0,0))[3]==0
 manifest.append({'alpha_bounds':list(small.getchannel('A').getbbox()),'name':'sharpie_'+name,'file':'png-128/'+p.name,'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
(HERE/'manifest.json').write_text(json.dumps({'source':'../approved/svg/sharpie-primary.svg','source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'format':'static transparent PNG','size':[128,128],'framing':{'method':'uniform visible-silhouette fit','target_long_edge_px':126,'safety_margin_px':1,'viewBox':[float(x) for x in base.get('viewBox').split()]},'emoji':manifest},indent=2)+'\n')
font=ImageFont.truetype('/usr/share/fonts/TTF/DejaVuSans.ttf',15) if Path('/usr/share/fonts/TTF/DejaVuSans.ttf').exists() else ImageFont.load_default()
canvas=Image.new('RGB',(1152,760),'#E5E7EB');draw=ImageDraw.Draw(canvas)
for row,bg in enumerate(('#E5E7EB','#313338')):
 draw.rectangle((0,row*380,1152,(row+1)*380),fill=bg)
 for i,item in enumerate(manifest):
  x=(i%8)*144;y=row*380+(i//8)*185
  im=Image.open(HERE/item['file']).convert('RGBA');canvas.paste(im,(x+8,y+8),im)
  tiny=im.resize((32,32),Image.Resampling.LANCZOS);canvas.paste(tiny,(x+56,y+140),tiny)
  draw.text((x+7,y+171),item['name'].replace('sharpie_',''),font=font,fill=INK if row==0 else WHITE)
canvas.save(HERE/'sharpie-discord-preview.png')
with zipfile.ZipFile(HERE/'sharpie-discord-emoji.zip','w',zipfile.ZIP_DEFLATED) as z:
 for item in manifest:z.write(HERE/item['file'],Path(item['file']).name)
print(f'{len(manifest)} emoji validated; maximum PNG size {max(x["bytes"] for x in manifest)} bytes')
