# Relatório de QA — Sistema de Controle de Estoque

**Branch:** `fix/fase1-dinheiro` · **HEAD (código medido):** `702ae85` · **Última atualização:** 29/09/2026

> Esta suíte foi **recriada do zero** após a troca de máquina. As pastas `tests.QA/` e
> `qa/` existiam apenas no PC antigo e nunca foram versionadas. A partir de agora
> ambas estão no Git (ver `git log`).

> **Nomenclatura:** a marca exibida na tela (`<title>` e rodapé) é o nome **genérico** do
> produto, *Controle de Estoque e Caixa*. O nicho ainda não foi decidido, então nenhum nome
> de loja aparece no código. Os **dados fiscais do cupom são fictícios e ainda estão
> hard-coded** em `Views/Caixa/GerarCupom.cshtml` — ver *Dados da empresa* no `README.md`.
>
> **Contas de teste do banco real:** o `controleestoque.db` da loja tem **uma conta de Dono
> com e-mail provisório** (nome de nicho antigo). **Não existe tela de edição de usuário**
> (o controller só tem `Index` e `Criar`) e o `/Account/Setup` já está fechado — a troca fica
> para quando o sistema for usado de verdade. Detalhes e caminho futuro no `README.md`.
> Os e-mails dos testes (`dono@teste.com`, `operador@teste.com`) são de banco descartável em
> `%TEMP%` e **nunca tocam o banco real**.

## Como executar

```powershell
cd ControleEstoqueLoja\tests.QA
dotnet test
```

Cada teste cria um **banco SQLite isolado em `%TEMP%`** (`qa-teste-<guid>.db`) e o remove
ao final. **O `controleestoque.db` versionado nunca é aberto pela suíte.**

### Testes por HTTP real (aplicação de verdade, banco isolado)

```powershell
powershell -ExecutionPolicy Bypass -File .\qa\http-authz.ps1
```

Os scripts sobem a aplicação com `dotnet run` apontando para um banco descartável em
`%TEMP%` e conferem o hash SHA256 do `controleestoque.db` **antes e depois** — falham se
o banco versionado mudar.

| Script | Escopo | Estado |
|---|---|---|
| `qa/http-authz.ps1` | Autorização dos 3 perfis (anônimo / Operador / Dono) + **o que cada um vê na tela** — **BUG-13** | ✅ **115/115** checagens, 0 falhas |
| `qa/http-parte1.ps1` | Vendas e caixa da Fase 1 (BUG-01/02/05/06) | ⚠️ **histórico/superado**: foi escrito **antes** do BUG-14 (não envia token anti-forgery), do BUG-13 (não faz login) e da divisão de `RegistrarMovimentacao` (a rota antiga hoje responde 405). Acumula **14 falhas de preparação que não são bugs atuais**; para voltar a valer precisa do mesmo preâmbulo de Setup/login e das rotas novas de sangria/suprimento. |

## Estado atual

| Métrica | Valor |
|---|---|
| Total de testes | **72** |
| ✅ Aprovados (comportamento correto) | **72** |
| ❌ Falhando (bug presente) | **0** |

Última atualização: **01/10/2026** — **preço direto por kg (`PorPeso`)** e **tema corporativo**:
o tipo do produto deixou de se chamar `Granel` (nome do *local de guarda*, não da forma de
venda) e virou `PorPeso`; a tabela guarda o preço em **R$/kg** e a venda multiplica pela
quantidade, **sem fator escondido** (`CalcularSubtotalItem` perdeu o `x10`, e o cupom, o
carrinho e o JS do admin passaram a receber `unidade`/`regra` do servidor em vez de adivinhar
pelo nome do tipo). A migration `20261001120000_ProdutoPrecoDiretoPorQuilo` converte o preço
**já gravado** dos produtos por peso (centavos inteiros, `x10` exato, com uma guarda que
**aborta a migration sem alterar nada** se algum preço por peso não estiver em centavos
inteiros ou se o `x10` puder estourar o `INTEGER`); o `Down` é **proibido de propósito** —
depois de cadastrar preço em R$/kg não dá mais para separar os convertidos dos novos. A suíte
está **100% verde (72/72)**: entram o teste da conversão (`ProdutoPrecoPorQuilo_...`, medido no
arquivo da base legada) e os **dois testes de troco** que o `falhas.csv` (FIX-04) já citava e
que **não existiam** na suíte. `wwwroot/css/site.css` saiu e entrou
`wwwroot/css/corporate-theme.css` (com `.btn-voltar-borda`, que vivia no `site.css`).

