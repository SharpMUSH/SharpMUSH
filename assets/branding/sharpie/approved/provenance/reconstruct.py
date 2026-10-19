"""Rebuild Sharpie's manually refined path masters. Run from repository root.
No raster masks, threshold contours or auto-simplification are used in these masters.
Source-coordinate Bézier curves were refined against the approved source sheets.
"""
from pathlib import Path
import json,hashlib,subprocess,xml.etree.ElementTree as ET
from PIL import Image,ImageDraw
ROOT=Path.cwd();OUT=ROOT/'assets/branding/sharpie/approved';SCR=ROOT/'work/sharpie-production'
SCR.mkdir(parents=True,exist_ok=True)
PALETTE={'ink':'#17333E','mint':'#00DDB3','white':'#FFFFFF'}
P=[]
def path(d,color='ink',id=None):return f'<path{f" id={chr(34)}{id}{chr(34)}" if id else ""} fill="{PALETTE[color]}" d="{d}"/>'
def ellipse(cx,cy,rx,ry,color='ink'):return f'<ellipse cx="{cx}" cy="{cy}" rx="{rx}" ry="{ry}" fill="{PALETTE[color]}"/>'
def mirror(content,axis):return f'<g transform="translate({2*axis} 0) scale(-1 1)">{content}</g>'
# Shared nose + stem + smiling lobes, in primary source coordinates.
MOUTH='M157 269 C155 266 159 265 169 265 C179 265 183 266 181 269 L171 279 L171 287 C176 296 190 300 200 288 C198 300 182 305 169 293 C156 305 140 300 138 288 C148 300 162 296 167 287 L167 279 Z'
PRIMARY_OUTER='M38 2 L37 33 L19 19 L25 62 C8 118 2 155 25 194 L3 191 Q10 211 33 225 L8 244 L33 247 C20 260 13 279 10 297 L29 288 Q24 317 32 337 C61 323 93 326 128 340 L169 358 L209 340 C242 326 274 323 302 337 Q313 318 307 288 L326 297 C324 278 311 257 305 247 L329 244 L304 225 Q323 213 329 191 L311 194 C337 147 319 103 311 62 L317 19 L299 33 L298 2 C267 43 226 65 196 118 L183 111 L182 115 C173 106 166 106 157 102 L163 115 Q149 115 138 118 C112 82 73 50 38 2 Z'
PRIMARY_FACE='M51 262 L71 240 C70 210 90 194 109 194 C130 194 145 219 151 255 Q167 260 185 255 C194 220 205 194 225 194 C246 194 267 212 266 240 L286 262 Q261 261 247 269 C264 276 274 291 279 300 L253 293 L270 313 Q240 312 210 331 C243 319 278 319 302 337 C274 323 242 326 209 340 L169 358 L128 340 C93 326 61 323 32 337 C56 324 92 320 127 331 Q100 315 67 313 L84 293 L56 300 C64 286 75 275 90 269 Q72 261 51 262 Z'
EAR_MINT='M45 50 C27 82 18 120 25 151 Q30 169 45 179 C34 153 37 111 55 79 Q78 93 92 119 C83 86 65 62 45 50 Z'
EAR_WHITE='M55 79 C42 112 35 150 45 179 L28 176 Q34 187 44 191 L36 191 Q43 199 52 204 L76 167 Q68 163 62 157 L74 157 Q66 149 63 131 Q77 144 92 149 C91 120 75 92 55 79 Z'
CHEEK1='M51 240 C29 248 17 270 10 297 L42 282 Q38 273 39 267 Z'
CHEEK2='M51 278 L29 288 Q25 314 32 337 C34 315 39 294 51 278 Z'
EYE='M82 228 Q104 210 119 218 Q137 229 138 260 Q116 268 99 258 Q84 249 82 228 Z'
IRIS='M108 224 C93 224 88 241 97 254 C105 266 124 270 134 252 C127 260 116 263 108 254 C98 245 99 232 108 224 Z'
M_PRIMARY='M116 174 L141 130 L169 162 L195 130 L221 174 L199 209 L195 176 L169 221 L140 176 L135 209 Z'
primary=path(PRIMARY_FACE,'white')+path(EAR_MINT,'mint')+mirror(path(EAR_MINT,'mint'),169)+path(EAR_WHITE,'white')+mirror(path(EAR_WHITE,'white'),169)+path('M51 240 C29 248 17 270 10 297 L29 288 Q25 317 32 337 C34 315 39 294 51 278 L42 282 Q38 273 39 267 Z','mint')+mirror(path('M51 240 C29 248 17 270 10 297 L29 288 Q25 317 32 337 C34 315 39 294 51 278 L42 282 Q38 273 39 267 Z','mint'),169)+path(M_PRIMARY,'mint','forehead-m')+path(EYE,'ink','eye-left')+path(IRIS,'mint')+mirror(path(EYE,'ink','eye-right')+path(IRIS,'mint'),169)+ellipse(123,236,4.8,5.2,'white')+ellipse(215,236,4.8,5.2,'white')+path(MOUTH,'ink','nose-mouth')
P.append(('sharpie-primary',PRIMARY_OUTER,primary,(0,0,338,360),1,(39,140,369,500)))
# White circular badge reuses the exact complete primary group, with no feature redraw.
ROUND_OUTER='M170 2 A168 168 0 1 1 170 338 A168 168 0 1 1 170 2 Z'
badge=f'<defs><clipPath id="primary-head"><path d="{PRIMARY_OUTER}"/></clipPath></defs><g transform="translate(53.8 44) scale(.7)"><path id="primary-silhouette" fill="#17333E" fill-rule="evenodd" clip-path="url(#primary-head)" d="{PRIMARY_OUTER} {PRIMARY_FACE}"/><g clip-path="url(#primary-head)">{primary}</g></g>'
P.append(('sharpie-primary-badge',ROUND_OUTER,badge,(0,0,340,340),1,(39,140,369,500)))
# Minimal smiling head: smooth ear bows and heart-shaped cheek/chin silhouette.
SMILE_OUTER='M45 10 L48 40 L28 30 L39 62 C26 98 16 139 25 174 L5 199 L16 201 L7 208 Q18 224 34 234 L28 242 C45 282 97 306 145 331 L151 332 L157 331 C205 306 257 282 274 242 L268 234 Q284 224 295 208 L286 201 L297 199 L277 174 C286 139 276 98 263 62 L274 30 L254 40 L257 10 L178 82 Q151 76 124 82 Z'
SMILE_FACE='M38 198 L53 189 C64 162 84 150 98 156 C117 164 132 186 140 194 L162 194 C170 186 185 164 204 156 C218 150 238 162 249 189 L264 198 L246 207 L255 213 C245 233 227 243 207 257 C182 275 165 294 157 320 L151 333 L145 320 C137 294 120 275 95 257 C75 243 57 233 47 213 L56 207 Z'
SMILE_EAR='M59 75 C42 103 36 130 41 155 L80 112 Z'
SMILE_EYE='M72 190 C72 181 84 174 93 176 C106 179 115 191 116 199 C105 198 102 190 93 188 C85 187 80 192 76 195 Z'
SMILE_M='M99 134 L125 96 L151 124 L177 96 L203 134 L183 152 L177 136 L151 176 L125 136 L119 152 Z'
smile=path(SMILE_FACE,'white')+path(SMILE_EAR,'white')+mirror(path(SMILE_EAR,'white'),151)+path(SMILE_M,'mint','forehead-m')+path(SMILE_EYE)+mirror(path(SMILE_EYE),151)+path('M139 206 C134 206 134 212 139 216 L151 224 L163 216 C168 212 168 206 163 206 Q151 203 139 206 Z','ink','nose')
P.append(('sharpie-smile',SMILE_OUTER,smile,(0,0,302,336),2,(814,606,1125,942)))
# Compact sitting mascot: clean organic contours, oval eyes and paws.
MIN_OUTER='M44 10 L44 33 L29 20 L34 51 C16 90 14 136 14 186 L14 239 C14 269 30 286 59 287 L217 287 C238 286 251 273 255 255 C291 247 321 215 328 173 C337 133 319 103 291 92 C272 85 263 113 267 141 C272 171 275 201 257 224 L257 137 C257 103 252 76 242 51 L242 20 L227 33 L227 10 L166 70 Q136 64 105 70 Z'
MIN_FACE='M35 199 L49 183 C52 162 64 142 81 142 C102 143 118 164 126 181 L146 181 C154 164 170 143 191 142 C208 142 220 162 223 183 L237 199 L223 201 L230 208 L218 220 C190 221 169 235 165 261 Q136 268 107 261 C103 235 82 221 54 220 L42 208 L49 201 Z'
MIN_EAR='M48 60 C36 84 30 111 33 143 L69 102 Z'
MIN_M='M88 121 L112 85 L136 113 L160 85 L184 121 L165 138 L159 124 L136 161 L113 124 L107 138 Z'
MIN_TAIL='M267 141 C263 113 272 85 291 92 C307 98 322 114 328 137 L316 154 L304 135 L292 154 L281 145 Z'
minimal=path(MIN_FACE,'white')+path(MIN_EAR,'white')+mirror(path(MIN_EAR,'white'),136)+path(MIN_M,'mint','forehead-m')+path(MIN_TAIL,'mint')+ellipse(89,177,14,21)+ellipse(183,177,14,21)+path('M126 194 Q136 190 146 194 Q151 197 146 202 L136 210 L126 202 Q121 197 126 194 Z','ink','nose')+path('M42 269 C38 255 49 242 65 242 C81 242 94 254 94 268 C94 281 45 283 42 269 Z','white')+mirror(path('M42 269 C38 255 49 242 65 242 C81 242 94 254 94 268 C94 281 45 283 42 269 Z','white'),136)
P.append(('sharpie-minimal',MIN_OUTER,minimal,(0,0,340,295),2,(1180,635,1512,930)))
measure=[]
for name,outer,detail,box,sheet,crop in P:
 # Optical center and size use exact known design canvas, not noisy pixel components.
 natural=SCR/f'{name}-bounds.svg';natural.write_text(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 400">{path(outer,"ink","outer-silhouette")}</svg>')
 natural_query=subprocess.run(['inkscape',str(natural),'--query-all'],check=True,capture_output=True,text=True).stdout
 bounds_row=next(row.split(',') for row in natural_query.splitlines() if row.startswith('outer-silhouette,'))
 x0,y0,ww,hh=map(float,bounds_row[1:]);s=448/max(ww,hh);tx=(512-ww*s)/2-x0*s;ty=(512-hh*s)/2-y0*s
 base=path(outer,'white' if name=='sharpie-primary-badge' else 'ink','outer-silhouette')
 if name=='sharpie-primary':base=f'<path id="outer-silhouette" fill="#17333E" fill-rule="evenodd" clip-path="url(#silhouette)" d="{outer} {PRIMARY_FACE}"/>'
 svg=f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="512" height="512" role="img" aria-labelledby="title desc"><title id="title">{name.replace("-"," ").title()}</title><desc id="desc">Refined Sharpie path master in ink, mint and pure white.</desc><defs><clipPath id="silhouette"><path d="{outer}"/></clipPath></defs><g transform="translate({tx:.6f} {ty:.6f}) scale({s:.8f})">{base}<g clip-path="url(#silhouette)">{detail}</g></g></svg>'
 f=OUT/'svg'/f'{name}.svg';f.write_text(svg);r=ET.fromstring(svg);assert not any(x.tag.endswith(('image','script','foreignObject')) for x in r.iter())
 subprocess.run(['inkscape',str(f),'--export-type=png',f'--export-filename={OUT}/png/{name}.png','--export-width=2048'],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 im=Image.open(OUT/'png'/f'{name}.png').convert('RGBA');alpha_bounds=im.getchannel('A').getbbox()
 if alpha_bounds is None:raise ValueError(f'{name}: rendered asset has no visible alpha bounds')
 bounds=[v/4 for v in alpha_bounds];im.resize((1024,1024),Image.Resampling.LANCZOS).save(OUT/'png'/f'{name}.png')
 query=subprocess.run(['inkscape',str(f),'--query-all'],check=True,capture_output=True,text=True).stdout
 features={}
 for line in query.splitlines():
  parts=line.split(',')
  if parts[0] in ['forehead-m','nose-mouth','nose','eye-left','eye-right'] and len(parts)==5:
   x,y,ww,hh=map(float,parts[1:]);features[parts[0]]={'bbox_xywh':[x,y,ww,hh],'bbox_center':[x+ww/2,y+hh/2],'measurement':'Inkscape geometric bounds after SVG transforms'}
 anchors={}
 if name=='sharpie-primary':anchors={'nose_stem_start':[tx+169*s,ty+279*s]}
 if name=='sharpie-primary-badge':anchors={'nose_stem_start':[tx+(53.8+169*.7)*s,ty+(44+279*.7)*s]}
 measure.append({'authored_anchors':anchors,'features':features,'name':name,'source_sheet':sheet,'source_crop_xyxy':crop,'viewBox':[0,0,512,512],'rendered_alpha_bounds_512':bounds,'sha256_svg':hashlib.sha256(svg.encode()).hexdigest(),'path_elements':sum(x.tag.endswith('path') for x in r.iter()),'construction':'Manually refined native Bézier paths; mirrored paired features; exact silhouette clipping.'})
(OUT/'provenance/measurements.json').write_text(json.dumps({'palette':PALETTE,'variants':measure,'clear_space_recommendation':32,'minimum_sizes_recommendation_px':{'sharpie-primary':48,'sharpie-primary-badge':48,'sharpie-smile':24,'sharpie-minimal':32},'badge_derivation':'Primary paths reused without geometry edits; translate(53.8 44) scale(.7) inside a pure-white circular backing.'},indent=2))
canvas=Image.new('RGB',(1400,1000),'#E5E7EB');d=ImageDraw.Draw(canvas);d.rectangle((0,500,1400,1000),fill='#10242D')
for i,(name,*_) in enumerate(P):
 im=Image.open(OUT/'png'/f'{name}.png').resize((320,320),Image.Resampling.LANCZOS)
 for row in range(2):canvas.paste(im,(15+i*350,55+500*row),im);d.text((22+i*350,400+500*row),name,fill='#17333E' if row==0 else '#FFFFFF')
canvas.save(OUT/'sharpie-approved-preview.png')
print(json.dumps(measure,indent=2))
