import requests, re, json, sys
from concurrent.futures import ThreadPoolExecutor
s=requests.Session(); s.headers['User-Agent']='Mozilla/5.0'
def probe(site):
    try:
        r=s.get(f'https://svc.tabf.org.tw/{site}//Paper/Year',timeout=30)
        if r.status_code!=200: return site,None,None
        eps=re.findall(r'EPID=(\d+)">(\d+)年</a>',r.text)
        t=re.search(r'<title>\s*(.*?)\s*</title>',r.text,re.S)
        return site,(t.group(1).strip() if t else ''),eps
    except Exception as e: return site,str(e),None
sites=[f'{y}{k}{n:02d}' for y in range(100,116) for k in ('post','cht') for n in range(1,5)]
with ThreadPoolExecutor(6) as ex: res=list(ex.map(probe,sites))
out={}
for site,title,eps in res:
    if eps is not None and title:
        print(site,title[:60],eps); out[site]={'title':title,'epids':eps}
json.dump(out,open('sites.json','w'),ensure_ascii=False,indent=1)
