# 📋 ESTADO DO PROJETO — ControleEstoqueLoja

> Documento de passagem de bastão. Gerado em **02/10/2026**.
> Objetivo: permitir que outra pessoa (ou outra IA) compreenda o sistema
> **sem ver o código**, e decida os próximos passos com base em fatos.
>
> **Repositório:** https://github.com/LeonardoLibanori/ControleEstoqueLoja
> **Branch:** `main` · **HEAD:** `178babb` (= `origin/main`, árvore limpa na conferência de 05/10/2026)

---

## 1. O QUE O SISTEMA É

Sistema de **controle de estoque e frente de caixa (PDV)** para um pequeno
comércio. Substituiu um sistema específico de uma loja de cereais
(granel, balança, preço "por 100 g") e foi generalizado.

**Finalidade:** cadastrar produtos com controle de estoque, registrar vendas
no caixa, controlar o caixa (abertura, suprimento, sangria, fechamento),
imprimir cupom e gerar relatórios — com login e perfis de acesso.

| Aspecto | Valor |
|---|---|
| Stack | ASP.NET Core MVC · .NET 10 · EF Core 10 · SQLite · ClosedXML 0.105.1 |
| Banco | arquivo único `controleestoque.db` (SQLite) |
| Frontend | Razor Views + Bootstrap + SweetAlert2 + JS próprio |
| Roda em | `http://localhost:5236` (perfil `http` do `launchSettings.json`) |
| Build | **0 erros, 0 avisos** |
| Testes | **288/288**, 0 ignorado (`tests.QA`, xUnit) — `dotnet test tests.QA/tests.QA.csproj` |
| Banco versionado no Git | ✅ sim (decisão de 02/10 — ver seção 7) |

---

## 2. ARQUITETURA (mapa dos arquivos)

```
ControleEstoqueLoja/
├── Program.cs                  config, Identity, policies, Migrate() no boot
├── Models/
│   ├── Produto.cs              nome, Tipo, Preco(centavos), Quantidade,
│   │                           EstoqueMinimo, Ativo, CodigoBarras, VendaItems
│   │                           (kg/un é DERIVADO de Tipo, não é propriedade)
│   ├── Venda.cs                TotalVenda, ValorRecebido, ValorTroco, MetodoPagamento,
│   │                           CpfCliente, DataVenda, Itens
│   ├── VendaItem.cs            ProdutoId, Quantidade, PrecoUnitario
│   ├── Vendedor.cs             rótulo do cupom (NÃO é login)
│   ├── HistoricoPreco.cs       log de preço (ProdutoId, Anterior, Novo, Data)
│   ├── Usuario.cs              IdentityUser + NomeExibicao
│   ├── CaixaMovimentacao.cs    Suprimento / Sangria
│   └── FluxoCaixa.cs           tabela ÓRFÃ (não usada pelo código)
├── Controllers/
│   ├── HomeController.cs       CRUD produtos + busca + ajuste + CSV + Excel
│   ├── CaixaController.cs      PDV, vendas, caixa, sangria, relatórios, CSV + Excel
│   ├── AccountController.cs    Login / Logout / Setup / AcessoNegado
│   ├── DashboardController.cs   painel inicial + gráfico 7/30 + detalhe do dia (só Dono)
│   └── UsuariosController.cs   criar e listar contas (só Dono)
├── Security/
│   ├── Policies.cs             11 policies nomeadas (fonte única de verdade)
│   └── SeedPapel.cs            cria roles Dono e Operador no startup
├── Data/
│   ├── ApplicationDbContext.cs IdentityDbContext<Usuario> + 7 DbSets
│   ├── ConversorCentavos.cs    decimal <-> long (centavos)
│   ├── ExportacaoCsv.cs        CSV (UTF-8 com BOM, anti CSV-injection)
│   └── ExportacaoExcel.cs      .xlsx ClosedXML (fórmulas, cores, freeze, totais)
├── Migrations/                 8 migrations (seção 5)
├── Views/                      Razor (Home, Caixa, Account, Usuarios, Shared)
├── wwwroot/                    css/corporate-theme.css, js/admin-caixa.js
├── tests.QA/                   277 testes xUnit de caracterização
├── qa/                         RELATORIO-QA.md, falhas.csv, http-authz.ps1
├── setup.ps1                   prepara máquina NOVA (uma vez)
├── sincronizar.ps1             sincroniza entre máquinas (dia a dia)
├── SINCRONIZAR.md              guia de sincronização
├── RELATORIO-SESSAO.md         relatório de 01/10
└── controleestoque.db          banco real — VERSIONADO no Git
```

**Padrão:** MVC direto, sem camada de serviço/DTO. Os controllers falam
direto com o `ApplicationDbContext`. A suíte é de caracterização contra
controllers (não há testes de UI automatizados, exceto o script PowerShell
`qa/http-authz.ps1`).

---

## 3. REGRAS DE NEGÓCIO EMBUTIDAS (não óbvias para quem lê o código)

| # | Regra | Onde |
|---|---|---|
| 1 | **Preço é gravado em centavos (`INTEGER`)**, nunca decimal. `Preco = 499` = R$ 4,99 | `Data/ConversorCentavos.cs` |
| 2 | **Quantidade NÃO é dinheiro**: continua em `TEXT` decimal. Comparações de estoque são feitas **em C#**, nunca em SQL (em TEXT a collation ordenaria "10" antes de "9") | `Produto.Quantidade` |
| 3 | Produto **"Por peso"** tem preço **direto em R$/kg** (não há mais ×10 escondido). A unidade (`kg`/`un`) **NÃO é uma propriedade**: é derivada de `Tipo == PorPeso` (regras 19 e 20) | `Models/Produto.cs` |
| 4 | Granel legado = centavos **×10** na migration, uma vez, com guard anti-overflow | `Migrations/20261001120000` |
| 5 | **Todo cálculo financeiro é do servidor.** Preço e troco enviados pelo navegador são ignorados | `CaixaController.FinalizarVenda` |
| 6 | **Venda em dinheiro** exige `ValorRecebido >= Total`; troco recalculado no servidor | idem |
| 7 | **Sangria não pode exceder o saldo** (considera só o método "Dinheiro") | `CaixaController` |
| 8 | **Estoque não pode ficar negativo** em venda; ajuste recusa valor negativo | idem |
| 9 | **Exclusão de produto é lógica** (`Ativo=false`); nunca apaga histórico | `HomeController` |
| 10 | **Todo POST exige token anti-forgery**; JS manda header `X-CSRF-TOKEN` | `Program.cs`, `admin-caixa.js` |
| 11 | **Estoque mínimo = 0 ou nulo ⇒ sem alerta** | `Produto.EstoqueAbaixoDoMinimo` |
| 12 | **Histórico de preço só grava se o preço mudar de verdade** (após validações, antes do `SaveChanges`) | `HomeController.Editar` |
| 13 | Sessão **12 h com SlidingExpiration**; antiforgery reemitido a cada ~30 min | `Program.cs` |
| 14 | **Exportação Excel respeita o MESMO filtro da tela** (busca no estoque, dia no histórico) — trocar de formato não troca o conteúdo | `ExportacaoExcel`, actions `...Excel` |
| 15 | Na planilha, texto do cadastro passa por `ExportacaoCsv.EscaparTexto` e vira **texto com `quotePrefix`**: `"=1+1"` abre como texto, nunca como fórmula | `ExportacaoExcel.EscreverTexto` |
| 16 | Dinheiro e quantidade entram como **número** (nunca texto); a exibição é do `NumberFormat` (`R$ #,##0.00`, `#,##0`, `#,##0.000`) — é isso que faz o `SUM` existir | `ExportacaoExcel.EscreverNumero` |
| 17 | **Total da venda nunca é recalculado**: `TotalVenda`/`ValorRecebido`/`ValorTroco` saem como gravados; a linha de totais só agrega com `SUM`/`COUNT` | `ExportacaoExcel.Historico` |
| 18 | Na planilha, estoque abaixo do mínimo usa `Produto.EstoqueAbaixoDoMinimo` (regra 11) e **vence a zebra** de cores | `ExportacaoExcel.AplicarFundo` |
| 19 | **O enum `TipoProduto` mantém 4 valores** (`PorPeso`, `Prateleira`, `Geladeira`, `Freezer`), mas a tela do cadastro só oferece **2**: "Unidade" (`Prateleira`) e "Peso (kg)" (`PorPeso`). `Geladeira`/`Freezer` são **legados** de dados já gravados — continuam válidos no banco e aparecem como "Unidade" | `Models/Produto.cs`, `Views/Home/Novo.cshtml` |
| 20 | **Só `PorPeso` aceita quantidade fracionária**; qualquer outro tipo é inteiro (passo `1` no input). Um produto legado (`Geladeira`/`Freezer`) é tratado como unidade | `Views/Home/Novo.cshtml`, `CaixaController` |
| 21 | **Tipo fora do enum é rejeitado no servidor.** `Enum.TryParse` sozinho aceita `"99"`/`"-5"` (números que não existem no enum) e gravava um tipo inválido; agora exige `TryParse` **e** `IsDefined`. No cadastro o fallback é `Prateleira`; na edição, **mantém o tipo atual** | `HomeController.Novo`, `HomeController.Editar` |
| 22 | A aba **Estoque do Excel tem 7 colunas** — `Produto, Unidade, Preço, Estoque, Estoque mínimo, Estoque baixo, Valor em estoque`. A coluna "Tipo" (nome cru do enum) foi **removida**: a coluna "Unidade" (`kg`/`un`) já diz o que a tela mostra | `ExportacaoExcel.CabecalhoEstoque` |
| 23 | **"Hoje" é um INTERVALO local**, `[DateTime.Today, DateTime.Today + 1 dia)` — nunca `.Date == hoje` (viraria `DATE()` no SQLite e passaria a depender de fuso). Hora **local do servidor**, como o resto do sistema (`DataVenda` é gravada com `DateTime.Now`) | `DashboardController`, `CaixaController` |
| 24 | A **gaveta do dashboard é a mesma que limita a sangria** (regra 7): só vendas em `Dinheiro` **não fechadas** de hoje + suprimentos − sangrias do dia não fechadas, passando por `CaixaController.CalcularSaldoEmGaveta`. A fórmula **não é duplicada** em lugar nenhum — quem tem o número é o caixa | `DashboardController.CalcularSaldoEmGaveta` |
| 25 | O **dashboard é leitura pura**: sem POST/PUT/DELETE, sem `try/catch` (quem trata é o handler global, que não vaza `ex.Message`), e todas as somas são em `decimal` **em C#** sobre listas já materializadas (`AsNoTracking` + `ToListAsync`). `TotalVenda` **gravado**, nunca recalculado pelos itens | `DashboardController`, `Views/Dashboard/Index.cshtml` |
| 26 | O **período do gráfico aceita SÓ 7 e 30** (`?dias=`). Vazio, 0, -1, 15, 31, 999 e qualquer outro valor caem em **7**. A janela é sempre `[Today-(N-1), Today+1)` com **N pontos**, dia sem venda zerado, em ordem cronológica | `DashboardController.Index`, `NormalizarDias` |
| 27 | A **altura da barra é calculada no view-model** (`PercentualAltura`), não na view: maior dia = 100, **dia com venda nunca fica abaixo de 4%**, zero continua 0, e com todos os dias zerados o divisor é 1. O piso existe porque 9,98 contra um máximo de 1.000 dá ~1% e arredondava para `0px` — o dia sumia do gráfico | `DashboardDiaSerie.PercentualAltura` |
| 28 | A tela **Dia** soma o `TotalVenda` **gravado** (igual ao Histórico e ao Início) e as **quantidades somadas em C#** em `decimal`, nunca em SQL: `Quantidade` é `TEXT` no SQLite e a collation BINARY ordenaria "10" antes de "9" (regra 2). Produto desativado hoje **aparece** — ele vendeu no dia | `DashboardController.Dia`, `MontarProdutosDoDiaAsync` |

