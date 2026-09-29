# Relatório de QA — Sistema de Controle de Estoque

**Branch:** `fix/fase1-dinheiro` · **HEAD (código medido):** `702ae85` · **Última atualização:** 29/09/2026

> Esta suíte foi **recriada do zero** após a troca de máquina. As pastas `tests.QA/` e
> `qa/` existiam apenas no PC antigo e nunca foram versionadas. A partir de agora
> ambas estão no Git (ver `git log`).

## Como executar

```powershell
cd ControleEstoqueLoja\tests.QA
dotnet test
```

Cada teste cria um **banco SQLite isolado em `%TEMP%`** (`qa-teste-<guid>.db`) e o remove
ao final. **O `controleestoque.db` versionado nunca é aberto pela suíte.**

## Estado atual

| Métrica | Valor |
|---|---|
| Total de testes | **57** |
| ✅ Aprovados (comportamento correto) | **56** |
| ❌ Falhando (bug presente) | **1** |

Última atualização: **29/09/2026** — **autenticação e autorização (BUG-13)**:
`[Authorize]` + policies nomeadas em todas as rotas (`85d6797`) e a tela de
gestão de usuários só-Dono (`702ae85`). Antes destas duas, três correções de
segurança **sem** autenticação: XSS armazenado (BUG-12), vazamento de
`ex.Message` (BUG-15/16/17) e token anti-forgery em todos os POSTs (BUG-14).
Ficou vermelho apenas `Decimal_NaoDeveSerPersistidoComoText` (**BUG-20**).

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

`qa/http-parte1.ps1` valida os mesmos pontos por HTTP real (primeira execução em
28/09/2026): **todas as verificações passaram** e o `controleestoque.db` versionado
permaneceu com o hash `D38374DA...1609`.

---

## Severidade Crítica (pendentes)

_Nenhum. Os dois críticos de segurança foram corrigidos em 29/09/2026
(BUG-12 XSS e BUG-14 CSRF — ver "Corrigido em 29/09/2026" abaixo)._

---

## Severidade Alta (pendentes)

| ID | Descrição | Teste |
|---|---|---|
| BUG-20 | `decimal` mapeado como `TEXT` no SQLite — `ORDER BY` fica lexicográfico (`"10,00" < "9,00"`), quebrando relatórios | `DadosQaTests.Decimal_NaoDeveSerPersistidoComoText` |

---

## Severidade Média

_Nenhum pendente. BUG-15/16/17 (vazamento de `ex.Message`) foram corrigidos em
29/09/2026._

> **Filtro de data do Histórico:** `CaixaController.Historico` **aplica corretamente** o
> filtro no controller, então `LacunasQaTests.Historico_DeveRespeitarOFiltroDeData` **passa**.
> O sintoma percebido na tela está na camada de rota/view e não é coberto por teste
> unitário — ver `qa/http-parte1.ps1`.

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
policy**. Três entregas, nesta ordem:

| ID | O que foi feito | Commit | Teste |
|---|---|---|---|
| — | Identity, migration, cookie de 12 h e papéis Dono/Operador | `ae12be8` | — |
| **BUG-13** | `[Authorize]` + policy por rota; sangria separada do suprimento | `85d6797` | `SegurancaQaTests.Controllers_NaoDevemExporEndpointsSemAutorizacao` |
| — | Tela de gestão de usuários (só Dono) — sem ela não havia como criar Operador | `702ae85` | HTTP manual (abaixo) |

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

### Evidência HTTP — 3 perfis (66/66 checagens)

Script manual em `%TEMP%\manual-authz.ps1` (não versionado, por higiene — ver
`qa/http-parte1.ps1`), banco **isolado** em `%TEMP%\qa-estoque\manual-authz\`:
o `controleestoque.db` versionado **não foi aberto** em nenhum momento. Três
sessões de cookie independentes, aplicação real na porta 5400:

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

## Já corrigido (Fase 1 — commit `1016148`)

Preço de tabela ignorando o POST, quantidade `<= 0` rejeitada, granel `x10` via
`CalcularSubtotalItem`, troco recalculado no servidor, produto/vendedor inexistente
rejeitado, estoque insuficiente barrado, sangria gravada negativa e fechamento idempotente.
São os 32 testes verdes — ver `qa/falhas.csv` (`Status = corrigido`).


→ `SegurancaQaTests.NomeDeProduto_ComScript_DeveSerSanitizadoParaOInnerHTML`

### BUG-13 — Nenhum endpoint tem autenticação ✅ **corrigido** (`85d6797`)
Nenhum controller possuía `[Authorize]`: qualquer pessoa na mesma rede acessava
`/Caixa/Index`, `/Home/Excluir`, `/Caixa/RegistrarSangria` e
`/Caixa/ExcluirVendedor`. Hoje toda rota de domínio exige policy — ver
"Corrigido em 29/09/2026 — Autenticação e autorização (BUG-13)".

→ `SegurancaQaTests.Controllers_NaoDevemExporEndpointsSemAutorizacao`
