import json, os, subprocess, sys
codes = json.load(open(os.environ.get('MOEX_CODES', 'codes.json'), encoding='utf-8'))
for code, (year, title) in sorted(codes.items()):
    if not os.path.exists(f'r{code}.html') or os.path.getsize(f'r{code}.html') < 50000:
        import time
        for attempt in range(4):
            if subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'moex_search.py'), code, str(year), f'r{code}.html'], capture_output=True).returncode == 0:
                break
            time.sleep(5 * (attempt + 1))
    subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'moex_parse.py'), f'r{code}.html', f'i{code}.json'], check=True, capture_output=True)
    idx = json.load(open(f'i{code}.json', encoding='utf-8'))
    print(code, title[:24], len(idx), sum(1 for x in idx if x['a']), flush=True)