---

## 4. AUTENTICAÇÃO E AUTORIZAÇÃO

**Papéis:** `Dono` (acesso total) e `Operador` (só PDV).
⚠️ `Vendedor` (tabela) **não é login** — é o rótulo impresso no cupom.

**11 policies** em `Security/Policies.cs`, registradas em `Program.cs`:

| Policy | Quem acessa |
|---|---|
| `PodeOperarPDV` | Dono + Operador |
| `PodeReimprimirCupom` | Dono + Operador |
| `PodeRegistrarSuprimento` | Dono + Operador |
| `PodeAcessarAdmin` | Dono + Operador |
| `PodeRegistrarSangria` | **só Dono** |
| `PodeVerFinanceiro` | **só Dono** |
| `PodeFecharCaixa` | **só Dono** |
| `PodeGerenciarEstoque` | **só Dono** |
| `PodeGerenciarVendedores` | **só Dono** |
| `PodeGerenciarUsuarios` | **só Dono** |
| `PodeUsarIA` | **só Dono** — *declarada e registrada, ainda sem endpoint* (pronta para a camada de IA) |

**Extensibilidade:** autorização é declarativa, sem `if (User.IsInRole(...))`
espalhado no código. Policy nova = **uma linha** em `Program.cs` + **uma
constante** em `Policies.cs`.

**Liberado ao Operador:** PDV (Index, FinalizarVenda, BuscarPorCodigo,
BuscarPorId, ValidarCupom, ObterUltimaVendaId, GerarCupom,
ObterDetalhesVenda, ListarVendedores, RegistrarSuprimento) e a **leitura** do
estoque (Index/Produto em Home).

**1º Dono:** tela `/Account/Setup`, acessível **enquanto `UserManager.Users`
estiver vazio**. Depois, só o Dono cria contas em `/Usuarios`.

---

## 5. MIGRATIONS (8 aplicadas)

| Migration | O que faz |
|---|---|
| `20260220171624_Inicial_Nova` | schema base |
| `20260226131051_AddValorRecebidoValorTroco` | campos de pagamento |
| `20260928234611_ProdutoAtivo_ExclusaoLogica_VendaItemRestrict` | exclusão lógica (BUG-08) |
| `20260929184131_Identity_Usuarios_Roles_Policies` | tabelas do Identity |
| `20260930134556_DecimalParaCentavos` | dinheiro de TEXT → INTEGER de centavos (BUG-20) |
| `20261001120000_ProdutoPrecoDiretoPorQuilo` | "por 100 g" → R$/kg (×10, com guard) |
| `20261001201327_EstoqueMinimoProduto` | coluna `EstoqueMinimo` |
| `20261001204929_HistoricoPrecoProduto` | tabela `HistoricoPrecos` |

**Processo padrão para migration nova (adotado no projeto):**
1. Gerar com `dotnet ef migrations add`
2. **Mostrar o SQL e esperar aprovação**
3. Testar em **réplica** (`%TEMP%`), nunca no banco real
4. Backup + aplicar no banco real
5. O `Down()` da `20261001120000` é **proibido** de propósito (irreversível)

⚠️ **`Program.cs` chama `Database.Migrate()` no startup** — `dotnet run` aplica
migrations pendentes **sem perguntar**. Intencional, mas é preciso saber.

---

## 6. FEATURES ENTREGUES

| Feature | Commit | Como usar |
|---|---|---|
| Estoque mínimo + alerta visual | `989acca` | badge "⚠ ESTOQUE BAIXO" na listagem |
| Histórico de preço por produto | `643fec4` | tela de histórico, mais recente primeiro |
| Exportação CSV (Estoque e Histórico) | `0fde7a0` | botão "Exportar CSV", respeita o filtro da tela, UTF-8 com BOM |
| Exportação **Excel .xlsx** (Estoque e Histórico) | *pendente* | botão "Exportar Excel": fórmulas de total, linha de totais, cores do tema, painel congelado, mesmo filtro da tela |
| Cupom do vendedor **opcional** | `a193d0d` | botão na etapa do CPF (antes travava a venda) |
| Menu de perfil (ícone + "Olá") | `45f22ea` | dropdown com Usuários e Sair |
| Unidade por produto / preço por kg | `f2940c4` | fim do "granel" hardcoded |
| Tema corporativo escuro | `f2940c4` | `corporate-theme.css` (substituiu `site.css`) |
| Autenticação + policies | `ae12be8`…`702ae85` | 2 papéis, 11 policies |
| Sincronização entre máquinas | `a49c399` | `sincronizar.ps1` |
| **Dashboard inicial (Início)** | `d654570` | item de menu só para o Dono: vendas do dia, dinheiro na gaveta, quebra por forma de pagamento, estoque baixo, últimos 7 dias e últimas vendas |
| **Dashboard: gráfico 7/30 dias + detalhe do dia** | `bae0743`, `151fffe` | seletor de período por link (`?dias=7`/`30`); clicar numa barra abre o detalhe do dia **inline** (`?dias=N&data=aaaa-mm-dd`: total, o que foi vendido e as vendas do dia, com toggle) — a rota `/Dashboard/Dia` continua existindo, mas a barra não navega mais para ela; "Últimas vendas" saiu do painel e "Estoque baixo" é a última fileira |

### Visuais (bugs de interface corrigidos)

