# 📋 ESTADO DO PROJETO — ControleEstoqueLoja

> Documento de passagem de bastão. Gerado em **02/10/2026**.
> Objetivo: permitir que outra pessoa (ou outra IA) compreenda o sistema
> **sem ver o código**, e decida os próximos passos com base em fatos.
>
> **Repositório:** https://github.com/LeonardoLibanori/ControleEstoqueLoja
> **Branch:** `main` · **HEAD:** `bfd9d43` (= `origin/main`, árvore limpa)

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
| Testes | **222/222** + 1 ignorado (BUG-28) (`tests.QA`, xUnit) — `dotnet test tests.QA/tests.QA.csproj` |
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
│   ├── DashboardController.cs   painel inicial (só Dono): leitura pura, regra 25
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
├── tests.QA/                   222 testes xUnit de caracterização
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
| **Dashboard inicial (Início)** | `b3414bf` | item de menu só para o Dono: vendas do dia, dinheiro na gaveta, quebra por forma de pagamento, estoque baixo, últimos 7 dias e últimas vendas |

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
dotnet test tests.QA\tests.QA.csproj     # deve dar 222/222 + 1 ignorado
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
4. Rodar a suíte completa antes de commitar (222 testes — `dotnet test tests.QA/tests.QA.csproj`; note que o `.slnx` **não** inclui o projeto de testes)
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
- **Sem** dashboard
- Tabela `FluxoCaixa` é **órfã** (criada, nunca usada pelo código)
- **`Produto.Quantidade` continua em TEXT** (limitação consciente: comparação numérica em SQL seria errada)
- Gestão de usuários **não permite editar/desativar** conta existente (só criar e listar) — desativar Operador exigiria alteração direta no banco
- **Backup do banco só acontece na troca de máquina** (o `sincronizar.ps1` grava
  uma cópia em `backups-banco/` quando roda). **Não há** backup automático
  diário com rotação — se ninguém sincronizar, o GitHub é a única cópia

### Funcional
- **BUG-28** — `CaixaController.ObterStatusAtual` (L315-345) **não filtra `IsFechado`** nas três somas (L321, L325, L331), enquanto a regra 7 em `RegistrarMovimentacaoInterno` filtra (L263, L268, L273). Efeito prático: depois do fechamento do dia, a **"CONFERÊNCIA DE CAIXA"** do PDV (`admin-caixa.js` L113, disparada pelo botão CAIXA em `_Layout` L236) mostra **"SALDO EM GAVETA"** somando vendas já fechadas, e esse número é **maior** que o do dashboard. A sangria, todavia, respeita o saldo correto. Teste que registra o comportamento esperado: `DashboardQaTests.Dashboard_Gaveta_IgualAoStatusAposVendaFechada_ComportamentoEsperado` (ignorado). Corrigir exige mexer em `CaixaController`, fora do escopo deste commit
- **`FaturamentoMes` e `FaturamentoAno` do Histórico comparam só mês/ano** (`CaixaController.L462`, `L466`): `DataVenda.Month == Today.Month` soma também o mesmo mês de **todos os anos anteriores**. "Faturamento do ano" inclui janeiro do ano passado
- **O fechamento só vira flag e não guarda valor declarado**: `ZerarCaixaParaProximoDia` (L847) apenas marca `IsFechado = true` em vendas e movimentações. Não existe entidade de sessão de caixa nem total declarado — o dinheiro que o dono contou na gaveta **não é gravado em lugar nenhum**
- **Vendas e movimentações não fechadas de dias anteriores ficam fora do saldo**: o filtro é sempre `Data.Date == hoje`, então um dia que o dono esqueceu de fechar "verte" para o dia seguinte (as linhas somem do saldo, mas continuam no histórico)
- **`Controllers/FluxoCaixaController.cs` é arquivo morto fora do build**: excluído por `<Compile Remove>` no `.csproj` L17 (e não compilaria mesmo — usa `_context.OperacoesCaixa`, que não existe). Sem `[Authorize]`, mas **inalcançável** (a rota responde 404). Candidato a remoção; se alguém tirar o `Compile Remove`, ele volta ao build sem proteção
- **`Views/Caixa/Index1.cshtml`, `Index2.cshtml` e `Index3.cshtml` são views órfãs**: nenhuma está no menu nem em rota. `Index1` ainda duplica o CDN do SweetAlert2 (L95). Candidatas a remoção
- **Cinco recursos externos no `_Layout`** quebram a operação offline: fontes do Google (L100-102), Bootstrap Icons via jsdelivr (L104) e SweetAlert2 via jsdelivr (L363). Sem internet os ícones `bi-*` do menu somem e os alertas do PDV não abrem. O texto dos itens de menu **não** depende de ícone — o item "Início" foi criado assim de propósito

### Técnico
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

### (c) Integrações com IA
11. **Previsão de demanda / sugestão de reposição** (usa estoque mínimo + histórico)
12. **Leitura de nota fiscal (OCR + LLM)** para entrada de produtos
13. **Copiloto de consulta** sobre financeiro/estoque — a policy `PodeUsarIA`
    **já está criada e registrada, sem endpoint**: pronta para ser usada

---

## 12. COMO VERIFICAR TUDO QUE ESTÁ AFIRMADO AQUI

```powershell
# build limpo
dotnet build                              # 0 erros, 0 avisos

# suíte completa
dotnet test tests.QA\tests.QA.csproj     # 222/222 + 1 ignorado

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

*Fim do relatório. Gerado em 02/10/2026, atualizado a partir de `main@bfd9d43`.*



