# FIX bancos (db-connector-app)

Aplicativo de desktop (Windows, Linux e macOS) para acessar os bancos PostgreSQL dos ambientes do FIX pelo **DBeaver**
(ou DataGrip, pgAdmin, psql...) com o **login da FIX no navegador**, sem senha de banco e sem port-forward.

```
DBeaver ──localhost:15432──▶ FIX bancos ──TLS (token do login)──▶ db-gateway (no cluster) ──▶ PostgreSQL
 (senha local)                │ login no navegador (authorization code + PKCE)
                              └─ renova o token sozinho; o token fica só na memória
```

O aplicativo não guarda segredo nenhum: quem decide o acesso é o login (Keycloak, com o SSO da Microsoft) e o grupo
`/bancos/<ambiente>` da pessoa, conferidos no servidor (`db-gateway`, repositório privado). Todo comando fica registrado
com o e-mail de quem o executou.

## Instalar

Baixe da [última release](../../releases/latest) o do seu sistema e confira o `SHA256SUMS`:

| Sistema | Arquivo |
| --- | --- |
| Windows 10/11 | `fix-bancos-windows-x64.exe` (assinado; não precisa instalar, é só abrir) |
| Linux | `fix-bancos-linux-x64` (`chmod +x`) · sem área de trabalho: `fix-bancos-cli-linux-x64` |
| macOS (Apple Silicon) | `fix-bancos-macos-arm64` (`chmod +x`; na primeira vez, botão direito → Abrir) |

## Desinstalar

O FIX bancos não tem instalador nem altera nada no sistema: para remover, feche a janela e apague o executável (e o
atalho, se você criou um). Não há configuração nem dados guardados em disco.

## Usar

1. Abra o FIX bancos. A janela mostra a **senha local** desta sessão e os ambientes.
2. Clique em **Conectar** no ambiente: o navegador abre no login. Depois do login, o cartão fica verde com o seu e-mail.
3. No DBeaver: conexão PostgreSQL (ou **Copiar URL** e colar em Nova conexão → URL):

   | Campo | Valor |
   | --- | --- |
   | Host e porta | `localhost` · dev `15432`, qa `15433`, prod `15434` |
   | Database | `fix_core`, com **Show all databases** marcado |
   | Usuário | qualquer um (quem vale é o login no navegador) |
   | Senha | a senha local da janela (muda a cada vez que o FIX bancos abre) |
   | SSL | desligado (o trecho local não sai da máquina; o FIX bancos cifra até o servidor) |

4. Deixe a janela aberta enquanto usa. Fechar encerra as conexões.

A sessão de login dura 30 minutos; depois, o navegador abre de novo na próxima conexão (com a sessão da Microsoft ainda
aberta no navegador, é imediato). Em dev e qa dá para ler e escrever dados; em prod, só ler.

Linha de comando (Linux sem área de trabalho, scripts): `fix-bancos-cli [dev] [qa] [prod]`, com as mesmas portas e a
senha local impressa no terminal. `fix-bancos-cli --ajuda` lista as opções.

## Desenvolvimento

```bash
dotnet build Fix.Bancos.slnx
dotnet test Fix.Bancos.slnx
dotnet run --project src/Fix.Bancos.Desktop            # janela; aceita "dev", "--idp microsoft", "--gateway host:porta --client id --inseguro"
dotnet publish src/Fix.Bancos.Desktop -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true
```

| Projeto | O que é |
| --- | --- |
| `src/Fix.Bancos` | Núcleo: ambientes, login no navegador (PKCE, retorno em `127.0.0.1:15431`), senha local, porta local, protocolo do PostgreSQL |
| `src/Fix.Bancos.Desktop` | A janela ([Avalonia](https://avaloniaui.net/)) |
| `src/Fix.Bancos.Cli` | A linha de comando |

Branches: `dev`, `qa` e `master`, como os outros repositórios da FIX-FGA. O CI compila e testa todo push e PR; na
`master`, publica os executáveis numa release.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

Only the Windows executable (`fix-bancos-windows-x64.exe`) built by this repository's GitHub Actions workflow
([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) from the `master` branch is signed. Every signing request is
approved manually.

Team roles:

- **Committers and reviewers:** members of the [FIX-FGA developers team](https://github.com/orgs/FIX-FGA/teams/developers).
  Changes reach `master` only through pull requests, reviewed by a team member.
- **Approvers:** the [FIX-FGA organization owners](https://github.com/orgs/FIX-FGA/people?query=role%3Aowner)
  ([@sfourm](https://github.com/sfourm), [@yuridsfga](https://github.com/yuridsfga),
  [@cristian272001](https://github.com/cristian272001)).

Multi-factor authentication is required for every team member, on GitHub and on SignPath.

**Privacy policy:** this program will not transfer any information to other networked systems unless specifically
requested by the user. It only talks to the systems the user chooses to connect to: the FIX identity provider (login in
the user's browser) and the FIX database gateway of the selected environment. It stores nothing on disk; login tokens
are kept in memory only while the program is open.

## Assinatura (para quem mantém)

O executável do Windows é assinado pela SignPath Foundation, de graça para projetos open source, para o Windows
(Controle Inteligente de Aplicativos e SmartScreen) abri-lo sem bloqueio. Configuração, uma vez:

1. Inscrição do projeto em [signpath.org](https://signpath.org/) (formulário de projetos open source).
2. Na SignPath, projeto `db-connector-app` com o repositório conectado como *trusted build system* (GitHub), a política de
   assinatura `release-signing` e a configuração de artefato `windows`:
   ```xml
   <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
     <zip-file>
       <pe-file path="fix-bancos-windows-x64.exe">
         <authenticode-sign />
       </pe-file>
     </zip-file>
   </artifact-configuration>
   ```
3. No GitHub: secret `SIGNPATH_API_TOKEN` (usuário de CI da SignPath com permissão de enviar pedidos) e variável
   `SIGNPATH_ORGANIZATION_ID`.

Sem o secret, o CI publica a release sem assinatura e avisa no log. Conferir no Windows:
`Get-AuthenticodeSignature fix-bancos-windows-x64.exe` (Status `Valid`).

## Licença

[MIT](LICENSE).
