"""Build the minimal sitting Sharpie emoji family from native SVG masters."""
from pathlib import Path
import copy,hashlib,json,os,subprocess,sys,zipfile
import xml.etree.ElementTree as E
from PIL import Image,ImageDraw
HERE=Path(__file__).resolve().parent;ASSETS=HERE.parent;SCR=HERE.parents[3]/'work/sharpie-minimal-emoji';SCR.mkdir(parents=True,exist_ok=True)
for key in ('TMPDIR','TMP','TEMP'):os.environ[key]=str(SCR)
# Shared expression vocabulary is authored by the primary pack builder.
subprocess.run([sys.executable,str(ASSETS/'discord/build_pack.py')],check=True)
for folder in ('svg','png-128','png-512'):(HERE/folder).mkdir(exist_ok=True)
NS='http://www.w3.org/2000/svg';E.register_namespace('',NS)
source=ASSETS/'approved/svg/sharpie-minimal.svg';base=E.parse(source).getroot()
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
  group=E.Element('{'+NS+'}g',{'transform':'translate(5.76 -7.88) scale(.77)','id':'expression'})
  for child in list(expression)[8:]:group.append(copy.deepcopy(child))
  detail.insert(5,group)
 f=HERE/'svg'/f'sharpie_min_{name}.svg';E.ElementTree(r).write(f,encoding='unicode',xml_declaration=True)
 subprocess.run(['inkscape',str(f),'--export-width=512',f'--export-filename={HERE}/png-512/{f.stem}.png'],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 im=Image.open(HERE/'png-512'/f'{f.stem}.png').convert('RGBA');p=HERE/'png-128'/f'{f.stem}.png';im.resize((128,128),Image.Resampling.LANCZOS).save(p,optimize=True)
 out=Image.open(p);assert out.mode=='RGBA' and out.size==(128,128) and p.stat().st_size<256*1024
 items.append({'name':f.stem,'file':'png-128/'+p.name,'bytes':p.stat().st_size,'alpha_bounds':list(out.getchannel('A').getbbox()),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
(HERE/'manifest.json').write_text(json.dumps({'source':'../approved/svg/sharpie-minimal.svg','source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'expression_source':'../discord/svg/','format':'static transparent PNG','size':[128,128],'framing':{'target_long_edge_px':126,'viewBox':[float(x) for x in base.get('viewBox').split()]},'emoji':items},indent=2)+'\n')
canvas=Image.new('RGB',(1152,760),'#E5E7EB');draw=ImageDraw.Draw(canvas)
for row,bg in enumerate(('#E5E7EB','#313338')):
 draw.rectangle((0,row*380,1152,(row+1)*380),fill=bg)
 for i,item in enumerate(items):
  x=i%8*144;y=row*380+i//8*185;im=Image.open(HERE/item['file']);canvas.paste(im,(x+8,y+8),im);tiny=im.resize((32,32),Image.Resampling.LANCZOS);canvas.paste(tiny,(x+56,y+140),tiny);draw.text((x+7,y+171),item['name'].replace('sharpie_min_',''),fill='#17333E' if row==0 else '#FFFFFF')
canvas.save(HERE/'sharpie-minimal-discord-preview.png')
with zipfile.ZipFile(HERE/'sharpie-minimal-discord-emoji.zip','w',zipfile.ZIP_DEFLATED) as z:
 for item in items:z.write(HERE/item['file'],Path(item['file']).name)
print(f'{len(items)} minimal emoji validated; max {max(x["bytes"] for x in items)} bytes')
