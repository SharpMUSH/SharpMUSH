"""Build the minimal sitting Sharpie emoji family from native SVG masters."""
from pathlib import Path
import copy,hashlib,json,os,subprocess,sys,zipfile
import xml.etree.ElementTree as E
from PIL import Image,ImageDraw,ImageFont
HERE=Path(__file__).resolve().parent;ASSETS=HERE.parent;SCR=HERE.parents[3]/'work/sharpie-minimal-emoji';SCR.mkdir(parents=True,exist_ok=True)
for key in ('TMPDIR','TMP','TEMP'):os.environ[key]=str(SCR)
# Shared expression vocabulary is authored by the primary pack builder.
subprocess.run([sys.executable,str(ASSETS/'discord/build_pack.py')],check=True)
for folder in ('svg','png-128','png-512'):(HERE/folder).mkdir(exist_ok=True)
NS='http://www.w3.org/2000/svg';E.register_namespace('',NS)
source=ASSETS/'approved/svg/sharpie-minimal.svg';base=E.parse(source).getroot()
# Positional replacement requires this exact minimal detail layout.
detail=base.find(f'{{{NS}}}g/{{{NS}}}g')
expected_tags=['path','path','g','path','path','ellipse','ellipse','path','path','g']
if detail is None or [x.tag for x in detail]!=['{'+NS+'}'+tag for tag in expected_tags]:
 raise ValueError(f'{source}: unexpected minimal detail structure; refusing positional eye/nose removal')
expected_eyes=[{'cx':'89','cy':'177','rx':'14','ry':'21','fill':'#17333E'}, {'cx':'183','cy':'177','rx':'14','ry':'21','fill':'#17333E'}]
if (detail.get('clip-path')!='url(#silhouette)' or detail[3].get('id')!='forehead-m'
 or [x.attrib for x in list(detail)[5:7]]!=expected_eyes or detail[7].get('id')!='nose'
 or detail[7].get('fill')!='#17333E'
 or any(x.get('id') is not None for i,x in enumerate(detail) if i not in (3,7))):
 raise ValueError(f'{source}: minimal eye/nose boundaries changed; refusing positional removal')
primary_source=ASSETS/'approved/svg/sharpie-primary.svg'
primary_detail=E.parse(primary_source).getroot().find(f'{{{NS}}}g/{{{NS}}}g')
if primary_detail is None or len(primary_detail)!=14 or primary_detail[7].get('id')!='forehead-m':
 raise ValueError(f'{primary_source}: unexpected primary expression boundary')
primary_prefix=[E.tostring(x) for x in list(primary_detail)[:8]]
metrics=json.loads((ASSETS/'approved/provenance/measurements.json').read_text());bounds=next(v['rendered_alpha_bounds_512'] for v in metrics['variants'] if v['name']=='sharpie-minimal')
span=max(bounds[2]-bounds[0],bounds[3]-bounds[1])*128/126;cx=(bounds[0]+bounds[2])/2;cy=(bounds[1]+bounds[3])/2
base.set('viewBox',f'{cx-span/2:.6f} {cy-span/2:.6f} {span:.6f} {span:.6f}')
items=[]
for item in json.loads((ASSETS/'discord/manifest.json').read_text())['emoji']:
 name=item['name'].removeprefix('sharpie_');r=copy.deepcopy(base);detail=r.find(f'{{{NS}}}g/{{{NS}}}g')
 r.find('{'+NS+'}title').text='Sharpie minimal '+name;r.find('{'+NS+'}desc').text='Minimal sitting Sharpie with '+name+' expression.'
 if name!='neutral':
  for child in list(detail)[5:8]:detail.remove(child)
  expression=E.parse(ASSETS/'discord/svg'/f'sharpie_{name}.svg').getroot().find(f'{{{NS}}}g/{{{NS}}}g')
  if expression is None or len(expression)<=8 or [E.tostring(x) for x in list(expression)[:8]]!=primary_prefix:
   raise ValueError(f'sharpie_{name}: primary expression prefix changed; refusing positional expression slicing')
  group=E.Element('{'+NS+'}g',{'transform':'translate(5.76 -7.88) scale(.77)','id':'expression'})
  for child in list(expression)[8:]:group.append(copy.deepcopy(child))
  detail.insert(5,group)
 f=HERE/'svg'/f'sharpie_min_{name}.svg';E.ElementTree(r).write(f,encoding='unicode',xml_declaration=True)
 subprocess.run(['inkscape',str(f),'--export-width=512',f'--export-filename={HERE}/png-512/{f.stem}.png'],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 im=Image.open(HERE/'png-512'/f'{f.stem}.png').convert('RGBA');p=HERE/'png-128'/f'{f.stem}.png';im.resize((128,128),Image.Resampling.LANCZOS).save(p,optimize=True)
 out=Image.open(p);assert out.mode=='RGBA' and out.size==(128,128) and p.stat().st_size<256*1024
 items.append({'name':f.stem,'file':'png-128/'+p.name,'bytes':p.stat().st_size,'alpha_bounds':list(out.getchannel('A').getbbox()),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
(HERE/'manifest.json').write_text(json.dumps({'source':'../approved/svg/sharpie-minimal.svg','source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'expression_source':'../discord/svg/','format':'static transparent PNG','size':[128,128],'framing':{'target_long_edge_px':126,'viewBox':[float(x) for x in base.get('viewBox').split()]},'emoji':items},indent=2)+'\n')
try:font=ImageFont.truetype('DejaVuSans.ttf',15)
except OSError:font=ImageFont.load_default()
canvas=Image.new('RGB',(1152,760),'#E5E7EB');draw=ImageDraw.Draw(canvas)
for row,bg in enumerate(('#E5E7EB','#313338')):
 draw.rectangle((0,row*380,1152,(row+1)*380),fill=bg)
 for i,item in enumerate(items):
  x=i%8*144;y=row*380+i//8*185;im=Image.open(HERE/item['file']);canvas.paste(im,(x+8,y+8),im);tiny=im.resize((32,32),Image.Resampling.LANCZOS);canvas.paste(tiny,(x+56,y+140),tiny);draw.text((x+7,y+171),item['name'].replace('sharpie_min_',''),font=font,fill='#17333E' if row==0 else '#FFFFFF')
canvas.save(HERE/'sharpie-minimal-discord-preview.png')
with zipfile.ZipFile(HERE/'sharpie-minimal-discord-emoji.zip','w',zipfile.ZIP_DEFLATED) as z:
 for item in items:z.write(HERE/item['file'],Path(item['file']).name)
print(f'{len(items)} minimal emoji validated; max {max(x["bytes"] for x in items)} bytes')