Antes disso, **30/09/2026** — **dinheiro em `INTEGER` de centavos (BUG-20)**:
as 7 colunas monetárias saíram de `TEXT` para `INTEGER` de centavos
(`Data/ConversorCentavos.cs` + a migration `DecimalParaCentavos`, que **converte o
dado já gravado** e recria os índices das tabelas reconstruídas). A suíte voltou a
ficar **100% verde (57/57)**, e o teste do BUG-20 foi **reescrito para medir o
esquema efetivo** — snapshot do modelo, arquivo SQLite criado pelo
`Database.Migrate()` e o número cru — em vez de procurar texto no arquivo de uma
migration histórica. Ver *Corrigido em 30/09/2026* abaixo (`fd30124`).

Antes disso, **29/09/2026** — **autenticação e autorização (BUG-13)**:
`[Authorize]` + policies nomeadas em todas as rotas (`85d6797`), a tela de
gestão de usuários só-Dono (`702ae85`) e o **menu por papel** nas views: o que a
pessoa não tem permissão de fazer não aparece na navegação, nem no Alt+M, nem
como botão na tela. Antes dessas três, três correções de
segurança **sem** autenticação: XSS armazenado (BUG-12), vazamento de
`ex.Message` (BUG-15/16/17) e token anti-forgery em todos os POSTs (BUG-14).
Ficou vermelho naquela data apenas `Decimal_NaoDeveSerPersistidoComoText`
(**BUG-20**), corrigido em 30/09/2026.
O teste HTTP dos 3 perfis, que até então era um script descartável em `%TEMP%`,
passou a ser **versionado** em `qa/http-authz.ps1` (115/115 checagens, 49 delas
sobre o HTML que cada perfil recebe) — ver *Como executar*.

Os 2 testes de concorrência foram recuperados do notebook antigo em 29/09/2026
(commit `11b88f0`): ambos passam, porque a branch atual já rejeita a venda
concorrente excedente (a Fase 1 adicionou a rechecagem de estoque). Eles
documentam o BUG-05 (falta de transação/lock) para a Fase 2.

Os testes são de **caracterização**: falham enquanto o bug existir e passam quando for
corrigido. Nenhum código de produção foi alterado para fazê-los passar.

> ⚠️ **Nota sobre contagem:** dois testes são `[Theory]` com 2 casos cada
> (`RegistrarMovimentacao_DeveRejeitarValorNaoPositivo` e `Novo_DeveRejeitarPrecoNegativo`).
> Por isso **15 bugs** correspondem a **16 casos de teste**. Os números acima são de
> **casos**, não de métodos.

---

## Lacunas conhecidas

Comportamentos que **não são bugs** — o sistema funciona como foi projetado — mas
que são limitações conscientes da versão atual. Ficam registrados aqui para não
serem confundidos com falhas nem redescobertos como "novidade" em uma próxima fase:

- **Gestão de usuários não permite editar/desativar conta existente** — hoje só é
  possível criar e listar. Desativação de Operador que saiu da empresa exigiria
  alteração direta no banco. A tela `/Usuarios` mostra a situação de cada conta
  (Ativo/Bloqueado por tentativas erradas), mas não tem ação de editar, trocar
  senha ou inativar.

- **`Quantidade` continua `TEXT` no SQLite** — a conversão de centavos (BUG-20)
   cobre **dinheiro**, e quantidade não é dinheiro: produto PorPeso tem 3+ casas
   decimais (`1,235 kg`) e quilo não tem centavo. Aplicar o conversor a ela seria
  errado por princípio: o `ConversorCentavos` **recusa** a 3ª casa decimal (por
  design — ele não arredonda dinheiro), então a pesagem passaria a lançar exceção
   na primeira venda por peso. Consequência aceita: `ORDER BY`/`SUM` por
  **quantidade** segue lexicográfico no banco, e o valor não é monetário — não
  entra em troco, sangria nem total. O teste do BUG-20 **audita a lista**: se
  nascer coluna nova de dinheiro como `decimal`/`TEXT`, a suíte quebra — a dívida
  não cresce sozinha.

---

## ✅ Corrigido em 28/09/2026 — CASCADE e Migrate (Fase 2, etapa 3)

Primeira etapa com **migration** (schema alterado no banco versionado).

| ID | O que foi corrigido | Commits | Teste |
|---|---|---|---|
| BUG-08 | `ON DELETE CASCADE` em `VendaItens.ProdutoId` apagava o histórico de vendas | `f231d8b` (migration) · `450180d` (soft delete) | `ProdutosQaTests.Excluir_DevePreservarHistoricoDeVendas_DoProduto` |
| BUG-19 | `EnsureCreated()` ignorava as migrations | `037f8ad` · `b00a67e` (comentário) | `DadosQaTests.Program_DeveUsarMigrate_EmVezDeEnsureCreated` |

### BUG-08 — como ficou

