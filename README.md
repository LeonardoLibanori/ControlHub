# Controle de Estoque — Loja

Sistema web de controle de estoque e frente de caixa (PDV), em **ASP.NET Core MVC / .NET 10**
com **Entity Framework Core + SQLite**.

| | |
|---|---|
| Framework | .NET 10 (`net10.0`) |
| Banco | SQLite via EF Core 10 |
| Views | Razor + Bootstrap 5 + SweetAlert2 |

---

## Rodando pela primeira vez

O repositório **não versiona o banco de dados real** (`controleestoque.db`): ele é ignorado
pelo Git, porque contém dados de operação e só faz sentido na máquina da loja.

Para começar, basta **copiar o banco de exemplo**:

```powershell
# dentro da pasta ControleEstoqueLoja
copy controleestoque.exemplo.db controleestoque.db
```

Pronto. O arquivo de exemplo já vem com o schema atualizado (migrations aplicadas) e
alguns **dados fictícios** para você navegar pela aplicação sem precisar cadastrar nada:

- **Produtos:** Café Torrado e Moído (Prateleira, R$ 18,90), Achocolatado em Pó
  (Prateleira, R$ 7,50) e Café em Grão (Granel, R$ 45,00/kg)
- **Vendedor:** Vendedor Exemplo — cupom `EXEMPLO1`
- **Vendas:** 2 vendas de exemplo (R$ 37,80 e R$ 675,00)

Depois é só subir a aplicação:

```powershell
dotnet run
```

Abra <http://localhost:5236> (Caixa) ou `/Home/Index` (Estoque).

> Se preferir começar do zero, basta **apagar** `controleestoque.db`: na próxima execução o
> `Program.cs` roda `Database.Migrate()` e recria todas as tabelas automaticamente.

### Sobre o cupom de exemplo

O cupom é normalizado em **maiúsculas** (`EXEMPLO1`). O preenchimento no PDV não diferencia
maiúsculas de minúsculas, então `exemplo1` funciona igual.

---

## Desenvolvimento

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

---

## Estrutura

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