| ID | Problema | Correção |
|---|---|---|
| BUG-27 | CSS e JS **não carregavam** (app sem `UseStaticFiles`); com `Accept-Encoding: gzip, br` o servidor devolvia 200 com corpo **vazio** — a página abria sem estilo | `UseStaticFiles` no `Program.cs` + `qa/http-assets.ps1` (6 assets × 2 requisições) |
| **BUG-30** | Com **Dinheiro**, o `CONFIRMAR` ficava habilitado com o campo vazio/insuficiente: o operador só descobria a recusa **depois** de tentar (o servidor já recusava, o PDV apenas não ajudava) | `podeConfirmarPagamento()` + `definirConfirmarHabilitado()` em `admin-caixa.js`: `disabled` + `aria-disabled="true"` + visual apagado; **comparação em centavos inteiros** (`Math.round(x*100)`), nunca float; aceita vírgula e ponto; `irParaCPF()` reconfirma (defesa em profundidade) |
| **BUG-30** | O aviso "Faltam R$ 17,00" vinha inteiro dentro do `<h2>` de 2rem: **quebrava no meio** ("Faltam R$ 17," / "00"), com **duas cores** e sem caber em 360 px | Estrutura única nos dois estados — rótulo pequeno em cima (**TROCO** / **FALTAM**, este em `var(--danger)`) e valor grande embaixo, `white-space: nowrap` + `font-size: clamp()`; região com `aria-live="polite"` |
| **BUG-30** | Reabrir o modal mantinha o valor da venda anterior no campo, e o botão abria habilitado para o valor errado | `resetarEtapaPagamento()` em `abrirModalPagamento()`: zera método, valor, troco, aviso e CPF |
| **BUG-30** | `Enter` no campo de valor podia disparar a confirmação | `onkeydown` captura o `Enter` e faz `preventDefault()` |
| **BUG-30** | O erro do servidor aparecia sempre como **"Erro no Estoque"**, mesmo quando a recusa era de dinheiro insuficiente (BUG-29) | Título passou a "Venda não concluída"; a **mensagem do servidor** é exibida (`res.message`), com *fallback* |
| **BUG-36** | Lista de vendas do Histórico não acompanhava o período selecionado ou dia avulso; janelas divergiam dos cartões e faltava limite de linhas e subtítulo dinâmico | A lista passa a usar a MESMA janela dos cartões (`hoje`, `7dias`, `mes`, `ano`, `data` avulsa com fim exclusivo). Título dinâmico sem ícone, subtítulo "N vendas · R$ total", cartão "ÚLTIMOS 7 DIAS", campo de data só preenchido para filtro por dia, limite de 200 linhas com aviso e totais/contagem em C# com decimal sobre todas as vendas |

> **Ordem de responsabilidade:** o **servidor continua sendo a autoridade**. O bloqueio no PDV é conveniência de operador; `CaixaController.FinalizarVenda` segue validando e recusando (`BUG-29`). Nada do payload, do cálculo do total ou do troco foi alterado.

---

## 7. DECISÃO CRÍTICA RECENTE: banco versionado no Git

Em **02/10/2026** o usuário decidiu que **casa e trabalho devem usar o mesmo
banco** (antes cada máquina tinha o seu, e os logins eram diferentes).

- `controleestoque.db` **passou a ser versionado** (`.gitignore` tem
  `!controleestoque.db` no final, depois das regras `*.db` do template).
- `.gitattributes` marca `*.db` como **binary** → o Git **recusa** fazer merge
  de dois bancos (nunca gera um `.db` corrompido em silêncio).
- `sincronizar.ps1` encapsula o ritual: `-Enviar` / `-Receber` / `-Status`.
  Faz backup automático em `backups-banco/`, recusa rodar com o app aberto,
  e nunca usa `--force`.
- `SINCRONIZAR.md` documenta o passo a passo e o que fazer em caso de conflito.

### 🔒 Visibilidade do repositório — PRIVADO (verificado em 02/10/2026)

**O repositório é PRIVADO.** Isto não é recomendação pendente: é fato
verificado. (O bloco anterior afirmava que ele era público e recomendava
torná-lo privado — estava errado.)

Consulta à API do GitHub em **02/10/2026**:

| Requisição | Resultado |
|---|---|
| `GET /repos/LeonardoLibanori/ControleEstoqueLoja` **sem credencial** | `404 Not Found` |
| mesmo `GET` **com a credencial do dono** | `200` · `"visibility": "private"` · `"private": true` |
| `raw.githubusercontent.com/.../main/README.md` sem credencial | `404` |
| página `github.com/LeonardoLibanori/ControleEstoqueLoja` sem credencial | `404` |

⚠️ O `404` anônimo **sozinho não prova nada**: o GitHub devolve `404` (e não
`403`) tanto para repositório privado quanto para inexistente, exatamente para
não revelar a existência de repositórios privados. A prova é o **`200`
autenticado**. O token não foi impresso em momento algum da verificação.

**O banco entrou no histórico num único commit:**

```text
$ git log --oneline --follow -- controleestoque.db
a49c399 chore(banco): sincroniza dados e estoque entre maquinas
```

Só isso. E `controleestoque.db` não foi alterado em nenhum commit posterior —
ou seja, **não existem versões antigas do banco espalhadas pelo histórico**,
com dados diferentes dos atuais.

Com o repositório privado, deixam de estar expostos:

- os **hashes de senha** das contas;
- os **dados de venda** da loja;
- as versões do banco guardadas pelo histórico do Git.

### ❓ Pendência: o repositório já esteve público antes de 02/10/2026?

A API do GitHub **não expõe histórico de visibilidade**. A única fonte é o
**Security log** da conta. Campo **a confirmar**:

| Pergunta | Status |
|---|---|
| É privado hoje? | ✅ **Sim** — verificado em 02/10/2026 |
| Tem forks / watchers? | 0 / 0 — tranqüilizador, mas **não é prova** (scrapers e o GitHub Archive não geram forks nem watchers) |
| **Já esteve público com o banco dentro?** | ❓ **A CONFIRMAR** |

**Como verificar:** foto de perfil → **Settings** → na seção *Archives* da
barra lateral → **Security log**. Filtrar por
`repo:LeonardoLibanori/ControleEstoqueLoja`, olhar as entradas da categoria
`repo` (os qualificadores `operation:create` / `operation:access` /
`operation:modify` ajudam) e ler o campo **`visibility`** (`public` /
`private`). A tela também exporta JSON ou CSV.

⏳ **Prazo real — o log guarda apenas 90 dias.** Em 02/10/2026 ele alcança
somente até ~**04/07/2026**:

- ✅ **Cobre a data que importa**: `a49c399`, quando o banco entrou no
  histórico (02/10/2026);
- ❌ **Não cobre** a criação do repositório (22/02/2026) — esse trecho já
  expirou e não volta mais;
- ⚠️ **A janela está andando**: depois de ~**31/12/2026** o evento de
  02/10/2026 sai do log e a pergunta fica sem resposta para sempre.

**Por que importa:** o banco guarda **hashes de senha** das contas e os
**dados de venda** da loja. Se o repositório esteve público em qualquer
momento a partir de `a49c399`, trate esse conteúdo como potencialmente
exposto — mesmo que tenha sido tornado privado depois.

**Se a verificação indicar que esteve público:** trocar as senhas das contas
do sistema. Se indicar que **nunca** foi público, este bloco inteiro pode
encolher para uma linha de nota.

---

## 8. BUGS CORRIGIDOS

### Fase 1 — integridade financeira
| ID | Bug | Correção |
|---|---|---|
| BUG-01 | Servidor confiava no preço enviado pelo cliente | passa a usar o preço do banco |
| BUG-02 | Quantidade negativa somava estoque | rejeita `quantidade <= 0` |
| BUG-03 | Preço de granel ×10 inconsistente entre tela e banco | fórmula única, `MidpointRounding.AwayFromZero` |
| BUG-04 | Sinal da sangria invertido (saldo crescia ao retirar) | convenção única, saldo coerente |
| BUG-25 | Troco confiado ao navegador | recalculado no servidor |
| BUG-10/11/26 | `metodoPagamento` nulo, texto, notação científica, overflow → HTTP 500 | retorna 400 com JSON amigável |
| BUG-12/13/14/20 | Sangria > saldo, valor ≤ 0, sem motivo, fechamento não-idempotente | todos bloqueados/validados |
| — | Venda não era atômica | **transação única** (baixa + itens + venda) |

### Fase 2 — segurança
| ID | Bug | Correção |
|---|---|---|
| BUG-23 | XSS armazenado no nome do produto | `textContent` no JS + `SanitizeTexto` no servidor |
| BUG-12/16/17 | Vazamento de `ex.Message` ao cliente | log no servidor + mensagem genérica |
| BUG-14 | Ausência de CSRF | `[ValidateAntiForgeryToken]` + token no header |
| BUG-13 | Rotas sem autenticação | Identity + 11 policies |

### Dados
| ID | Bug | Correção |
|---|---|---|
| BUG-20 | Dinheiro gravado como `TEXT` (ordenava errado) | `INTEGER` de centavos |
| BUG-08 | Excluir produto apagava histórico de venda | exclusão lógica + FK `Restrict` |
| BUG-19 | `EnsureCreated()` ignorava migrations | trocado por `Migrate()` |