1. **Migration `20260928234611_ProdutoAtivo_ExclusaoLogica_VendaItemRestrict`:**
   - Recria a FK `VendaItens.ProdutoId` com **`ON DELETE RESTRICT`** (era `CASCADE`).
     O banco passa a **recusar** a exclusão física de um produto que já tem itens de venda.
   - Adiciona `Produtos.Ativo INTEGER NOT NULL DEFAULT 1`.
   - ⚠️ O EF gerou `defaultValue: false` (default do CLR para `bool`), o que
     **desativaria todos os produtos cadastrados** e os faria sumir das listagens.
     Corrigido para `true` **antes de qualquer aplicação**.
   - O SQL recria a tabela `VendaItens` com `INSERT ... SELECT`, copiando todas as
     linhas: **nenhum dado de venda é perdido**.
2. **Exclusão lógica:** `HomeController.Excluir` marca `Ativo = false` (nunca
   `Remove()`). O produto some da operação mas continua no banco com todo o histórico.
3. **Filtros `Ativo`** em `HomeController.Index/Produto/Editar/Movimentar/AjustarEstoque`
   e na checagem de código de barras; em `CaixaController.Index` (grid do PDV),
   `BuscarPorId`, `BuscarPorCodigo`; e `FinalizarVenda` **recusa** produto desativado.

**Validação por HTTP real (banco real, com backup feito antes):**
- Excluir o produto com venda → **HTTP 302** (sucesso), não 500
- `BuscarPorId` do produto desativado → `success=false`; do ativo → `success=true`
- Grid do PDV lista **só o produto ativo**
- **As 2 vendas permaneceram intactas**
- Dados no banco real após a migration: 2 produtos, 1 vendedor, 2 vendas

**Mudança no banco versionado:** `controleestoque.db` foi de 53.248 para 57.344 bytes
(hash `D38374DA…1609` → `99A4CFDF…96B6`). Backup do schema anterior em
`%TEMP%\backup-pre-migration\`.

### BUG-19 — como ficou
`Program.cs` trocou `EnsureCreated()` por `Migrate()`. Com a migration aplicada,
`Migrate()` reporta *"No migrations were applied. The database is already up to date."*,
confirmando que a base existente é reconhecida e preservada. O commit `b00a67e` apenas
ajustou a redação de um comentário: o teste do BUG-19 faz `DoesNotContain` no texto do
arquivo e o comentário citava o nome do método antigo por extenso.

**Pré-requisito:** `dotnet-ef` 10.0.3 instalado e `dotnet-tools.json` versionado
(`345b07b`).

---

## ✅ Corrigido em 28/09/2026 — Produtos, Vendedores e Lacunas (Fase 2, parte 2)

Sem migration, sem tocar em Segurança.

| ID | O que foi corrigido | Onde | Teste |
|---|---|---|---|
| BUG-07 | Editar produto zerava o estoque: `TryParse` sem checar o retorno fazia `Quantidade = 0` quando a view não envia o campo | `HomeController.Editar` | `ProdutosQaTests.Editar_DevePreservarEstoque_QuandoCampoQuantidadeNaoEnviado` |
| BUG-09 | Cadastro aceitia código de barras duplicado (quebrava o PDV ao bipar) | `HomeController.Novo` | `ProdutosQaTests.Novo_DeveRejeitarCodigoDeBarrasDuplicado` |
| BUG-10 | Cadastro aceitia preço negativo | `HomeController.Novo` | `ProdutosQaTests.Novo_DeveRejeitarPrecoNegativo` |
| BUG-11 | Cadastro aceitava estoque negativo | `HomeController.Novo` | `ProdutosQaTests.Novo_DeveRejeitarEstoqueNegativo` |
| BUG-18 | `ValidarCupom` (ToUpper) e `FinalizarVenda` (=) divergiam em caixa | `CaixaController` | `VendedoresQaTests.Cupom_DeveAceitarMesmoCodigoEmCaixaDiferente_NasDuasEtapas` |
| BUG-21 | `TempData["Erro"]` nunca exibido em nenhuma view | `Views/Shared/_Aviso.cshtml` + `Produto.cshtml`/`Index.cshtml` | `LacunasQaTests.TempDataErro_DeveSerExibidoNaView` |
| BUG-22 | `HomeController` sem action `Error` (handler global apontava para 404) | `HomeController.Error` + `Views/Home/Error.cshtml` | `LacunasQaTests.HomeController_DevePossuirActionError` |

**Detalhes:**

- **BUG-07:** `Quantidade` só é sobrescrita quando o campo foi de fato enviado e é um
  número válido. O ajuste continua sendo feito por `AjustarEstoque`.
- **BUG-09/10/11:** as rejeições usam `TempData["Erro"]` + redirect, o mesmo padrão já
  usado por `AjustarEstoque`, e aparecem ao operador via SweetAlert2 (ver BUG-21).
- **BUG-18:** novo `CaixaController.NormalizarCupom()` (`Trim().ToUpper()`) é aplicado em
  `ValidarCupom`, `FinalizarVenda`, `CadastrarVendedor` (grava normalizado e bloqueia
  duplicado normalizado), `ExcluirVendedor` e `VerVendasVendedor`.
- **BUG-21:** novo partial `Views/Shared/_Aviso.cshtml` renderiza o `TempData` como alerta
  SweetAlert2 (já carregado no layout), sem duplicar HTML entre as views. A mensagem viaja
  em atributo `data-mensagem` (escapado pelo Razor), não interpolada em `<script>`.
- **BUG-22:** a action `Error` + view em português **não expõem stack trace nem
  `ex.Message`** — apenas o identificador da requisição para correlacionar com o log.

---

## ✅ Corrigido em 28/09/2026 — Caixa (Fase 2, parte 1)

Fonte única de verdade em `CaixaController`:

```csharp
public const string MetodoPagamentoDinheiro = "Dinheiro";

