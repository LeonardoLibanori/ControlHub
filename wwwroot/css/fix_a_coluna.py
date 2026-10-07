import io

with open('c:/Aplicações & Sistemas/Sistema de Estoque/ControleEstoqueLoja/wwwroot/css/corporate-theme.css', 'r', encoding='utf-8') as f:
    content = f.read()

old_a_coluna = """a.dash-coluna {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: .35rem;
    min-width: 0;
    text-decoration: none;
    /* Focus visivel: a coluna virou link navegavel pelo teclado. */
    border-radius: .35rem;
}

a.dash-coluna:focus-visible {
    outline: 2px solid var(--accent);
    outline-offset: 2px;
}

a.dash-coluna:focus {
    outline: 2px solid var(--accent);
    outline-offset: 2px;
}

a.dash-coluna.dash-coluna-vazia {
    opacity: .5;
}

a.dash-coluna.dash-coluna-vazia .dash-coluna-trilho {
    opacity: .5;
}

a.dash-coluna.dash-coluna-vazia .dash-coluna-barra {
    background: rgba(194, 163, 109, .15);
    min-height: 2px;
}"""

new_a_coluna = """a.dash-coluna {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: .4rem;
    min-width: 0;
    padding: .35rem;
    border: none;
    background: transparent;
    cursor: pointer;
    text-decoration: none;
    border-radius: .6rem;
    transition: background-color .15s ease, transform .15s ease;
}

a.dash-coluna:hover {
    background: var(--surface-2);
}

a.dash-coluna:focus-visible {
    outline: 2px solid var(--accent);
    outline-offset: 2px;
}

a.dash-coluna.is-dia {
    background: rgba(194, 163, 109, .12);
}

a.dash-coluna.is-dia .dash-coluna-trilho {
    background: rgba(194, 163, 109, .18);
    border-color: rgba(194, 163, 109, .35);
}

a.dash-coluna.is-dia .dash-coluna-dia {
    color: var(--text);
    font-weight: 800;
}

a.dash-coluna.is-dia .dash-coluna-barra {
    background: linear-gradient(180deg, #d4b97a, #c2a36d);
}

a.dash-coluna.dash-coluna-vazia .dash-coluna-trilho {
    opacity: .5;
}

a.dash-coluna.dash-coluna-vazia .dash-coluna-barra {
    background: rgba(194, 163, 109, .15);
    min-height: 2px;
}"""

if old_a_coluna in content:
    content = content.replace(old_a_coluna, new_a_coluna)
    with open('c:/Aplicações & Sistemas/Sistema de Estoque/ControleEstoqueLoja/wwwroot/css/corporate-theme.css', 'w', encoding='utf-8') as f:
        f.write(content)
    print('Updated a.dash-coluna rules')
else:
    print('Old a.dash-coluna block not found')