### Build/infra
| Correção |
|---|
| Avisos CS86xx (anulabilidade) — assinaturas corrigidas na origem, **0 avisos** |
| NU1903 (SQLitePCLRaw, CVE) — pin `2.1.12` (SQLite 3.53.3) |
| `dotnet run` na raiz do workspace — `.slnx` na raiz + README corrigido |
| Banco na fila do Git — `sincronizar.ps1` |

### Visuais
| Correção | Detalhe |
|---|---|
| **BUG-27 — CSS chegava vazio no browser** | Página inteira sem estilo (botão/campo crus) e o modal de Operação vazando no meio do login. Causa: `app.MapStaticAssets()` — em `dotnet run` não existem as variantes pré-comprimidas do manifesto de build, então com o `Accept-Encoding: gzip, br` (que todo browser envia) a resposta era `200` com `Content-Length: 0`. **Correção:** `app.UseStaticFiles()`, que serve o `wwwroot` com `Content-Type`/`Content-Length` corretos em `run` e em `publish`. |
| Modal de pagamento aparecia antes da hora | SweetAlert2 subia sobre o modal do caixa |
| Botão Buscar fora do padrão | padding/raio/peso herdados de `.btn-novo` |
| Histórico estourando a página | tabela fluida com container certo |
| Overflow de 2px no mobile | `width: 100vw` causava rolagem lateral |
| Tela de Usuários com aviso e botão redundantes | mensagem duplicada removida |

### Rótulo enganoso
| ID | Bug | Correção |
|---|---|---|
| — | No Histórico, o cabeçalho da 2ª coluna dizia **"Qtd. Itens"** mas a célula mostra `@venda.DataVenda` | cabeçalho corrigido para "Data" (`57b51a5`). Não era só enfeite: quem fecha o dia lia a coluna achando que era quantidade e conferia um número que nunca bate com o total |

---

## 9. COMO TRABALHAR NESTE PROJETO

### Rodar
```powershell
cd ControleEstoqueLoja
dotnet run                # http://localhost:5236
```

### Testar
```powershell
dotnet build                              # deve terminar com 0 aviso, 0 erro
dotnet test tests.QA\tests.QA.csproj     # deve dar 247/247 + 1 ignorado
```

> O `.slnx` da raiz lista **só** o projeto web. `dotnet test` sem caminho
> roda zero testes e sai com código 0 — parece verde, mas não testou nada.
> Sempre aponte o `.csproj` de `tests.QA`.

### Trocar de máquina

O banco está versionado, então `push` e `pull` já levam **código e banco**.
Sempre com a aplicação **fechada**:

```powershell
# chegou na máquina
git pull
dotnet run

# vai trocar de máquina, depois de mexer
git add -A ; git commit -m "..." ; git push
```

Fechar o sistema antes do `pull` não é formalidade: com o app aberto o
`controleestoque.db` fica travado e o Git não consegue trocar o arquivo.

`.\sincronizar.ps1 -Receber` / `-Enviar` continuam existindo e são úteis
quando se quer o ritual completo (backup do banco em `backups-banco/` e
aplicação das migrations pendentes), mas **não são obrigatórios** para
atualizar o projeto entre as duas máquinas.

Primeira vez numa máquina nova:
```powershell
git clone https://github.com/LeonardoLibanori/ControleEstoqueLoja.git
cd ControleEstoqueLoja
dotnet run
```

### Antes de mudar qualquer coisa
1. **Fechar a aplicação** antes de `pull`/`push` — o `.db` fica travado com ela aberta
2. **Não** rodar `dotnet ef database update` no banco real sem backup
3. **Não** usar `--force` no git
4. Rodar a suíte completa antes de commitar (277 testes — `dotnet test tests.QA/tests.QA.csproj`; note que o `.slnx` **não** inclui o projeto de testes)
5. Mudança de segurança → atualizar `qa/falhas.csv`
6. Nunca deixar um **`.db-wal`/`.db-shm`** órfão ao lado do `.db` quando for
   versionar o banco — é o vetor de banco corrompido que o `.gitignore` do
   projeto já cita

### Regras do repositório
- Uma feature por commit, mensagem clara em português
- Migration: mostrar o SQL e esperar aprovação antes de aplicar
- Não alterar testes para fazê-los passar; se o teste estiver errado, **parar e perguntar**
- Documentação de QA em `qa/RELATORIO-QA.md` e `qa/falhas.csv`

---

## 10. LACUNAS CONHECIDAS (candidatas a próximos passos)

### Funcional
- **Não existe** histórico de movimentação de estoque (entrada/saída) — só o saldo atual
- **Não existe** multi-loja / multi-empresa (banco único, sem `LojaId`)
- **Sem** controle de fornecedor, compras ou nota fiscal
- **Sem** controle de lote/validade (relevante para perecíveis)
- **Sem** relatórios de margem de lucro / custo
- Tabela `FluxoCaixa` é **órfã** (criada, nunca usada pelo código)
- **`Produto.Quantidade` continua em TEXT** (limitação consciente: comparação numérica em SQL seria errada)
- Gestão de usuários **não permite editar/desativar** conta existente (só criar e listar) — desativar Operador exigiria alteração direta no banco
- **Backup do banco só acontece na troca de máquina** (o `sincronizar.ps1` grava
  uma cópia em `backups-banco/` quando roda). **Não há** backup automático
  diário com rotação — se ninguém sincronizar, o GitHub é a única cópia

### Funcional
- **PERGUNTA EM ABERTO — o relatório do dia (`RelatorioDados`, `ObterResumo`) inclui itens já fechados.** Depois do fechamento do dia, o botão CAIXA (`ObterStatusAtual`) zera o saldo, mas `RelatorioDados`/`ObterResumo` **somam vendas, suprimentos e sangrias de hoje sem `!IsFechado`** — logo o `saldoFinal` do relatório pode divergir do saldo em gaveta da conferência. **Não é bug classificado**: foi **decidido** não mexer nesses dois métodos agora, porque não está definido se o relatório do dia é "o que entrou no caixa hoje" (e então deveria excluir o fechado, como a regra 7) ou "tudo que aconteceu hoje" (e então está certo, e quem diverge é a conferência). A decisão é de produto, não de código. Enquanto não houver decisão, os dois comportamentos coexistem. Pergunta: **o relatório do dia deve incluir o que já foi fechado?**
- **`FaturamentoMes` e `FaturamentoAno` do Histórico comparam só mês/ano** (`CaixaController.L462`, `L466`): `DataVenda.Month == Today.Month` soma também o mesmo mês de **todos os anos anteriores**. "Faturamento do ano" inclui janeiro do ano passado
- **O fechamento só vira flag e não guarda valor declarado**: `ZerarCaixaParaProximoDia` (L847) apenas marca `IsFechado = true` em vendas e movimentações. Não existe entidade de sessão de caixa nem total declarado — o dinheiro que o dono contou na gaveta **não é gravado em lugar nenhum**
- **Vendas e movimentações não fechadas de dias anteriores ficam fora do saldo**: o filtro é sempre `Data.Date == hoje`, então um dia que o dono esqueceu de fechar "verte" para o dia seguinte (as linhas somem do saldo, mas continuam no histórico)
- **`Controllers/FluxoCaixaController.cs` é arquivo morto fora do build**: excluído por `<Compile Remove>` no `.csproj` L17 (e não compilaria mesmo — usa `_context.OperacoesCaixa`, que não existe). Sem `[Authorize]`, mas **inalcançável** (a rota responde 404). Candidato a remoção; se alguém tirar o `Compile Remove`, ele volta ao build sem proteção
- **`Views/Caixa/Index1.cshtml`, `Index2.cshtml` e `Index3.cshtml` são views órfãs**: nenhuma está no menu nem em rota. `Index1` ainda duplica o CDN do SweetAlert2 (L95). Candidatas a remoção
- **Cinco recursos externos no `_Layout`** quebram a operação offline: fontes do Google (L100-102), Bootstrap Icons via jsdelivr (L104) e SweetAlert2 via jsdelivr (L363). Sem internet os ícones `bi-*` do menu somem e os alertas do PDV não abrem. O texto dos itens de menu **não** depende de ícone — o item "Início" foi criado assim de propósito

### Técnico
- **Detalhe do dia do Dashboard não tem paginação**: `DashboardController.Dia` lista todas as vendas e todos os produtos do dia de uma vez. Para o volume de um dia de loja cabe; um dia com milhares de vendas (promoção grande) carregaria tudo
- **O gráfico aceita só 7 e 30 dias**: 14, 60 ou "o mês inteiro" não são janelas válidas (caem em 7). Ampliar a lista de períodos é linha de controller + CSS, sem migration
- **Concorrência**: a leitura de estoque não tem lock pessimista nem `ConcurrencyCheck`; a proteção atual vem da revalidação de estoque no servidor
- **Sem testes de UI/integração** automatizados (só unitários de controller + um script PowerShell de HTTP)
- **Sem ILogger** — erros vão para `Console.Error`
- Sem observabilidade, métricas ou health check
- **Sem CI/CD** (o `.github/workflows` chegou a ser criado, mas está vazio)