public static decimal CalcularSaldoEmGaveta(decimal vendasDinheiro, decimal suprimentos, decimal sangrias)
    => vendasDinheiro + suprimentos + sangrias;
```

A sangria é gravada **negativa** (`-Math.Abs`), então entra por **adição** — subtrair
inverteria o sinal.

| ID | O que foi corrigido | Onde | Teste |
|---|---|---|---|
| BUG-01 | Sinal da sangria invertido: `(total + suprimentos) - sangrias` somava a sangria | `ObterDadosFechamento` | `CaixaQaTests.ObterDadosFechamento_DeveSubtrairSangriaDoSaldo` |
| BUG-02 | Saldo da gaveta incluía vendas em cartão | `ObterStatusAtual` | `CaixaQaTests.ObterStatusAtual_SaldoDeveConsiderarSomenteDinheiro` |
| BUG-03 | Saldo do relatório incluía vendas em cartão | `RelatorioDados` | `CaixaQaTests.RelatorioDados_SaldoFinalDeveConsiderarSomenteDinheiro` |
| BUG-04 | Saldo do fechamento incluía vendas em cartão | `ObterDadosFechamento` | `CaixaQaTests.ObterDadosFechamento_SaldoDeveConsiderarSomenteDinheiro` |
| BUG-05 | Sangria maior que o saldo era aceita | `RegistrarMovimentacao` | `CaixaQaTests.RegistrarMovimentacao_DeveRejeitarSangriaMaiorQueOSaldo` |
| BUG-06 | Valor `<= 0` gerava movimentação fantasma | `RegistrarMovimentacao` | `CaixaQaTests.RegistrarMovimentacao_DeveRejeitarValorNaoPositivo` |

**Comportamento não alterado:** `RegistrarMovimentacao` continua **síncrona** (assinatura
inalterada) e o vazamento de `ex.Message` (BUG-17, Segurança) segue **pendente** por estar
fora do escopo desta etapa.

`qa/http-parte1.ps1` validou os mesmos pontos por HTTP real (primeira execução em
28/09/2026): **todas as verificações passaram** e o `controleestoque.db` versionado
permaneceu com o hash `D38374DA...1609`. **Atenção:** esse script é anterior ao
BUG-14/BUG-13 e hoje não roda mais contra a aplicação atual — ver o aviso em
*Como executar*.

---

## Severidade Crítica (pendentes)

_Nenhum. Os dois críticos de segurança foram corrigidos em 29/09/2026
(BUG-12 XSS e BUG-14 CSRF — ver "Corrigido em 29/09/2026" abaixo)._

---

## Severidade Alta (pendentes)

_Nenhum pendente. `DadosQaTests.Decimal_NaoDeveSerPersistidoComoText` (**BUG-20**),
único vermelho desde 28/09/2026, foi corrigido em 30/09/2026 — ver
*Corrigido em 30/09/2026 — Dinheiro em `INTEGER` de centavos`._

> **Fica `TEXT`, e é decisão consciente:** `Quantidade` (`Produtos` e `VendaItens`)
> não entrou na conversão — ver *Lacunas conhecidas*.

---

## Severidade Média

_Nenhum pendente. BUG-15/16/17 (vazamento de `ex.Message`) foram corrigidos em
29/09/2026._

> **Filtro de data do Histórico:** `CaixaController.Historico` **aplica corretamente** o
> filtro no controller, então `LacunasQaTests.Historico_DeveRespeitarOFiltroDeData` **passa**.
> O sintoma percebido na tela está na camada de rota/view e não é coberto por teste
> unitário — o script `qa/http-parte1.ps1` que cobria esse caminho é
> **histórico/superado** (ver *Como executar*), então o ponto continua **aberto**
> e sem reprodução automatizada.

