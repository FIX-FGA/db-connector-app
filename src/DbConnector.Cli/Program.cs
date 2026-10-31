using DbConnector;

const string Ajuda = """
    db-connector-cli: proxy local de SSO (OIDC) para bancos PostgreSQL, sem janela.
    Com área de trabalho, prefira o aplicativo (db-connector), onde as conexões são cadastradas.

    Uso: db-connector-cli [conexão...] [--arquivo <conexoes.json>]     (sem conexão: a primeira)

    Usa as conexões cadastradas no aplicativo (ou as de um arquivo exportado). Para cada conexão, abre o navegador no
    login e fica escutando na porta local dela. No aplicativo SQL (DBeaver, DataGrip, psql…): PostgreSQL, host
    localhost, a porta da conexão, usuário qualquer, a SENHA LOCAL que este terminal mostra (muda a cada vez que abre)
    e SEM SSL (o db-connector cifra até o gateway). Deixe o terminal aberto enquanto usa.

    Opções:
      --arquivo <arquivo>  conexões de um arquivo (o que o aplicativo exporta), no lugar das cadastradas
      --idp <alias>        vai direto ao provedor de identidade (kc_idp_hint do Keycloak, ex.: microsoft)
      --gateway <host:porta> --client <id> --emissor <url> [--porta <n>]   uma conexão avulsa
      --inseguro           aceita certificado inválido do gateway (só para teste local)
      --senha-local <s>    usa esta senha local em vez de gerar uma (só para teste)
    """;

Opcoes? opcoes;
try
{
    opcoes = Opcoes.Ler(args);
}
catch (Exception e) when (e is ArgumentException or FormatException or IOException)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine(Ajuda);
    return 2;
}
if (opcoes is null)
{
    Console.WriteLine(Ajuda);
    return 0;
}
if (opcoes.Escolhidas.Count == 0)
{
    Console.Error.WriteLine("Nenhuma conexão cadastrada: cadastre no aplicativo (db-connector) ou passe --arquivo <conexoes.json>.");
    return 2;
}

using var parar = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; parar.Cancel(); };
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
await using var conector = new Conector(opcoes, http, new SenhaLocal(opcoes.SenhaLocal));
conector.Registro += (conexao, mensagem) => Console.WriteLine($"[{conexao.Nome}] {mensagem}");

Console.WriteLine($"{Opcoes.NomeDoAplicativo} (Ctrl+C encerra).");
Console.WriteLine();
Console.WriteLine($"  Senha local desta sessão (no aplicativo SQL, campo senha): {conector.SenhaLocal.Valor}");
Console.WriteLine("  Ela muda a cada vez que o programa abre; não a compartilhe.");
Console.WriteLine();
foreach (var conexao in opcoes.Escolhidas)
{
    try
    {
        conector.Ligar(conexao);
    }
    catch (InvalidOperationException e)
    {
        Console.Error.WriteLine($"[{conexao.Nome}] {e.Message}");
        return 1;
    }
    // Um login por vez no navegador, na ordem das conexões.
    await conector.EntrarAsync(conexao);
}

try
{
    await Task.Delay(Timeout.Infinite, parar.Token);
}
catch (OperationCanceledException)
{
}
return 0;