### Produto
- **Unidade de medida real (campo próprio)**: "Peso (kg)" cobre **só o quilo**. Vender a **litro**, **metro** ou **par** não cabe no enum atual (`PorPeso` significa kg) — exige um campo `UnidadeMedida` de verdade na tabela `Produtos`, com **migration** (novo enum/tabela, backfill e ajuste na venda, no Excel e no cupom). É feature futura, não uma linha de tela
- **Sem** conversão de caixa por hora/dia útil
- **Sem** perfis além de Dono/Operador (ex.: gerente)
- **Sem** recuperação de senha nem 2FA
- **Sem** suporte a múltiplas lojas

---

## 11. PRÓXIMOS PASSOS — SUGESTÕES DO CONTEXTO

A **Fase A de baixo risco** já está **100% concluída** (estoque mínimo,
histórico de preço, CSV).

### (a) Essenciais para portfólio
1. **Concorrência real**: transação explícita + teste de duas vendas simultâneas
2. **Histórico de movimentação** de estoque (kardex) — sem isso não há rastreabilidade
3. **Testes de integração HTTP** no CI (hoje é script manual)
4. **CI/CD**: build + testes automatizados a cada push

### (b) Essenciais para vender
5. **Gestão de usuários completa**: editar/desativar, reset de senha
6. **Backup automático** do banco (diário, com rotação)
7. **Relatórios financeiros**: fechamento por período, margem, custo
8. **Controle de fornecedor e compras**
9. **Perfis mais finos** (gerente, caixa) além de Dono/Operador
10. **Recuperação de senha** e 2FA



---

## 0c. Mapeamento da entrada — _Layout.cshtml (verificado 10/2026)

### Estrutura do shell que será criado

- **Corpo**: `<body class="app-shell">` (classe de habilitação do JS) ou, para
  Login/Setup (casas de acesso, sem shell), o `<body>` sem a classe.
- **Shell**: `.app-shell` → flex column (min-height 100vh). Dentro:
  - `.app-sidebar` (fixo, `left:0`, `width: 272px`, `z-index: 1040`): top (brand),
    `<nav class="app-nav">` (itens + seção do usuário), footer (tema + estado).
  - `.app-main` (flex:1): `.app-header` (hambúrguer, tema, busca, avatar + menu),
    `.app-content` (max-width 1440px, margin auto, padding), `.app-footer`.
- **Conteúdo das telas**: `@RenderBody()` dentro de `.app-content` — **não
  muda**, nem ordem, nem contéudo.
- **Legado**: `.main-container` permanece nos Views, com override escopado
  `.app-content .main-container { width:100%; margin:0 }` (sem duplicar padding
  da caixa do shell; sem overflow no mobile).

### Elementos não de navegação (chrome) mapeados

| ID/Classe | Uso | Status |
|---|---|---|
| `campoBusca`, `#sugestoes` | busca global do header | mantido no header, escondido no mobile |
| `#modalAtalhos` | menu numerado Alt+M | mantido, fora do shell, sobreposto |
| `#modalOperacao` | sangria/suprimento | mantido |
| `#modalAjudaAtalhos` | overlay de ajuda (`.aberto`) | mantido, sobreposição global |
| `#btnMenuPerfil`, `.dropdown-menu` | perfil + tema/Usuários/Sair | mantido no header |
| `#campoBusca` | foco + ESC limpa | mantido no Esc handler |

### Atalhos Alt+ mapeados (todos com `if (window.LOJA_ACESSO...)` no
`keydown` inline do _Layout; politica via `Security/Policies.cs`)

| Atalho | Destino | Policy | Exibir no sidebar |
|---|---|---|---|
| `Alt+E` | `/Home/Index` | PodeOperarPDV | `Estoque` |
| `Alt+C` | `/Caixa` | PodeOperarPDV | `Caixa` |
| `Alt+H` | `/Caixa/Historico?periodo=hoje` | PodeVerFinanceiro | `Histórico` |
| `Alt+M` | `#modalAtalhos` (numbers 1-7) | `temMenuAtalhos` | — (modal) |
| `Escape` | fechar modal Ajuda / limpar campoBusca | — | — |

### Login/Setup usam layout separado

- `Views/Account/Login.cshtml` e `Views/Account/Setup.cshtml` e
  `Views/Account/AcessoNegado.cshtml` — **não** estendem `_Layout` com shell;
  respondem `ViewContext` é a mesma mas `User.Identity.IsAuthenticated` é falso,
  portanto a ramificação `if (autenticado)` não entra, e o `<header>` original
  (menu simples) é renderizado. O shell só nasce quando `autenticado == true`.

### Regra de decisão (verificar depois)

- O shell só é colocado se `autenticado == true`. Senão, o HTML original
  (header/brand/navbar) é mantido inalterado.

### (c) Integrações com IA
11. **Previsão de demanda / sugestão de reposição** (usa estoque mínimo + histórico)
12. **Leitura de nota fiscal (OCR + LLM)** para entrada de produtos
13. **Copiloto de consulta** sobre financeiro/estoque — a policy `PodeUsarIA`
    **já está criada e registrada, sem endpoint**: pronta para ser usada

---

## 12. COMO VERIFICAR TUDO QUE ESTÁ AFIRMADO AQUI

```powershell
# build limpo

### AJUSTE DE SHELL 3 DE 3 — cabeçalho do Dashboard (10/10/2026)

- **Objetivo:** remover a linha de subtítulo `"dd/MM/aaaa · conferência do dia"` abaixo do título "Dashboard" e transformar o seletor de janela ("7 dias | 30 dias") em uma linha flex alinhada ao centro da primeira linha do título, com `gap: 16px` e `margin-bottom: 24px` até o primeiro cartão.
- **Mudanças:**
  - `Views/Dashboard/Index.cshtml`: linha do subtítulo removida; estrutura do cabeçalho deixou de carregar estilo inline e passou a depender da folha de estilos.
  - `wwwroot/css/corporate-theme.css`: novo bloco no fim da folha (`.dash-cabecalho` em `display: flex`, `justify-content: space-between`, `align-items: center`, `gap: 16px`, `margin-bottom: 24px`; `.dash-titulo` com `margin-bottom: 0`; em `(max-width: 767.98px)` o cabeçalho vira coluna, `row-gap: 12px` e `margin-bottom: 16px`).
  - Causa raiz: a regra `.dash-cabecalho` original ficava dentro de um bloco de comentário quebrado (`*/` órfão no FINAL da regra, na descrição "ETAPA C"), de modo que `display: flex` nunca aplicava. O bloco novo no fim da folha foi deixado **auto-suficiente** (sem depender da regra quebrada).
- **Verificações CDP (Dono, ambos os temas):**
  - `1440`: gap header→cartão `24px`, `dCentro=0px`, `"conferência"` não aparece, `overflowX=0`.
  - `1024`: gap `24px`, `dCentro=0px`, `"conferência"` não aparece, `overflowX=0`.
  - `390`: gap `16px` (seletor desce abaixo do título com `12px` entre si, `margin-bottom` do cabeçalho `16px`), `dCentro=49px` (esperado: seletor em coluna), `"conferência"` não aparece, `overflowX=0`.
- **Resultados:** build 0 erros; `dotnet test tests.QA` `288/288`; `encoding-check` 0; `css-balance` 451 pares; `contrast` `60/60`; `nav-smoke` **82/82** em Dono e no escuro. FALHA de autonomia isolada do Operador na instância de teste (`operador@teste.com` não existe no `.db` temporário de QA) — não interfere no escopo deste ajuste.
- **Commit:** `fix(dashboard): removes subtitle and repositions period selector`.

dotnet build                              # 0 erros, 0 avisos

# suíte completa
dotnet test tests.QA\tests.QA.csproj     # 247/247 + 1 ignorado

# migrations aplicadas
dotnet ef migrations list

# estado da sincronização
.\sincronizar.ps1 -Status

# auditoria de segurança (3 perfis: anônimo / Operador / Dono)
.\qa\http-authz.ps1                      # 115 checagens
```

---

## 13. HISTÓRICO DE COMMITS (63 no total)

