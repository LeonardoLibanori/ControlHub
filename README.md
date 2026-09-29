# 🎛 Controle de Estoque e Caixa

Sistema web para gerenciamento de estoque e caixa de pequenas lojas, desenvolvido com **ASP.NET Core MVC** e **Entity Framework Core**.

## ­ƒîÉ Demo

Acesse o sistema em produ├º├úo: **[http://controle-estoque-loja.runasp.net](http://controle-estoque-loja.runasp.net)**

> ⚠️ O ambiente de demonstração é uma implantação anterior e **pode não refletir as últimas correções deste repositório** (branch `fix/fase1-dinheiro`). Para o comportamento atual, rode o projeto localmente como descrito abaixo.
>
> O banco de dados é criado automaticamente na primeira execução (via migrations). Este repositório traz um banco de exemplo com dados fictícios — copie `controleestoque.exemplo.db` para `controleestoque.db` para começar rápido.

---

## ­ƒÜÇ Tecnologias

- .NET 10 / ASP.NET Core MVC
- Entity Framework Core 10 (Code-First)
- SQLite
- Razor Views com Runtime Compilation
- SweetAlert2

---

## 🚪 Rodando pela primeira vez

O repositório **não versiona o banco de dados real** (`controleestoque.db`): ele é ignorado
pelo Git, porque contém dados de operação e só faz sentido na máquina da loja.

Para começar rápido, copie o banco de exemplo:

```powershell
# dentro da pasta ControleEstoqueLoja
copy controleestoque.exemplo.db controleestoque.db
```

Depois é só subir a aplicação:

```powershell
dotnet run
```

Abra <http://localhost:5236> (Caixa) ou `/Home/Index` (Estoque).

> Se preferir começar do zero, basta **apagar** `controleestoque.db`: na próxima execução o
> `Program.cs` roda `Database.Migrate()` e recria todas as tabelas automaticamente.

### Banco de exemplo

O arquivo `controleestoque.exemplo.db` já vem com o schema atualizado (migrations aplicadas) e
alguns **dados fictícios**, para navegar pela aplicação sem precisar cadastrar nada:

- **Produtos:** Café Torrado e Moído (Prateleira, R$ 18,90), Achocolatado em Pó
  (Prateleira, R$ 7,50) e Café em Grão (Granel, R$ 45,00/kg)
- **Vendedor:** Vendedor Exemplo — cupom `EXEMPLO1`
- **Vendas:** 2 vendas de exemplo (R$ 37,80 e R$ 675,00)

> O cupom é normalizado em **maiúsculas** (`EXEMPLO1`). O preenchimento no PDV não diferencia
> maiúsculas de minúsculas, então `exemplo1` funciona igual.

---

## ­ƒôª M├│dulo de Estoque

- Listagem de todos os produtos com busca por nome
- Cadastro de produto com os campos: nome, tipo, quantidade, pre├ºo e c├│digo de barras
- Edi├º├úo de produto existente
- Exclus├úo de produto
- Movimenta├º├úo de estoque (entrada e sa├¡da de quantidade)
- Produtos organizados por tipo: **Granel**, **Prateleira**, **Geladeira** e **Freezer**

---

## ­ƒÆ░ M├│dulo de Caixa

### Realizando uma Venda
1. Acesse o **Caixa** (`Alt + C`)
2. Busque o produto pelo nome ou leia o c├│digo de barras
3. Adicione os itens ao carrinho
4. Informe o m├®todo de pagamento e o valor recebido
5. Finalize a venda ÔÇö o sistema calcula o troco automaticamente
6. O cupom fiscal ├® gerado para impress├úo

> ÔÜá´©Å **Para registrar uma venda ├® obrigat├│rio cadastrar ao menos um vendedor antes.** O c├│digo do vendedor (cupom) ├® vinculado ├á venda.

### Cadastrando um Vendedor
1. Pressione `Alt + M` para abrir o Menu Administrativo
2. Selecione a op├º├úo **7 - Vendedores**
3. Clique em **Cadastrar Novo**
4. Preencha o nome e o c├│digo (cupom) do vendedor
5. Clique em **Salvar Registro**

---

## ­ƒÅº Menu Administrativo (Alt + M)

| Tecla | Fun├º├úo |
|---|---|
| `1` | ­ƒÆ© Sangria ÔÇö retirada de dinheiro do caixa |
| `2` | ­ƒôÑ Suprimento ÔÇö entrada de dinheiro no caixa |
| `3` | ­ƒÅº Status do Caixa |
| `4` | ­ƒöÆ Fechamento do Caixa |
| `5` | ­ƒôè Relat├│rio do Caixa |
| `6` | ­ƒû¿´©Å Reimprimir ├║ltima venda |
| `7` | ­ƒæÑ Gerenciar Vendedores |

---

## Ôî¿´©Å Atalhos do Sistema

| Atalho | Fun├º├úo |
|---|---|
| `Alt + M` | Abre o Menu Administrativo |
| `Alt + C` | Navega para o Caixa |
| `Alt + H` | Navega para o Hist├│rico de Vendas |
| `Alt + E` | Navega para o Controle de Estoque |
| `ESC` | Abre/fecha o painel de ajuda com todos os atalhos |

---

## ­ƒôï Hist├│rico de Vendas

- Listagem de todas as vendas realizadas
- Exibe data, total, m├®todo de pagamento e vendedor
- Reimpress├úo de cupom de qualquer venda anterior

---

## ­ƒÅù´©Å Arquitetura

Padr├úo MVC com separa├º├úo em `Controllers`, `Models`, `Views` e `Data` (DbContext). Inje├º├úo de depend├¬ncia nativa do ASP.NET Core.
---

## 🧪 Desenvolvimento

```powershell
dotnet build                       # compilar
dotnet run                         # subir a aplicação
cd tests.QA; dotnet test           # suíte de testes de caracterização (55 testes)
```

### Migrations

O projeto usa um **manifesto local de ferramentas** (`dotnet-tools.json`) com o `dotnet-ef`,
então o comando é `dotnet ef` (sem prefixo `tool`):

```powershell
dotnet ef migrations add NomeDaMigration
dotnet ef database update
dotnet ef migrations list
```

> Nunca rode `dotnet ef` contra o `controleestoque.db` real sem backup — as migrations
> alteram o schema do banco.

### Suíte de testes

`tests.QA/` é uma suíte xUnit de **caracterização**: os testes descrevem o comportamento
esperado e ficam **vermelhos enquanto o bug existir**, virando verdes quando corrigido.
Cada teste cria um SQLite descartável em `%TEMP%` — o banco real nunca é tocado.

Os achados e a severidade de cada bug estão em [`qa/RELATORIO-QA.md`](qa/RELATORIO-QA.md)
e [`qa/falhas.csv`](qa/falhas.csv).

### Estrutura

```
ControleEstoqueLoja/
├── Controllers/          HomeController (estoque) e CaixaController (PDV)
├── Models/               Produto, Venda, VendaItem, Vendedor, CaixaMovimentacao...
├── Data/                 ApplicationDbContext (configura FKs e exclusão lógica)
├── Migrations/           migrations do EF Core
├── Views/                Razor
├── wwwroot/              Bootstrap, jQuery e admin-caixa.js
├── tests.QA/             suíte xUnit de caracterização
├── qa/                   relatório de QA, falhas.csv e script HTTP
├── controleestoque.exemplo.db   banco de exemplo (versionado)
└── controleestoque.db           banco real (ignorado pelo Git)
```

---
> Desenvolvido com C# e .NET 10 ­ƒöÀ

