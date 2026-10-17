"""Render 4x vector edge proofs and validate structure, palette and source reuse."""
from pathlib import Path
import subprocess,json,xml.etree.ElementTree as ET
from PIL import Image,ImageDraw
p=Path('assets/branding/sharpie/approved');scratch=Path('work/sharpie-production');scratch.mkdir(parents=True,exist_ok=True);out=p/'provenance/edge-review';out.mkdir(exist_ok=True)
names=['sharpie-primary','sharpie-primary-badge','sharpie-smile','sharpie-minimal']
for name in names:
 f=p/'svg'/f'{name}.svg';root=ET.parse(f).getroot();assert root.attrib['viewBox']=='0 0 512 512'
 assert all(not el.tag.endswith(('image','script','foreignObject')) for el in root.iter())
 assert {el.attrib['fill'] for el in root.iter() if 'fill' in el.attrib}<={'#17333E','#00DDB3','#FFFFFF'}
 ids=[el.attrib['id'] for el in root.iter() if 'id' in el.attrib];assert len(ids)==len(set(ids))
 temp=scratch/f'{name}-4x.png';subprocess.run(['inkscape',str(f),f'--export-filename={temp}','--export-width=2048'],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 im=Image.open(temp).convert('RGBA');assert im.getpixel((0,0))[3]==0
 proof=Image.new('RGB',(4096,2112),'#E5E7EB');d=ImageDraw.Draw(proof);d.rectangle((2048,0,4096,2112),fill='#10242D')
 proof.paste(im,(0,64),im);proof.paste(im,(2048,64),im);d.text((32,24),f'{name} | 4x native SVG render | light',fill='#17333E');d.text((2080,24),f'{name} | 4x native SVG render | dark',fill='#FFFFFF');proof.save(out/f'{name}-4x.png')
 # A full-width image plus small edge crops makes every contour easy to inspect.
 tiles=Image.new('RGB',(1600,1600),'#E5E7EB');td=ImageDraw.Draw(tiles)
 regions=[(200,80,1000,880),(1000,80,1800,880),(200,1180,1000,1980),(1000,1180,1800,1980)]
 for i,region in enumerate(regions):
  crop=im.crop(region);x=(i%2)*800;y=(i//2)*800;tiles.paste(crop,(x,y),crop)
 tiles.save(out/f'{name}-edge-crops.png')
print('Four 4x light/dark proofs and four edge-crop sheets created. Structure, IDs, palette and transparency passed.')