| Data | Commits | Tema |
|---|---|---|
| 22–26/02 | `c48acf3`…`cd74e9d` | projeto original (sistema de cereais) |
| 28/09 | `42e87d1`…`7a76e3b` | recriação da suíte de QA, **Fase 1** (integridade financeira), BUG-08/19 |
| 29/09 | `4b06257`…`d71c2b2` | segurança (XSS, `ex.Message`, CSRF), **Identity + policies** (BUG-13) |
| 30/09 | `fd30124`…`7d83fb9` | BUG-20 (centavos), `setup.ps1`, unidade/preço por kg, tema corporativo |
| 01/10 | `c1f3f1b`…`62bbfa4` | menu de perfil, cupom opcional, **Fase A** (estoque mínimo, histórico de preço) |
| 02/10 | `0fde7a0`…`2c30b34` | **exportação CSV**, **banco versionado + `sincronizar.ps1`**, doc de passagem de bastão |
| 02/10 | `57b51a5` | correção do cabeçalho do Histórico ("Qtd. Itens" → "Data") |

---

---

## 14. REDESIGN PASSO 1 DE 5 — TEMAS CLARO/ESCURO POR TOKENS (08/10/2026)

### O que mudou

**`wwwroot/css/corporate-theme.css`** — toda cor agora vive em quatro blocos
de paleta no topo do arquivo; as regras consomem `var(...)`:

1. `:root, :root[data-theme="dark"]` — paleta **escura** (padrão)
2. `:root` — derivados (`color-mix` 14% fundo / 55% borda), **aliases legados**
   (`--app-bg`, `--surface-*`, `--accent`, `--text-muted`…) e tokens comuns
   (botões coloridos, variáveis `--bs-*` do Bootstrap, dropdown)
3. `:root[data-theme="light"]` — paleta **clara** (escolha explícita)
4. `@media (prefers-color-scheme: light)` sobre `:root:not([data-theme])` —
   modo **auto** acompanhando o sistema

O **dourado foi aposentado**: `--accent` agora é a série **azul**
(`--c-blue`/`--t-blue`); dourados fixos (`#c2a36d`, `rgba(194,163,109,*)`)
viraram tokens azuis ou `color-mix`. Selos e botões coloridos usam **tinta de
série** (texto `--t-*` sobre fundo `--s-*-fundo` 14%) nos dois temas; no claro
só a ação primária é azul sólido com texto branco. Restos conhecidos: **6
cores fixas em `style=""`** em 3 Views (Caixa/Index ×4 `#004d1a`,
Home/Editar ×1, Home/Privacy ×1 — Views intocáveis por regra) e sombras
decorativas `rgba(0,0,0,*)` (não são texto/fundo de leitura).

**`Views/Shared/_Layout.cshtml`** — script inline no `<head>` **antes de
qualquer CSS**: lê a chave `controlhub-tema` do `localStorage`
(`auto`/`claro`/`escuro`), grava `data-theme` no `<html>` e expõe
`aplicarTema()`; no menu de perfil, três opções de texto
(`role="menuitemradio"` + `aria-checked` sincronizado). Sem `data-theme`, o
`prefers-color-scheme` do CSS decide junto com o SO.

**`qa/contrast.mjs` (novo)** — guarda de contraste: lê os blocos de paleta,
resolve `var()`/`color-mix()`, calcula a razão WCAG exata de 29 pares por
paleta (58 no total; 4.5:1 texto, 3.0:1 borda de ação) e confere que o bloco
auto é espelho exato do claro. **Achou e corrigiu** a borda do botão primário
escura (2.93 < 3.0 → tinta 55% → 70%).

**`qa/screenshot-themes.mjs` (novo)** — Chrome headless + CDP puro (sem npm):
12 prints (Login, Estoque, Caixa+modal de pagamento, Histórico, Usuários,
Dashboard × 2 temas). Credenciais só por variável de ambiente
(`PRINTS_EMAIL`/`PRINTS_SENHA`).

### Verificação (08/10/2026)

| Checagem | Resultado |
|---|---|
| `dotnet build --no-incremental` | 0 erros; 1 aviso **pré-existente** CS8602 (`_DetalheDiaPartial.cshtml(277)`) |
| `dotnet test tests.QA` | **288/288** |
| `node qa/encoding-check.mjs` | 148 arquivos, 0 problemas |
| `node qa/css-balance.mjs` | 331 pares balanceados |
| `node qa/contrast.mjs` | **58/58 pares**, auto = claro |
| `qa/http-assets.ps1` | 13/13 (6 assets × 2 com/sem gzip) |
| `qa/http-authz.ps1 -Porta 5321` | **131/131** + hash do `.db` versionado idêntico |
| Prints (6 telas × 2 temas) | 12 PNGs; luminância confere o tema em 11/12 — `caixa-modal-claro` mede 92 lux porque o overlay escuro cobre a página clara (o modal branco fica por cima; páginas escuras medem 16–29 lux) |
| `git diff` de arquivos protegidos | vazio (só CSS, `_Layout`, `ESTADO` e `qa/` novo) |

**Destino dos prints**: a tarefa pedia `E:\Sistema de Estoque\prints\`, mas
esta máquina **não tem unidade E:** — os PNGs ficaram em
`c:\Aplicações & Sistemas\Sistema de Estoque\prints\` (raiz do workspace).

### Regras mantidas

- `controleestoque.db` **modificado antes desta sessão** e **excluído do
  `git add`** (não foi tocado; hash confirmado pelo http-authz)
- Nenhuma migration, nenhum `--force`, nenhum push, testes intactos
- `@media print` e o cupom **não** foram alterados
- Nada em Models, Controllers, ApplicationDbContext, CaixaController, Export*

---

## 15. AJUSTE DE SHELL (1 DE 3) — MENU DO USUÁRIO ÚNICO, CHAVE DE TEMA E FAIXA ENXUTA (09/10/2026)

Tag de partida: `pre-ajuste-1` (`2cbf740`). Escopo: reescrever o shell
autenticado do `_Layout.cshtml` (sidebar, menu do usuário, chave de tema e
faixa superior) com verificação CDP/QA completa e **commit único**.

### O que mudou

- **`Views/Shared/_Layout.cshtml`** (ramificação `if (autenticado)` reescrita):
  - **Sidebar** `#app-sidebar` com marca `ControlHub`, grupos **Operação**
    (Estoque; Caixa se `podeVerFinanceiro`) e **Gestão** (Histórico; Dashboard
    — o grupo inteiro **dentro** do `@if (podeVerFinanceiro)`, corrige item
    órfão para o Operador), item ativo via `MenuAtivo(...)` (bug do ativo
    duplo em `/Caixa/Historico` corrigido: `MenuAtivo("Caixa","Index")` para o
    PDV).
  - **Menu do usuário único**: botão `#btnUsuario` (aria-haspopup/controls/
    expanded) + `#menuUsuario` (`role="menu"`) com `Usuários` (só Dono) e
    `Sair` (form com antiforgery). Sumiram `#btnMenuPerfil`, `.dropdown-menu`
    e o `<b>` da navbar antiga.
  - **Chave de tema**: `<button class="app-theme" role="switch"
    aria-checked>` com knob e ícones lua/sol; sem botão `#btnTheme` antigo.
  - **Faixa superior enxuta**: `.app-header` contém **só** `#btnMenu`
    (hambúrguer) + `.app-caminho` (grupo / página). Sumiram a busca
    (`#campoBusca`, `.busca-form`) e os atalhos de perfil.
  - Modais (`modalAtalhos`, `modalOperacao`, `modalAjudaAtalhos`) **fora** do
    if/else (compartilhados); `IConfiguration` injetado para `Marca:Nome`.
- **`wwwroot/js/shell.js`** (novo, sem dependências): menu do usuário
  (abre/fecha, Esc, clique fora, setas), chave de tema (alterna
  escuro/claro e persiste), hambúrguer (recolhe em ≥1024px persistindo
  `controlhub-sidebar`; gaveta + cortina abaixo de 1024px). Expõe
  `window.SHELL` para os testes.
- **`wwwroot/css/corporate-theme.css`**: blocos "AJUSTE DE SHELL" (sidebar,
  menu, chave, faixa, cortina, gaveta) e token `--knob-ink` nas 3 paletas
  (escuro `#0B1220`, claro/auto `#FFFFFF`).
- **`qa/cdp-helper.mjs`** (novo): infraestrutura CDP comum (Chrome headless +
  DevTools puro, sem npm) — login e medições reutilizáveis.
- **`qa/shell-check.mjs`** (novo): 7 verificações (V1 itens da sidebar por
  papel, V2 ativo/aria-current por tela + gate do Operador no Dashboard,
  V3 menu do usuário único, V4 chave de tema + contraste do knob ≥ 3:1 ao
  vivo, V5 faixa enxuta, V6 larguras 1440/390 com gaveta/cortina/Esc,
  V7 baseline) para **Dono + Operador × 2 temas × Estoque + Dashboard**;
  grava `prints\baseline-menu.json`.
