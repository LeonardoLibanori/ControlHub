import sys, urllib.request, urllib.parse, http.cookiejar, re

BASE = 'http://127.0.0.1:5311'
periodo = sys.argv[1] if len(sys.argv) > 1 else 'ano'

cj = http.cookiejar.CookieJar()
op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj))
h = op.open(BASE + '/Account/Login').read().decode('utf-8', 'replace')
tok = re.search(r'<meta\s+name="csrf-token"\s+content="([^"]*)"', h).group(1)
d = urllib.parse.urlencode({'Email': 'admin@loja.com', 'Senha': 'Admin123', '__RequestVerificationToken': tok}).encode()
op.open(urllib.request.Request(BASE + '/Account/Login', data=d))
try:
    r = op.open(BASE + '/Dashboard?periodo=' + periodo)
    body = r.read().decode('utf-8', 'replace')
    print('CODE', r.getcode(), 'LEN', len(body))
except urllib.error.HTTPError as e:
    body = e.read().decode('utf-8', 'replace')
    print('CODE', e.code, 'LEN', len(body))
open('_dump.html', 'w', encoding='utf-8').write(body)
print('saudacao h1:', 'dsh-saudacao' in body)
print('dsh-grafico-svg:', 'dsh-grafico-svg' in body)
print('dsh-rosca:', 'dsh-rosca' in body)
