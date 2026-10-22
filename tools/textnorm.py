"""文字正規化：PDF 字型常把一般漢字對應到「相容表意字」（如 U+F9DD 利）或康熙部首（U+2F00 區），
外觀相同但搜尋、比對都對不上；所有解析器輸出與驗證前都先轉成標準字。"""
import unicodedata


def canon(s):
    if not s:
        return s
    s = ''.join(unicodedata.normalize('NFKC', c) if '⼀' <= c <= '⿟' else c for c in s)
    return unicodedata.normalize('NFC', s)