- **`qa/contrast.mjs`**: novo par obrigatório "icone sob o knob / fundo do
  knob" (`knob-ink` vs `c-blue`, mínimo 3.0) nas duas paletas.
- **`qa/http-authz.ps1`**: 3 seletores de "o que o navegador mostra"
  atualizados para o markup novo (nome em `<span class="app-usuario__nome">`;
  Histórico com `?periodo=hoje`). Autorização (200/403/302) intocada.

### Verificação (09/10/2026)

| Checagem | Resultado |
|---|---|
| `node qa/shell-check.mjs` | **142/142** — 7 verificações; Dono+Operador, escuro+claro, 1440+390px, Estoque+Dashboard; knob 7.21:1 (escuro) e 5.17:1 (claro) |
| `dotnet build --no-incremental` | 0 erros; 1 aviso **pré-existente** CS8602 |
| `dotnet test tests.QA` | **288/288** (`TestResults/shell-ajuste1.trx`) |
| `node qa/encoding-check.mjs` | 153 arquivos, 0 problemas |
| `node qa/css-balance.mjs` | 444 pares balanceados |
| `node qa/contrast.mjs` | **60/60 pares** (58 + 2 do knob), auto = claro |
| `qa/http-assets.ps1` | 13/13 (6 assets × 2 com/sem gzip) |
| `qa/http-authz.ps1 -Porta 5323` | **131/131** + hash do `.db` versionado idêntico |
| `git diff` protegidos | vazio (só `_Layout`, CSS, `shell.js`, `ESTADO` e `qa/`) |
| grep do diff | nenhum `Html.Raw`/`innerHTML`/recurso externo novo (os 3 `http://` são localhost nos scripts QA) |
| Seed de dados de QA | Dono e Operador criados no banco **temporário** `%TEMP%\qa-shell.db` (app de QA na porta 5311); versionado intocado |

### Regras mantidas

- `controleestoque.db` continua **modificado de antes da sessão** e
  **excluído do `git add`**; hash SHA256 idêntico antes/depois do http-authz
- Nenhuma migration, nenhum `--force`, nenhum push; gates de permissão
  originais (policies BUG-13) preservados — o menu só pergunta ao mesmo
  `IAuthorizationService` das rotas
- Views intocáveis (Models/Controllers/`@media print`/cupom) não mudaram

---

## 16. AJUSTE DE SHELL (2 DE 3) — HISTÓRICO ABRE E ALT+M FUNCIONA (10/10/2026)

Escopo: fazer o **menu administrativo abrir** e o **Histórico abrir**, sem mudar
lógica de sangria/suprimento/fechamento/relatório/reimpressão/vendedores — só
navegação e o menu. Só `Views/Shared/_Layout.cshtml`, `wwwroot/css/corporate-theme.css`
(bloco novo no fim), `wwwroot/js/shell.js`, `wwwroot/js/site.js`, `qa/` e este
arquivo. `admin-caixa.js` **não** foi tocado (os botões 1–7 continuam chamando as
mesmas funções de lá).

### Causa de cada defeito (com evidência)

**Histórico.** No shell autenticado (novo) o item já apontava certo
(`/Caixa/Historico?periodo=hoje`, via `asp-route-periodo`) e abria HTTP 200 com
"Vendas de hoje" e zero erros de console (medido por CDP). O defeito real estava
no **navbar antigo** (branch `else`, telas de login/Setup): o href era
`/Caixa/Historóico?periodo=hoje` — com **acento errado em "Historóico"**, que não
bate com a action `Historico` e daria 404. Corrigido para `Historico`.

**Alt+M / atalhos.** Quatro causas, todas no handler de teclado inline do
`_Layout`:

1. **Casava por `e.key`** (`e.key === 'm'`, `'c'/'h'/'e'` via `toLowerCase`).
   `e.key` depende do layout de teclado: em layout não-US o Alt+letra devolve
   outro caractere e o atalho simplesmente não vem. Passou a casar por
   **`event.code`** (`KeyM`/`KeyC`/`KeyE`/`KeyH`, `Digit1..7`), idêntico em
   qualquer layout.
2. **Estado do overlay errado**: usava `bootstrap.Modal.getInstance(modal) !== null`,
   que só diz que a instância *existe* (mesmo escondida) — depois do 1º abrir/fechar
   o `Alt+M` parava de abrir. Passou a usar `modal.classList.contains('show')`.
3. **Esc não fechava** o overlay: o handler só alternava o `modalAjudaAtalhos` e, no
   PDV, o `#campoBusca` em foco era limpo antes de qualquer coisa. Agora, com o
   overlay aberto, **Esc o fecha primeiro**; **clique-fora** (backdrop) também fecha.
4. **Empilhamento**: garantido por bloco CSS explícito — `#modalAtalhos` (z 1080) e
   seu backdrop (1070) ficam acima da sidebar (1040) e da faixa superior (1030).

Permissões preservadas: `Alt+C`/`Alt+E` exigem `LOJA_ACESSO.pdv`; `Alt+H` exige
`LOJA_ACESSO.financeiro` (só Dono); `Alt+M` abre o overlay renderizado por
`temMenuAtalhos` (admin p/ Dono, do caixa p/ Operador). Nenhum destino nem policy mudou.

### O que mudou

- `Views/Shared/_Layout.cshtml`: handler de teclado reescrito (por `event.code`,
  `preventDefault`, estado real por `classList`, Esc e clique-fora fecham, mesmas
  policies e mesmos destinos, números 1–7 chamam os mesmos `data-key`); href do
  navbar antigo corrigido (`Historóico` → `Historico`).
- `wwwroot/css/corporate-theme.css`: bloco novo no fim (`#modalAtalhos` 1080,
  `body > .modal-backdrop` 1070) — 446 pares de chave, equilibrado.
- `qa/nav-smoke.mjs`: guarda nova (reaproveita `qa/cdp-helper.mjs`).
- `qa/up-shell.ps1`: sobe a instância de teste (porta 5311, `.db` temporário em
  `%TEMP%`) e cria Dono + Operador — base para a guarda e o diagnóstico.
- `qa/cdp-helper.mjs`: `aoEvento` (assinantes de eventos CDP) para a guarda contar
  erros de console e respostas `>= 400`.
- `wwwroot/js/shell.js` e `wwwroot/js/site.js`: **sem mudança** (shell.js só mexe na
  gaveta; site.js está vazio) — o handler único é o inline do `_Layout`.

### A guarda nova — `qa/nav-smoke.mjs`

Para **Dono e Operador**, nos **dois temas**: cada destino do menu (e "Usuários"
pelo menu do usuário, Dono) abre **HTTP 200**, breadcrumb coerente, **zero erros de
console** e **zero requisições `>= 400`**; `Alt+M` abre o overlay em todas as telas
e `Esc` o fecha; `Alt+C`/`Alt+E`/`Alt+H` navegam para o destino certo; o Operador
não vê itens fora da policy e `/Dashboard` dá acesso negado. Os atalhos são
disparados com `event.code` real e um `key` de layout não-US (ex.: `µ` para Alt+M):
só um handler por `code` passa — é assim que a regressão fica presa.

### Verificação (10/10/2026)

| Checagem | Resultado |
|---|---|
| `node qa/nav-smoke.mjs --base http://localhost:5311` | **82/82** — Dono+Operador, escuro+claro |
| `dotnet build --no-incremental` | 0 erros; 1 aviso **pré-existente** CS8602 em `_DetalheDiaPartial.cshtml` (confirmado no baseline com as mudanças em stash) |
| `dotnet test tests.QA` | **288/288** (`TestResults/ajuste2.trx`) |
| `node qa/encoding-check.mjs` | 153 arquivos, 0 problemas |
| `node qa/css-balance.mjs` | 446 pares balanceados |
| `node qa/contrast.mjs` | **60/60 pares**, 0 problemas |
| `qa/http-assets.ps1` | **13/13** (6 assets × 2 com/sem gzip) |
| `qa/http-authz.ps1` | **0 falhas** + hash do `controleestoque.db` versionado **idêntico** antes/depois |
| `git diff` protegidos | vazio (só `_Layout`, CSS, `cdp-helper.mjs`, `nav-smoke.mjs`, `up-shell.ps1`, `ESTADO`) |

### Regras mantidas

- `controleestoque.db` **intacto** (hash SHA256 idêntico antes/depois do http-authz)
  e **fora do `git add`**; seed de QA só no `.db` temporário em `%TEMP%`
