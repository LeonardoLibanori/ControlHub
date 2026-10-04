# 🔄 Sincronização entre máquinas (casa ↔ trabalho)

> **Decisão de 02/10/2026:** o banco `controleestoque.db` **passou a ser versionado no Git**.
> Antes ele era ignorado, e cada máquina tinha o seu — por isso os logins eram diferentes
> e sempre aparecia "arquivo órfão" ou "faltando/atualizado". Agora é **um banco só**.

---

## ⚡ O dia a dia (2 comandos)

Sempre nessa ordem, **com a aplicação fechada**:

| Situação | Comando |
|---|---|
| **Vai trocar de máquina** (sair de casa, ir pro trabalho) | `.\sincronizar.ps1 -Enviar` |
| **Chegou na outra máquina** | `.\sincronizar.ps1 -Receber` |
| **Dúvida / antes de decidir** | `.\sincronizar.ps1 -Status` |

Na primeira execução da máquina, se reclamar de política de script:

```powershell
powershell -ExecutionPolicy Bypass -File .\sincronizar.ps1 -Status
```

Depois disso é só `-Enviar` e `-Receber`.

---

## 📋 O que cada comando faz

### `-Receber` (chegou na máquina)

1. **Backup** do `.db` local em `.\backups-banco\`
2. `git pull` (com `--ff-only`, que recusa merge外面枝)
3. Aplica as **migrations pendentes** e diz quais eram
4. Confirma que ficou em dia

### `-Enviar` (vai trocar de máquina)

1. **Backup** do `.db` local
2. `git add -A` (código **e** banco)
3. Mostra **a lista de arquivos** antes de comitar
4. `commit` com mensagem automática + `push`

### `-Status` (diagnóstico)

Não altera nada. Mostra: se as máquinas estão iguais, quantos commits faltam em cada
lado, o hash/tamanho/data do banco e se ele está versionado.

---

## 🚨 Por que a ordem importa

O banco é **um arquivo binário com um dono por vez**. Se você trabalhar nas duas máquinas
**sem** sincronizar no meio, as duas gravam no mesmo `.db` e o Git se recusa a combinar —
ele não sabe qual dos dois é o certo. Isso é proposital: uma combinação errada produziria
um banco corrompido em silêncio, com vendas desaparecidas.

> **Regra de ouro:** só troque de máquina **depois** de rodar `-Enviar`.
> Quem chega, roda `-Receber` **antes** de abrir o sistema.

---

## 💥 Conflito de banco (as duas mexeram sem sincronizar)

O `-Receber` vai recusar o pull e mostrar isto:

```
[FALHA] o pull nao pode ser feito automaticamente.
  As maquinas mexeram no MESMO .db e ele e binario: o Git recusa combinar.
```

**Não use `--force`.** O que fazer, nesta ordem:

1. **Não abra o sistema** — ele ainda está com os dados da sua máquina.
2. O `-Receber` já salvou um backup automático em `.\backups-banco\`. Guarde-o.
3. Decida qual banco vale mais. Na prática, quase sempre é o **da máquina que você usou
   por último** (é o que tem as vendas mais recentes). Copie-o para cima do que veio do Git:

   ```powershell
   # 1. o banco atual, que é o que você quer manter
   Copy-Item .\controleestoque.db "$env:TEMP\meu-banco.db"
   # 2. busca a versão do Git (pode falhar, tudo bem)
   git checkout origin/main -- controleestoque.db
   # 3. devolve o seu
   Copy-Item "$env:TEMP\meu-banco.db" .\controleestoque.db -Force
   # 4. reenvia
   .\sincronizar.ps1 -Enviar
   ```

4. Se os dois bancos tiverem vendas que você quer manter, o caminho é exportar os dois
   em planilha (botão **Exportar Excel** nas telas de Estoque e Histórico — abre direto
   no Excel, com a linha de totais), unifying num só e então começar de um banco limpo.
   A rota antiga de CSV continua existindo (`/Home/ExportarEstoqueCsv` e
   `/Caixa/ExportarHistoricoCsv`) caso o arquivo precise abrir em outro programa.

---

## 🔒 Segurança — leia antes do primeiro push

O repositório é **público** no GitHub. Versionar o banco significa que os **hashes de
senha** das contas e os **dados de venda** ficam no histórico do Git.

- Hashes de senha não são a senha, mas **não são reversíveis** — ainda assim, é dado
  sensível.
- O histórico do Git guarda versões **antigas** mesmo depois de você apagar algo.

**Recomendação:** torne o repositório privado antes do primeiro `-Enviar`.

```powershell
# pela pagina do GitHub: Settings > General > Danger Zone > Change visibility
# ou pela CLI:
gh repo edit LeonardoLibanori/ControleEstoqueLoja --visibility private --accept-visibility-change-consequences
```

---

## 🗂️ O que é versionado e o que não é

| Arquivo | Vai pro Git? | Por quê |
|---|---|---|
| `controleestoque.db` | ✅ **SIM** | é o banco da loja — precisa ser o mesmo nas duas máquinas |
| `controleestoque.exemplo.db` | ✅ SIM (já era) | template de banco zerado |
| `controleestoque.db-wal` / `-shm` | ❌ não | artefato temporário de quem abriu o app; não fazem sentido longe da máquina |
| `backups-banco\*.db` | ❌ não | backups locais, ficam só na máquina |
| código, testes, migrations | ✅ sim | como sempre |

O `.gitattributes` marca `*.db` como **binário**. Sem isso o Git tentaria juntar dois
bancos byte a byte e could producing um arquivo corrompido sem avisar.

---

## 🆘 Problemas comuns

| Sintoma | O que fazer |
|---|---|
| `A aplicacao esta RODANDO` | Feche o sistema. Se travou: `Stop-Process -Name ControleEstoqueLoja -Force` |
| `address already in use` na 5236 | Mesma coisa: o processo antigo não saiu |
| `não foi possível localizar um projeto` | Você está na pasta **pai**. Use `dotnet run --project ControleEstoqueLoja` |
| `MSB3021` (não consegue copiar o .exe) | O app está aberto. Feche e rode de novo |
| Erro de autenticação no push | `git credential-manager github login` |
| `Your branch is behind` | Rode `.\sincronizar.ps1 -Receber` |
| `Your branch is ahead` | Rode `.\sincronizar.ps1 -Enviar` |

---

## 🔁 Migrando uma máquina que já tem o banco local

A máquina que já roda o sistema hoje é a **fonte da verdade**. Depois de atualizar o
código (`git pull`), basta:

```powershell
git pull origin main
.\sincronizar.ps1 -Enviar     # publica o banco local como o "oficial"
```

A outra máquina, ao rodar `-Receber`, passa a ter exatamente o mesmo.
