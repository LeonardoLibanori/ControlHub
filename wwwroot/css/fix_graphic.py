import io

with open('c:/Aplicações & Sistemas/Sistema de Estoque/ControleEstoqueLoja/wwwroot/css/corporate-theme.css', 'r', encoding='utf-8') as f:
    content = f.read()

old = """    display: grid;
    grid-template-columns: repeat(7, minmax(0, 1fr));

/* Container do grafico de barras */
.dash-grafico {
    display: grid;
    grid-template-columns: repeat(7, minmax(0, 1fr));"""

new = """    display: grid;
    grid-template-columns: repeat(7, minmax(0, 1fr));"""

content = content.replace(old, new)

with open('c:/Aplicações & Sistemas/Sistema de Estoque/ControleEstoqueLoja/wwwroot/css/corporate-theme.css', 'w', encoding='utf-8') as f:
    f.write(content)

print('Fix aplicado!')