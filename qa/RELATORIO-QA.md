# Relatório de QA — Sistema de Controle de Estoque

**Branch:** `fix/fase1-dinheiro` · **HEAD:** `1016148` · **Data da recriação:** 28/09/2026

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
| Total de testes | **55** |
| ✅ Aprovados (comportamento correto) | **49** |
| ❌ Falhando (bug presente) | **6** |

Última atualização: **28/09/2026** — terceira etapa da Fase 2 (CASCADE + Migrate),
a primeira que envolve **migration**. Juntando as três etapas são **15 bugs
corrigidos** (Caixa, Produtos/Vendedores/Lacunas, e agora BUG-08 e BUG-19).
Ficou vermelho: 1 Dados (decimal como TEXT) + 5 Segurança = **6 casos**.

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

### BUG-12 — XSS armazenado no PDV
`HomeController.Novo` + `wwwroot/js/admin-caixa.js` (linha 438)

O nome do produto é gravado sem sanitizar e interpolado direto no `innerHTML`:

```javascript
btn.innerHTML = `<strong>${nomeOriginal}</strong> <small ...>R$ ${div.dataset.preco}</small>`;
```

Um produto chamado `<img src=x onerror=alert(1)>` executa JavaScript no navegador de quem
busca no PDV. A view Razor escapa (`@Model.Nome`); o JavaScript não.

---

## Severidade Alta (pendentes)

| ID | Descrição | Teste |
|---|---|---|
| BUG-09 | Cadastro aceita código de barras duplicado | `ProdutosQaTests.Novo_DeveRejeitarCodigoDeBarrasDuplicado` |
| BUG-10 | Cadastro aceita preço negativo | `ProdutosQaTests.Novo_DeveRejeitarPrecoNegativo` |
| BUG-11 | Cadastro aceita estoque negativo | `ProdutosQaTests.Novo_DeveRejeitarEstoqueNegativo` |
| BUG-14 | POSTs sem `[ValidateAntiForgeryToken]` (CSRF) | `SegurancaQaTests.PostActions_NaoDevemFaltarAntiForgery` |
| BUG-20 | `decimal` mapeado como `TEXT` no SQLite — `ORDER BY` fica lexicográfico (`"10,00" < "9,00"`), quebrando relatórios | `DadosQaTests.Decimal_NaoDeveSerPersistidoComoText` |

---

## Severidade Média

| ID | Descrição | Teste |
|---|---|---|
| BUG-15 | `ListarVendedores` vaza `ex.Message` ao cliente | `SegurancaQaTests.ListarVendedores_NaoDeveVazarMensagemDeExcecao` |
| BUG-16 | `ObterDadosFechamento` vaza `ex.Message` ao cliente | `SegurancaQaTests.ObterDadosFechamento_NaoDeveVazarMensagemDeExcecao` |
| BUG-17 | `RegistrarMovimentacao` vaza `FormatException` ao cliente | `SegurancaQaTests.RegistrarMovimentacao_NaoDeveVazarMensagemDeExcecao` |

> **Filtro de data do Histórico:** `CaixaController.Historico` **aplica corretamente** o
> filtro no controller, então `LacunasQaTests.Historico_DeveRespeitarOFiltroDeData` **passa**.
> O sintoma percebido na tela está na camada de rota/view e não é coberto por teste
> unitário — ver `qa/http-parte1.ps1`.

---

## Já corrigido (Fase 1 — commit `1016148`)

Preço de tabela ignorando o POST, quantidade `<= 0` rejeitada, granel `x10` via
`CalcularSubtotalItem`, troco recalculado no servidor, produto/vendedor inexistente
rejeitado, estoque insuficiente barrado, sangria gravada negativa e fechamento idempotente.
São os 32 testes verdes — ver `qa/falhas.csv` (`Status = corrigido`).


→ `SegurancaQaTests.NomeDeProduto_ComScript_DeveSerSanitizadoParaOInnerHTML`

### BUG-13 — Nenhum endpoint tem autenticação
Nenhum controller possui `[Authorize]`. Qualquer pessoa na mesma rede acessa
`/Caixa/Index`, `/Home/Excluir`, `/Caixa/RegistrarMovimentacao`, `/Caixa/ExcluirVendedor`.

→ `SegurancaQaTests.Controllers_NaoDevemExporEndpointsSemAutorizacao`
