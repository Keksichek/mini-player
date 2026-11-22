"""Render the actual OBS HTML in Edge and check overflow/font behaviour without OBS."""
from pathlib import Path
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import hashlib, html, json, re, subprocess, threading

repo = Path(__file__).resolve().parents[1]
out = repo / '.build' / 'obs-browser-checks'
out.mkdir(parents=True, exist_ok=True)
font = (repo / 'Source/Fonts/Onest-Bold.ttf').read_bytes()
font_id = hashlib.sha256(font).hexdigest() + '.ttf'
source = (repo / 'Launcher/Source/ObsBridge.cs').read_text(encoding='utf-8')
page = source[source.index('<!doctype html>'):source.index('</html>') + 7]
parts = {'cover':(12,12,80,80,16), 'title':(104,12,244,22,16), 'artist':(104,35,244,20,12), 'elapsed':(104,67,90,14,10), 'duration':(258,67,90,14,10), 'bar':(104,86,244,4,16)}
appearance = dict(Width=360, Height=106, Radius=18, Opacity=.97, BorderWidth=1, Shape='rounded', Background='#191821', Border='#494051', Text='#F5F3FA', Secondary='#AAA5B9', Accent='#C4A9F4', Font='Segoe UI', FontId=font_id, GridEnabled=True, GridSize=8,
    Parts={k:dict(zip(('X','Y','Width','Height','FontSize'),v),Visible=True) for k,v in parts.items()})
state = dict(Title='Очень длинное название композиции — полная версия с продолжением', Subtitle='Исполнитель с длинным именем и приглашённые музыканты', Progress=.39, Position=83, Duration=214, Appearance=appearance)
page = page.replace('<script>', '<script>let fixture='+json.dumps(state,ensure_ascii=False)+';window.fetch=async()=>({ok:true,json:async()=>fixture});', 1)
checks = r'''
<script>
setTimeout(async()=>{const results=[];function check(ok,label){if(!ok)throw Error(label);results.push(label);}try{
 await document.fonts.ready;
 check(activeFont?.status==='loaded'&&document.fonts.has(activeFont),'Custom font loads in Chromium from local route');
 const t=title.firstElementChild,s=sub.firstElementChild;
 check(t.getAnimations().length===1&&s.getAnimations().length===1,'Long title and artist both animate');
 const animation=t.getAnimations()[0];animation.pause();animation.currentTime=3000;const transform=getComputedStyle(t).transform;
 check(transform!=='none'&&Number(transform.split(',')[4])<0,'Long text actually moves left');
 await refresh();check(t===title.firstElementChild&&t.getAnimations()[0]===animation&&animation.currentTime===3000,'Polling preserves animation and position');
 const frames=animation.effect.getKeyframes(),overflow=Math.ceil(t.getBoundingClientRect().width-title.clientWidth);
 check(frames[2].transform===`translateX(-${overflow}px)`&&frames[3].transform===frames[2].transform,'Full end of text is revealed with a pause');
 fixture.Title='Коротко';fixture.Subtitle='Автор';await refresh();check(title.firstElementChild.getAnimations().length===0&&sub.firstElementChild.getAnimations().length===0,'Short replacements stop scrolling');
 fixture.Title='Совсем новое очень длинное название — другой трек в музыкальном плеере';await refresh();check(title.firstElementChild.getAnimations().length===1&&title.firstElementChild.getAnimations()[0]!==animation,'New track restarts scrolling');
 fixture.Appearance.Parts.title.Width=850;await refresh();check(title.firstElementChild.getAnimations().length===0,'Wider title stops unnecessary scrolling');
 fixture.Appearance.Parts.title.Width=150;await refresh();check(title.firstElementChild.getAnimations().length===1,'Narrower title restarts scrolling');
 fixture.Appearance.Parts.title.Visible=false;await refresh();check(title.firstElementChild.getAnimations().length===0,'Hidden text does not animate');
 fixture.Appearance.Parts.title.Visible=true;fixture.Appearance.Parts.title.Width=244;fixture.Subtitle='Исполнитель с длинным именем и приглашённые музыканты';await refresh();
 check(!document.querySelector('canvas,svg'),'Editor grid is absent from overlay');
 fixture.Appearance.Shape='circle';fixture.Appearance.Width=360;fixture.Appearance.Height=200;await refresh();check(getComputedStyle(card).borderTopLeftRadius==='50%'&&card.clientWidth===360&&card.clientHeight===200,'Stretched circle renders as ellipse');
 fixture.Appearance.Shape='rounded';fixture.Appearance.Height=106;await refresh();
 document.body.dataset.test='passed';
 }catch(e){document.body.dataset.test='failed';results.push('FAIL '+e.message);}
 const report=document.createElement('pre');report.id='test-results';report.style.display='none';report.textContent=JSON.stringify(results);document.body.appendChild(report);
},1800);
</script>
'''
page = page.replace('</html>',checks+'</html>').encode('utf-8')
class Handler(BaseHTTPRequestHandler):
    def log_message(self,*args): pass
    def do_GET(self):
        if self.path.startswith('/font/') and self.path.endswith(font_id): data, mime = font, 'font/ttf'
        else: data, mime = page, 'text/html; charset=utf-8'
        self.send_response(200);self.send_header('Content-Type',mime);self.send_header('Content-Length',str(len(data)))
        self.send_header('Content-Security-Policy',"default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'")
        self.end_headers();self.wfile.write(data)
server=ThreadingHTTPServer(('127.0.0.1',0),Handler)
threading.Thread(target=server.serve_forever,daemon=True).start()
edge=Path(r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe')
try:
    result=subprocess.run([str(edge),'--headless=new','--disable-gpu','--no-first-run','--no-proxy-server','--user-data-dir='+str(out/'edge-profile'),'--window-size=400,160','--virtual-time-budget=4000','--dump-dom','--screenshot='+str(out/'overlay.png'),f'http://127.0.0.1:{server.server_port}/overlay/token/mini-player'],capture_output=True,encoding='utf-8',timeout=35)
    (out/'rendered.html').write_text(result.stdout,encoding='utf-8')
    report=re.search(r'<pre id="test-results"[^>]*>(.*?)</pre>',result.stdout,re.S)
    if report:
        for line in json.loads(html.unescape(report.group(1))):print(('' if line.startswith('FAIL') else 'PASS ')+line)
    if 'data-test="passed"' not in result.stdout:raise SystemExit('Browser checks failed; see '+str(out/'rendered.html'))
finally:server.shutdown()
