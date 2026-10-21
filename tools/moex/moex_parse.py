import sys, re, json
from bs4 import BeautifulSoup
def parse(path):
    s=BeautifulSoup(open(path,encoding='utf-8'),'html.parser')
    out=[]; cat=None
    for el in s.find_all(['label','a']):
        if el.name=='label' and 'exam-title' not in (el.get('class') or []):
            t=el.text.strip()
            if '_' in t: cat=t
        elif el.name=='label':
            subj=el.text.strip()
            q=el.find_next('a',href=re.compile('t=Q'))
            a=el.find_next('a',href=re.compile('t=S'))
            # only take answer link if in same td
            td=el.find_parent('td')
            qa=td.find('a',href=re.compile('t=Q')); aa=td.find('a',href=re.compile('t=S'))
            out.append({'category':cat,'subject':subj,'q':qa['href'] if qa else None,'a':aa['href'] if aa else None})
    return out
if __name__=='__main__':
    r=parse(sys.argv[1])
    json.dump(r,open(sys.argv[2],'w',encoding='utf-8'),ensure_ascii=False,indent=1)
    for x in r: print(x['category'],'|',x['subject'],'|',bool(x['a']))
