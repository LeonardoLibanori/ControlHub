# -*- coding: utf-8 -*-
path = 'c:/Aplicações & Sistemas/Sistema de Estoque/ControleEstoqueLoja/Views/Dashboard/Index.cshtml'
f = open(path, encoding='utf-8').read()

old = '<div class="dash-cabecalho">'
new = '<div class="dash-cabecalho" style="display:flex; flex-wrap:nowrap; justify-content:space-between; align-items:flex-end; width:100%; gap:1rem;">'

if old in f:
    f = f.replace(old, new, 1)
    print('OK: inline style added to dash-cabecalho')
else:
    print('ERROR: dash-cabecalho not found')

old2 = '<nav class="dash-periodo" aria-label="Período do gráfico">'
new2 = '<nav class="dash-periodo" style="margin-left:auto; flex-shrink:0;" aria-label="Período do gráfico">'

if old2 in f:
    f = f.replace(old2, new2, 1)
    print('OK: inline style added to dash-periodo')
else:
    print('ERROR: dash-periodo not found')

with open(path, 'w', encoding='utf-8') as fh:
    fh.write(f)

print('File saved!')
