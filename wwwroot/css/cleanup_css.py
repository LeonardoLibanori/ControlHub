import io

with open('c:/Aplicações & Sistemas/Sistema de Estoque/ControleEstoqueLoja/wwwroot/css/corporate-theme.css', 'r', encoding='utf-8') as f:
    content = f.read()

# Remove duplicate old CSS blocks that were from the original base CSS
old_block = """/* No celular o grafico fica com rolagem lateral, em vez de espremer as 7
   barras a ponto de virar um risco. */
@media (max-width: 30rem) {
    .dash-grafico {
        min-width: 22rem;
    }

    .dash-coluna-valor {
        font-size: .62rem;
    }

    .dash-coluna-dia {
        font-size: .6rem;
    }
}

/* 30 DIAS: barras mais finas e colunas mais espaçadas */
    gap: .5rem;
    align-items: end;
    padding: 1rem .5rem 0;
    border: 1px solid var(--border);
    border-radius: .75rem;
}

.dash-coluna {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: .35rem;
    min-width: 0;
}

.dash-coluna-valor {
    font-size: .7rem;
    color: var(--text-muted);
    text-align: center;
    word-break: break-all;
}

/* Trilho com altura FIXA: e a barra que cresce dentro dele, entao as 7
   colunas ficam alinhadas pelo topo do graphicismo. */
.dash-coluna-trilho {
    display: flex;
    align-items: flex-end;
    width: 100%;
    height: 8rem;
    background: rgba(255, 255, 255, .04);
    border-radius: .35rem;
    overflow: hidden;
}

.dash-coluna-barra {
    width: 100%;
    background: var(--accent);
    /* min-height garante que um dia com 0 ainda mostre a "base" da barra:
       a coluna some inteira sem ela. */
    min-height: 2px;
}

.dash-coluna-dia {
    font-size: .7rem;
    color: var(--text-muted);
}

/* No celular o grafico fica com rolagem lateral, em vez de espremer as 7
   barras a ponto de virar um risco. */
@media (max-width: 30rem) {
    .dash-grafico {
        min-width: 22rem;
    }

    .dash-coluna-valor {
        font-size: .62rem;
    }
}
"""

new_block = """/* No celular o grafico fica com rolagem lateral, em vez de espremer as 7
   barras a ponto de virar um risco. */
@media (max-width: 30rem) {
    .dash-grafico {
        min-width: 22rem;
    }

    .dash-coluna-valor {
        font-size: .62rem;
    }

    .dash-coluna-dia {
        font-size: .6rem;
    }
}
"""

content = content.replace(old_block, new_block)

with open('c:/Aplicações & Sistemas/Sistema de Estoque/ControleEstoqueLoja/wwwroot/css/corporate-theme.css', 'w', encoding='utf-8') as f:
    f.write(content)

print('Fix aplicada!')