"""下載台灣金融研訓院網站「歷屆試題」的試題與答案 PDF，輸出 manifest.json（在目前資料夾）。"""
import requests,re,time,json,os
from bs4 import BeautifulSoup
s=requests.Session(); s.headers['User-Agent']='Mozilla/5.0 (portfolio study tool)'
_get=s.get
def get(url,**kw):
    for a in range(6):
        try: return _get(url,**kw)
        except requests.RequestException: time.sleep(5*(a+1))
    raise RuntimeError(url)
s.get=get
soup=BeautifulSoup(s.get('https://www.tabf.org.tw/LicenseHistoryExam.aspx',timeout=30).text,'html.parser')
man=[]
os.makedirs('pdf',exist_ok=True)
for tr in soup.find_all('tr'):
    tds=tr.find_all(['td','th'])
    if len(tds)<2: continue
    name=tds[0].get_text(' ',strip=True)
    for a in tr.find_all('a',href=re.compile('PHID')):
        phid=a['href'].split('=')[-1]; sess=a.get_text(' ',strip=True)
        p=BeautifulSoup(s.get('https://www.tabf.org.tw/LicenseHistoryExam.aspx?PHID='+phid,timeout=30).text,'html.parser')
        files=[]
        for b in p.find_all('a',href=re.compile(r'ExamHistoryEdit/.*\.pdf',re.I)):
            url=b['href']; fn='pdf/'+url.split('/')[-1]
            if not os.path.exists(fn):
                r=s.get(url,timeout=60)
                if r.ok and r.content[:4]==b'%PDF': open(fn,'wb').write(r.content)
                time.sleep(0.5)
            files.append({'label':b.get_text(' ',strip=True),'url':url,'file':fn})
        man.append({'exam':name,'session':sess,'phid':phid,'page':'https://www.tabf.org.tw/LicenseHistoryExam.aspx?PHID='+phid,'files':files})
        print(name[:20],sess,len(files),flush=True); time.sleep(0.5)
json.dump(man,open('manifest.json','w'),ensure_ascii=False,indent=1)
