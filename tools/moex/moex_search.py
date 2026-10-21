import sys, requests
from bs4 import BeautifulSoup
URL='https://wwwq.moex.gov.tw/exam/wFrmExamQandASearch.aspx'
def fields(soup):
    d={}
    for i in soup.find_all('input',{'type':'hidden'}): d[i['name']]=i.get('value','')
    return d
def search(code, year):
    s=requests.Session(); s.headers['User-Agent']='Mozilla/5.0'
    soup=BeautifulSoup(s.get(URL,timeout=60).text,'html.parser')
    d=fields(soup)
    d.update({'ctl00$holderContent$wUctlExamYearStart$ddlExamYear':year,'ctl00$holderContent$wUctlExamYearEnd$ddlExamYear':year,'ctl00$holderContent$btnYear':'依考試年度設定考試簡稱'})
    soup=BeautifulSoup(s.post(URL,data=d,timeout=60).text,'html.parser')
    d=fields(soup)
    d.update({'ctl00$holderContent$wUctlExamYearStart$ddlExamYear':year,'ctl00$holderContent$wUctlExamYearEnd$ddlExamYear':year,'ctl00$holderContent$ddlExamCode':code,'ctl00$holderContent$btnSearch':'查詢'})
    r=s.post(URL,data=d,timeout=60)
    return r.text
if __name__=='__main__':
    code,year,out=sys.argv[1:4]
    open(out,'w',encoding='utf-8').write(search(code,year))
