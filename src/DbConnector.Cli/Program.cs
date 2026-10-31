using DbConnector;

const string Ajuda = """
    db-connector-cli: proxy local de SSO (OIDC) para bancos PostgreSQL, sem janela.
    Com área de trabalho, prefira o aplicativo (db-connector).

    Uso: db-connector-cli [ambiente...] [--config <arquivo>]     (sem ambiente: o primeiro da configuração)

    Para cada ambiente, abre o navegador no login e fica escutando na porta local dele. No aplicativo SQL
    (DBeaver, DataGrip, psql…): PostgreSQL, host localhost, a porta do ambiente, usuário qualquer, a SENHA
    LOCAL que este terminal mostra (muda a cada vez que abre) e SEM SSL (o db-connector cifra até o gateway).
    Deixe o terminal aberto enquanto usa.

    Opções:
      --config <arquivo>   configuração (nome, emissor, ambientes); sem ela, a embutida no build
      --idp <alias>        vai direto ao provedor de identidade (kc_idp_hint do Keycloak, ex.: microsoft)
      --emissor <url>      outro emissor OIDC (realm)
      --gateway <host:porta> --client <id> --porta <n>   um ambiente avulso, no lugar dos da configuração
      --inseguro           aceita certificado inválido do gateway (só para teste local)
      --senha-local <s>    usa esta senha local em vez de gerar uma (só para teste)
    """;

Opcoes? opcoes;
try
{
    opcoes = Opcoes.Ler(args);
}
catch (Exception e) when (e is ArgumentException or FormatException)
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
if (opcoes.Ambientes.Count == 0)
{
    Console.Error.WriteLine("Nenhum ambiente configurado: passe --config <arquivo> ou use um build com a configuração embutida.");
    return 2;
}

using var parar = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; parar.Cancel(); };
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
await using var conector = new Conector(opcoes, http, new SenhaLocal(opcoes.SenhaLocal));
conector.Registro += (ambiente, mensagem) => Console.WriteLine($"[{ambiente.Nome}] {mensagem}");

Console.WriteLine($"{opcoes.NomeDoAplicativo} (Ctrl+C encerra).");
Console.WriteLine();
Console.WriteLine($"  Senha local desta sessão (no aplicativo SQL, campo senha): {conector.SenhaLocal.Valor}");
Console.WriteLine("  Ela muda a cada vez que o programa abre; não a compartilhe.");
Console.WriteLine();
foreach (var ambiente in opcoes.Ambientes)
{
    try
    {
        conector.Ligar(ambiente);
    }
    catch (InvalidOperationException e)
    {
        Console.Error.WriteLine($"[{ambiente.Nome}] {e.Message}");
        return 1;
    }
    // Um login por vez no navegador, na ordem dos ambientes.
    await conector.EntrarAsync(ambiente);
}

try
{
    await Task.Delay(Timeout.Infinite, parar.Token);
}
catch (OperationCanceledException)
{
}
return 0;