---

## ✅ Corrigido em 29/09/2026 — Segurança sem autenticação

Três correções que **não exigem sistema de login**. A autenticação (`[Authorize]`)
ficou de fora de propósito: é uma feature maior, a desenhar com calma.

| ID | O que foi corrigido | Commit | Teste |
|---|---|---|---|
| BUG-12 | XSS armazenado no nome do produto | `4b06257` | `SegurancaQaTests.NomeDeProduto_ComScript_DeveSerSanitizadoParaOInnerHTML` |
| BUG-15/16/17 | Vazamento de `ex.Message` ao cliente | `f11a4b1` | `ListarVendedores_...` · `ObterDadosFechamento_...` · `RegistrarMovimentacao_...` |
| BUG-14 | POSTs sem token anti-forgery (CSRF) | `c4fc2f6` | `SegurancaQaTests.PostActions_NaoDevemFaltarAntiForgery` |

### BUG-12 — XSS armazenado (dupla camada)

1. **JavaScript** (`wwwroot/js/admin-caixa.js`) — as linhas que interpolavam
   `${nome}` em `innerHTML`/`insertAdjacentHTML` passaram a montar os nós com
   `createElement` + `textContent` (sugestões de busca e linhas do carrinho).
   O texto sempre aparece; a tag nunca vira elemento.
2. **Servidor** (`HomeController`) — helper `SanitizeTexto` remove tags
   `<...>` e escapa `& < > " '` no nome gravado, aplicado em `Novo` e `Editar`
   (defense-in-depth: o teste exige que o payload não chegue intacto ao banco).

**Verificado manualmente** (app real, banco isolado): cadastrar
`<script>alert(1)</script>` e `<img src=x onerror=alert(1)>` → o nome volta
como `ALERT(1)` (sem tag) e o HTML do PDV não contém o payload cru.

### BUG-15/16/17 — Vazamento de mensagem de exceção

Criados `LogarExcecao(origem, ex)` (escreve a exceção completa em
`Console.Error`, ou seja, no log do servidor — ainda não há `ILogger`
configurado) e a constante `MensagemErroGenerica`. Aplicados em
`RegistrarMovimentacao`, `ObterRelatorioResumido`, `ObterDadosFechamento`,
`ZerarCaixaParaProximoDia` e `ListarVendedores`. O cliente deixa de receber
`SqliteException`, `no such table: Vendas` e `FormatException`.

### BUG-14 — Token anti-forgery (CSRF)

1. `[ValidateAntiForgeryToken]` nos **5 POSTs** do `CaixaController` e nos
   **5 POSTs** do `HomeController`.
2. `@Html.AntiForgeryToken()` nos formulários de `Home/{Novo,Editar,Index,Produto}`.
3. `Program.cs`: `AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN")` para os
   POSTs que não passam por `<form>`.
4. `_Layout.cshtml`: `<meta name="csrf-token" content="@Antiforgery.GetAndStoreTokens(Context).RequestToken" />`.
5. `admin-caixa.js`: helper `csrfHeaders()` aplicado em `FinalizarVenda`,
   `RegistrarMovimentacao` e `ZerarCaixaParaProximoDia`.

**Verificado manualmente** (app real, banco isolado): POST direto **sem
token** em `/Caixa/RegistrarMovimentacao` e `/Home/Novo` → **HTTP 400** e
**nada é gravado** (o suprimento de 999 do cenário sem token não aparece no
caixa); POST **com** token → **HTTP 200** e gravado. Ou seja, a tela continua
funcionando e o ataque CSRF é barrado.

> **Nota de operação:** o token anti-forgery é emitido por cookie. Se o
> navegador ficar muito tempo sem recarregar a página, o cookie expira e o
> próximo POST volta 400 — recarregar a tela resolve. Ainda não há
> autenticação, então o token só protege contra CSRF, **não** contra acesso
> direto por quem alcança a URL na rede (era o BUG-13, corrigido em `85d6797`).

---

## ✅ Corrigido em 29/09/2026 — Autenticação e autorização (BUG-13)

O sistema deixou de ser aberto na rede: **toda rota de domínio exige login e
policy**. Quatro entregas, nesta ordem:

| ID | O que foi feito | Commit | Teste |
|---|---|---|---|
| — | Identity, migration, cookie de 12 h e papéis Dono/Operador | `ae12be8` | — |
| **BUG-13** | `[Authorize]` + policy por rota; sangria separada do suprimento | `85d6797` | `SegurancaQaTests.Controllers_NaoDevemExporEndpointsSemAutorizacao` |
| — | Tela de gestão de usuários (só Dono) — sem ela não havia como criar Operador | `702ae85` | HTTP manual (abaixo) |
| **BUG-13** | Menu, atalhos e botões por papel nas views (o que não pode, não aparece) | `d71c2b2` | `qa/http-authz.ps1`, seção 6 — 49 checagens no HTML |