- Nenhuma migration, nenhum `--force`, nenhum push; policies BUG-13 preservadas
- `admin-caixa.js`, `CaixaController`, Models, `ApplicationDbContext`, `Export*` e
  testes existentes **não** mudaram; capturas em `E:\Sistema de Estoque\prints\`

---

## 16. MODAIS (1 DE 3) — SETA DO MENU DO USUÁRIO (13/10/2026)

Tag de partida: `pre-modais` (`e49b795`). Feature isolada em 3 commits:
seta do menu do usuário; "Editar produto" em modal; "Nova conta" em modal.
**Nenhum controller muda** — os modais reaproveitam as mesmas rotas, o mesmo
formulário e o mesmo POST. Escopo deste commit (1 de 3): a seta do menu.

### O que mudou

- **`wwwroot/css/perfil-seta.css`** (novo): esconde o `<span
  class="app-usuario__chevron">` antigo e desenha a seta no `::after` do
  `#btnUsuario`. **Fechado** (`aria-expanded="false"`) a seta aponta **para
  cima** (`rotate(-135deg)`, matriz `a≈-0.707, b≈-0.707`); **aberto**
  (`aria-expanded="true"`) aponta **para baixo** (`rotate(45deg)`, `a≈0.707,
  b≈0.707`). Sem glifo, SVG, caractere ou fonte de ícone; cor só por token
  (`--text-soft`); respeita `prefers-reduced-motion`.
- **`Views/Shared/_Layout.cshtml`**: **+1 linha** (o `<link>` de
  `perfil-seta.css` logo após o `corporate-theme.css`), **0 removidas**. O HTML
  do botão não foi tocado.
- **`qa/perfil-seta-check.mjs`** (novo, reutiliza `qa/cdp-helper.mjs`): mede a
  matriz do `::after` por CDP para Dono e Operador, nos dois temas, em 1440 e
  390px; confirma seta única visível (span antigo escondido), retorno ao estado
  "para cima" por Esc, clique no botão e clique fora, e os itens do menu por
  papel (`["Usuários","Sair"]` Dono; `["Sair"]` Operador).

### Verificação (13/10/2026)

| Checagem | Resultado |
|---|---|
| `node qa/perfil-seta-check.mjs` | **64/64** — Dono+Operador, escuro+claro, 1440+390px |
| `dotnet build --no-incremental` | 0 erros; 8 avisos **pré-existentes** (baseline) |
| `dotnet test tests.QA` | **370 aprovados / 3 falhas** — falhas **idênticas** ao baseline (`DashboardTemplateQaTests.FormasPagamento_*`) |
| `node qa/encoding-check.mjs` | 168 arquivos, 0 problemas |
| `node qa/css-balance.mjs` | 727 pares equilibrados |
| `node qa/contrast.mjs` | 60/60 pares, 0 problemas |
| auto-conferência do diff | só `_Layout.cshtml` (+1/-0), `perfil-seta.css`, `perfil-seta-check.mjs`; `.db` intacto |

### Regras mantidas

- `controleestoque.db` **intacto** e fora do `git add`; seed de QA só no `.db`
  temporário (`trx\app-teste.db`, fora do repositório).
- Nenhum controller, `shell.js`, `corporate-theme.css` nem `repor*` tocados;
  nenhuma migration, nenhum push. Capturas em `prints\seta-*.png`.

---

## 17. MODAIS (2 DE 3) — "EDITAR PRODUTO" EM MODAL (13/10/2026)

Continuação da feature de 3 commits (`pre-modais` → `ad63132` → COMMIT 2 → COMMIT 3).
Escopo deste commit (2 de 3): abrir a edição de produto num modal reutilizando a
**mesma rota GET/POST** de `/Home/Editar`, o **mesmo formulário** e o **mesmo
token antiforgery** da tela cheia. **Nenhum controller muda.**

### O que mudou

- **`wwwroot/js/modal-form.js`** (novo, IIFE sem dependências): infraestrutura
  genérica de modal de formulário. Intercepta o clique em
  `<a data-modal-form data-modal-titulo="…">`, faz `fetch` do GET da rota,
  injeta o **primeiro** `[data-modal-conteudo]` via `importNode` (scripts do HTML
  buscado **não** rodam), monta overlay `.app-modal-overlay` por DOM e, no envio,
  faz POST com `new FormData(form)` + token antiforgery (mesma técnica de
  `repor-estoque.js`). Ctrl/Shift/Meta/clique do meio continuam sendo link
  normal; Esc/clique no fundo fecham (fundo sujo **não** fecha); foco preso no
  modal (Tab) e devolvido ao gatilho ao fechar; erro de GET mostra "Abrir em
  tela cheia"; duplo clique no Salvar envia **1 POST** (rede CDP).
- **`wwwroot/css/app-modal.css`** (novo): só o visual do modal (overlay, caixa,
  cabeçalho, corpo, responsivo, ajustes de formulário dentro do modal). Cor
  **somente por token**; única cor fixa permitida é o preto do backdrop.
- **`wwwroot/js/produto-form.js`** (novo): o `<script>` inline que vivia em
  `Editar.cshtml`, movido **sem mudar a lógica**; expõe
  `window.ProdutoForm.iniciar(raiz)`, auto-inicia na tela cheia e é reamarrado
  pelo `modal-form.js` ao injetar o conteúdo no modal.
- **`Views/Home/Index.cshtml`**: link "Editar" ganha
  `data-modal-form data-modal-titulo="Editar produto"`; carrega
  `app-modal.css`, `produto-form.js` e `modal-form.js` (o `importNode` não
  executa scripts do HTML buscado, então o JS precisa estar na página de origem).
  **+9/-?** linhas.
- **`Views/Home/Editar.cshtml`**: o formulário é envolvido em
  `[data-modal-conteudo]` com um `[data-modal-fechar]` ("Voltar") e carrega os
  mesmos 3 assets, de modo que a **tela cheia continua funcionando** idêntica.
- **`qa/modal-check.mjs`** (novo, CDP): guarda da feature. Conta POSTs de
  `/Home/Editar` e `/Usuarios/Criar` por **eventos de rede do CDP**
  (`criarContadorRede`/`lerContador`, `Network.enable`), que sobrevivem ao
  `location.reload()` do sucesso. Cobre editar (abre sem navegar, título,
  produto certo, rótulo por kg/un, Voltar+foco, fundo limpo/sujo, Tab preso,
  salvar recarrega mantendo busca, preço novo, duplo clique = 1 POST, GET
  falho → "Abrir em tela cheia", colunas estáveis), Ctrl+clique (não abre modal
  e não sequestra o link), Nova conta (pulada até o COMMIT 3), itens intactos,
  operador (sem Editar/Usuarios). **Os cheques de "Nova conta" são pulados com
  `[PULA]` enquanto o gatilho ainda não está em modal (COMMIT 3).**

### Verificação (13/10/2026)

| Checagem | Resultado |
|---|---|
| `node qa/modal-check.mjs` | **54 aprovadas / 0 falhas** — Dono escuro+claro; Nova conta `PULA` (COMMIT 3) |
| `node qa/shell-check.mjs` | **142 aprovadas / 0 falhas** (V1–V6) |
| `powershell qa/http-assets.ps1` | **13 OK / 0 falha** (6 assets × 2 req) |
| `node qa/encoding-check.mjs` | 170 arquivos, 0 problemas |
| `node qa/css-balance.mjs` | 727 pares equilibrados |
| `node qa/contrast.mjs` | 60/60 pares, 0 problemas |
| `dotnet test tests.QA` | **370 aprovados / 3 falhas** — falhas **idênticas** ao baseline (`FormasPagamento_*`) |
| `powershell qa/http-authz.ps1` | 129 OK / **2 falhas pré-existentes** (texto do Dashboard "Vendas de hoje"/"ltimos 7 dias"; `Views/Dashboard` + `http-authz.ps1` = 0 diff vs baseline). Checagens do Estoque (`btn-novo`, `btn-editar`, permissões) **todas OK** |
| auto-conferência do diff vs `pre-modais` | só arquivos permitidos + `_Layout` (+1/-0, do COMMIT 1); `.db` intacto |

As 2 falhas do `http-authz` e as 3 do `dotnet test` são **pré-existentes** e
**não** relacionadas aos modais: `Views/Dashboard`, `tests.QA` e
`qa/http-authz.ps1` estão **byte-idênticos** ao baseline `pre-modais`
(`git diff --stat pre-modais` = 0 linhas para esses caminhos).

### Regras mantidas

- `controleestoque.db` **intacto** (SHA256 idêntico no `http-authz`) e fora do
  `git add`; seed de QA só no `.db` temporário (`trx\app-teste.db`).
- **Nenhum controller**, nenhum `shell.js`, nenhum `corporate-theme.css` e
  nenhum `repor*` tocados; nenhuma migration, nenhum push.
- Escopo do commit: `app-modal.css`, `modal-form.js`, `produto-form.js`,
  `Views/Home/Editar.cshtml`, `Views/Home/Index.cshtml`, `qa/modal-check.mjs`,
  esta nota. Capturas em `prints\modal-*.png`.

---

*Fim do relatório. Gerado em 02/10/2026, atualizado em 13/10/2026 (modais 1 e 2 de 3) a partir de `main@e49b795`.*