### Como ficou

1. **`[Authorize]` no nível de CLASSE** em `CaixaController`, `HomeController` e
   `AccountController` (padrão do projeto): nenhuma rota fica anônima por
   esquecimento. Uma action nova já nasce protegida.
2. **Cada action declara a policy que exige** (20 no caixa, 9 na home). A regra
   mora em `Security/Policies.cs` (fonte única, registrada em `Program.cs`);
   **não existe `if (User.IsInRole(...))` espalhado no código**.
3. **`AccountController`**: cada action leva `[AllowAnonymous]` **de propósito**
   (Login, Logout, Setup, AcessoNegado) — se o login exigisse autenticação,
   ninguém conseguiria entrar. `Privacy` e `Error` também são anônimas.
4. **`RegistrarMovimentacao` foi dividida** em `RegistrarSuprimento`
   (`PodeRegistrarSuprimento`: Operador + Dono) e `RegistrarSangria`
   (`PodeRegistrarSangria`: só Dono). A permissão passou a morar na **rota**: um
   Operador que chame `/Caixa/RegistrarSangria` leva **403**. A rota única antiga
   (tipo no corpo do POST) era exatamente a brecha — aceitava sangria de qualquer
   um. Sangria continua validando o saldo da gaveta; suprimento não.
5. **Acesso negado com duas respostas** (só o `AccessDeniedPath` quebrava o
   painel): navegação vai para `/Account/AcessoNegado`; chamada **de dados**
   (`fetch` do painel, `curl`) recebe **403** direto. Antes o `fetch` seguia o
   302, recebia a página HTML de acesso negado com status **200** e o tratamento
   de 403 do `admin-caixa.js` nunca rodava: o operador via "erro inesperado" em
   vez de "apenas o Dono pode registrar sangria", e o 200 dava a impressão de que
   a sangria tinha passado.
6. **`admin-caixa.js`** chama as rotas novas (sangria/suprimento conforme o tipo)
   e se identifica como AJAX (`X-Requested-With`), junto do `X-CSRF-TOKEN` que
   já enviava.
7. **Tela `/Usuarios`** (policy `PodeGerenciarUsuarios`, Dono apenas): lista nome,
   e-mail, papel de cada conta e situação; cria conta com nome, e-mail, senha e
   papel (Operador por padrão). O papel vindo do POST é validado contra a lista
   conhecida **antes** de tocar no Identity.
8. **O que cada papel VÊ** (`Views/Shared/_Layout.cshtml`, `Views/Home/Index.cshtml`
   e `Views/Home/Produto.cshtml`): as views perguntam à **mesma**
   `IAuthorizationService` das rotas — não existe um `if (role == "Operador")` em
   nenhuma view. Na prática: a barra superior mostra nome e papel e esconde
   `Histórico` (`PodeVerFinanceiro`) e `Usuários` (`PodeGerenciarUsuarios`) de quem
   não pode; o **Alt+M** lista só os itens utilizáveis (o Operador vê suprimento,
   reimprimir e vendedores — não vê sangria, caixa, fechamento nem relatório);
   `Sair` só aparece logado; e a tela de estoque, que o Operador abre como
   consulta, esconde `+ Novo Produto`, `Editar`/`Excluir` e o formulário de ajuste
   (mostra no lugar o aviso "ajuste de estoque é função do dono"). O
   `admin-caixa.js` recebe `window.LOJA_ACESSO` com as mesmas flags para esconder
   o que ele desenha em runtime (botão de novo vendedor). **Nada disso é
   segurança**: quem digitar `/Usuarios` na barra de endereços continua levando
   **403** — a view apenas deixa de oferecer um botão que só terminaria em 403.

### Evidência HTTP — 3 perfis (115/115 checagens)

Script **versionado** em `qa/http-authz.ps1` (mesmo padrão do `qa/http-parte1.ps1`:
`-Porta`, `-ManterApp`, banco isolado em `%TEMP%` e conferência do hash do banco
versionado no início e no fim), banco **isolado** em `%TEMP%\qa-authz-<guid>.db`:
o `controleestoque.db` versionado **não é aberto** em nenhum momento — o script falha
se o hash mudar. Três sessões de cookie independentes, aplicação real na porta 5400
(o Dono nasce em `/Account/Setup`, o Operador é criado pela tela `/Usuarios`); a
execução versionada terminou com **115/115 checagens e 0 falhas** e o hash
`80D3F707…A8B8A` idêntico antes e depois:

| Perfil | Resultado |
|---|---|
| **Anônimo** | 10 rotas de domínio → **302** para `/Account/Login?ReturnUrl=…`; **nenhuma** respondeu 200. POST de sangria e de criação de usuário → **401** |
| **Dono** | `/Usuarios` e `/Usuarios/Criar` → 200; criou o Operador → 302; e-mail repetido → 200 com aviso em português (nada duplicado); `Papel=SuperAdmin` → 200 com aviso e **nada gravado**; suprimento, sangria, produto, vendedor e relatórios → 200 |
| **Operador** (criado pela tela nova) | PDV → 200; bipou o produto → 200; **fechou uma venda completa** → `{"success":true}`; suprimento → 200; **sangria → 403**; relatórios (5 rotas), fechamento (2), estoque (5), vendedores (2) e usuários (3) → **403**; `/Usuarios` por navegação → 302 para `/Account/AcessoNegado` |

Dois detalhes que **só** o teste manual pegou:

- Mensagens do Identity em inglês (`The Papel field is required.`) trocadas por
  texto em português; os acentos saem HTML-codificados pelo Razor
  (`J&#xE1; existe uma conta com este e-mail.`), o que está **correto** — o
  navegador renderiza "Já".
- Um Operador levava **302 → 200** (página de acesso negado) no lugar do 403 em
  toda chamada `fetch`: corrigido no item 5 acima.

---

## ✅ Corrigido em 30/09/2026 — Dinheiro em `INTEGER` de centavos (BUG-20)

| ID | O que foi corrigido | Commit | Teste |
|---|---|---|---|
| BUG-20 | As 7 colunas monetárias deixaram de ser `TEXT` e passaram a `INTEGER` de centavos, **com o dado já gravado convertido** pela migration | `fd30124` | `DadosQaTests.Decimal_NaoDeveSerPersistidoComoText` |

**As colunas:** `Produtos.Preco`, `Vendas.TotalVenda`, `Vendas.ValorRecebido`,
`Vendas.ValorTroco`, `VendaItens.PrecoUnitario`, `CaixaMovimentacoes.Valor`,
`FluxoCaixa.Valor`. O C# continua vendo `decimal` — o conversor mora só no modelo.

### Por que centavos, e não "mudar o tipo da coluna"

O SQLite não tem tipo rígido: uma coluna declarada `INTEGER` continua aceitando
qualquer coisa lá dentro, e `ALTER TABLE ... ALTER COLUMN` simplesmente não existe.
Quem decide a ordenação e a soma é o **valor armazenado**, e `INTEGER` é a única
afinidade numericamente exata do SQLite. O modelo passa a gravar **centavos**
(`9.99` → `999`) por um único `ValueConverter<decimal,long>`
(`Data/ConversorCentavos.cs`), registrado nas 7 propriedades em
`ApplicationDbContext`. Nada na cadeia passa por `float`/`double`: `* 100m` e
`/ 100m` são exatos. O conversor **recusa, sem arredondar**, valor com mais de 2
casas e valor acima de R$ 922 trilhões — arredondar em silêncio mudaria um preço
que o operador digitou.

### O SQL que o EF gera sozinho estava errado (medição, não suposição)

O `AlterColumn` do provider SQLite faz `CREATE TABLE` nova + `INSERT ... SELECT`
**cru**. Copiar o texto `'9.99'` para uma coluna `INTEGER` não multiplica nada por
100: o SQLite só aplica a afinidade numérica e grava `REAL 9.99` (`typeof='real'`).
Como a leitura divide por 100, o preço de R$ 9,99 passaria a aparecer como
**R$ 0,0999**. A migration `DecimalParaCentavos` traz o rebuild escrito à mão:
estrutura **idêntica** à do script do EF (mesmas tabelas `ef_temp_`, mesmas
colunas na mesma ordem, mesmas PK/FK, mesmos índices, mesma ordem de
`DROP`/`RENAME`) — muda apenas a lista de colunas do `INSERT`, que converte.

| Armadilha | Como foi medida | O que a migration faz |
|---|---|---|
| `DROP TABLE "Vendas"` dispara `ON DELETE CASCADE` e **apaga o histórico de vendas** (na matriz de teste, 3 itens viraram 0 — e o `foreign_key_check` dizia "ok" porque as linhas já não existiam) | replay do SQL com `PRAGMA foreign_keys` ligado | `PRAGMA foreign_keys = 0` executado **fora** da transação (`suppressTransaction: true`); dentro dela o pragma é ignorado (medido: continuava `1`). É a mesma família do BUG-08 |
| O `REAL` intermediário deixa resto binário: `'8.345' * 100 = 834.5000000000001`, que truncaria em `834` | `SELECT` direto no arquivo | `CAST(ROUND(CAST(... AS REAL) * 100) AS INTEGER)` |
| Texto vazio (`''`) ou com vírgula (`'9,99'`) digitado direto no banco | `INSERT` manual | `TRIM`/`REPLACE` tratam a vírgula; `NULLIF('')` + `NOT NULL` fazem o valor inválido **derrubar a migration** em vez de virar `R$ 0,00` |
| Rebuild do SQLite **não preserva os índices** de `VendaItens` | `sqlite_master` antes/depois | `IX_VendaItens_ProdutoId` e `IX_VendaItens_VendaId` recriados no fim do `Up` |

No fim do `Up`, um `pragma_foreign_key_check` embutido numa tabela com
`CHECK (violacoes = 0)` trava a aplicação se qualquer FK ficar violada.

### O teste foi reescrito — e por quê

A versão anterior abria o arquivo `20260220171624_Inicial_Nova.cs` e procurava
`table.Column<decimal>(type: "TEXT")` nele. Para ela passar, a única saída seria
**editar a migration inicial**, o que não é permitido: migration aplicada é o
registro histórico do que rodou no banco da loja, e editar o arquivo não muda
esquema nenhum — só faria o `Inicial_Nova` declarar `INTEGER` enquanto a conversão
continua multiplicando por 100 (o `999` de uma base nova viraria `99900`). Era um
teste que media o **texto de um registro histórico** em vez do comportamento.

O teste agora mede o **esquema efetivo**, em três níveis independentes:

1. **snapshot do modelo** — as 7 colunas são `Property<long>` e nenhuma delas é
   `Property<decimal>`; e o que sobrou como `decimal` é exatamente `Quantidade`
   (2 ocorrências). Isso também vale como trava de arquitetura: se nascer coluna
   nova de dinheiro como `decimal`/`TEXT`, a suíte quebra;
2. **arquivo SQLite real pelo caminho de produção** — sobe uma base **só com
   `Database.Migrate()`** (o mesmo método do `Program.cs`) até
   `20260928234611_ProdutoAtivo_ExclusaoLogica_VendaItemRestrict`, que é exatamente
   a migration em que o `controleestoque.exemplo.db` versionado está parado hoje;
   grava dinheiro como `TEXT` no formato legado, roda o `Migrate()` completo
   (Identity + `DecimalParaCentavos`) e cobra `integer=999`;
3. **o número cru dos dois lados do sintoma** — a mesma `ORDER BY` que na base
   legada devolve `"10.00,9.99"` (ordem lexicográfica: o relatório de preços
   invertido) passa a devolver `"999,1000,123456"`; e `1234.56m` gravado pelo EF
   sai `integer=123456` do arquivo.

O banco do teste é sempre um arquivo novo em `%TEMP%` (`Pooling=False`, apagado no
`finally`); o `controleestoque.db` da loja continua intocado, como atesta
`Suite_NaoDeveAbrirOBancoVersionado`.

### Impacto no banco versionado

`controleestoque.exemplo.db` **não mudou**: continua na migration 3 e passa pela
conversão no primeiro `Database.Migrate()` da instalação — exatamente o caminho que
o teste agora exercita. O `controleestoque.db` da loja recebe a migration na
primeira execução depois do update. Junto com a correção, o `.gitignore` deixou de
excepcionar os sidecars `-shm`/`-wal` do template: são artefato de runtime de quem
abre o arquivo e, como o template vai para `controleestoque.db` por `copy`, um
`-wal` desacompanhado ao lado de um `.db` copiado é vetor de corrupção.

---

## Já corrigido (Fase 1 — commit `1016148`)

Preço de tabela ignorando o POST, quantidade `<= 0` rejeitada, preço PorPeso direto
em R$/kg via `CalcularSubtotalItem`, troco recalculado no servidor, produto/vendedor inexistente
rejeitado, estoque insuficiente barrado, sangria gravada negativa e fechamento idempotente.
São os 32 testes verdes — ver `qa/falhas.csv` (`Status = corrigido`).


→ `SegurancaQaTests.NomeDeProduto_ComScript_DeveSerSanitizadoParaOInnerHTML`

### BUG-13 — Nenhum endpoint tem autenticação ✅ **corrigido** (`85d6797`)
Nenhum controller possuía `[Authorize]`: qualquer pessoa na mesma rede acessava
`/Caixa/Index`, `/Home/Excluir`, `/Caixa/RegistrarSangria` e
`/Caixa/ExcluirVendedor`. Hoje toda rota de domínio exige policy — ver
"Corrigido em 29/09/2026 — Autenticação e autorização (BUG-13)".

→ `SegurancaQaTests.Controllers_NaoDevemExporEndpointsSemAutorizacao`