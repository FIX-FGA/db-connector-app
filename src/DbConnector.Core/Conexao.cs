using System.Text.Json.Serialization;

namespace DbConnector;

/// <summary>
/// Uma conexão cadastrada no aplicativo: o emissor OIDC e o client do login, o gateway PostgreSQL (TLS, recebe o access
/// token como senha) e a porta local onde o aplicativo SQL conecta. Não tem segredo: a senha é o login no navegador.
/// </summary>
public sealed record Conexao(
    string Nome,
    string Emissor,
    string ClientId,
    string Host,
    [property: JsonPropertyName("porta")] int PortaGateway = 5432,
    int PortaLocal = 15432,
    string? BancoPadrao = null,
    [property: JsonPropertyName("somenteLeitura")] bool SoLeitura = false,
    string? IdpHint = null)
{
    public string Permissao => SoLeitura ? "só leitura" : "leitura e escrita";

    public string EmissorNormalizado => Emissor.Trim().TrimEnd('/');

    /// <summary>URL para colar no aplicativo SQL (no DBeaver: Nova conexão → PostgreSQL → URL).</summary>
    public string UrlJdbc => $"jdbc:postgresql://localhost:{PortaLocal}/{(string.IsNullOrWhiteSpace(BancoPadrao) ? "postgres" : BancoPadrao)}?sslmode=disable";

    /// <summary>O que está errado nesta conexão (vazio: válida).</summary>
    public IReadOnlyList<string> Problemas()
    {
        var problemas = new List<string>();
        if (string.IsNullOrWhiteSpace(Nome)) problemas.Add("dê um nome à conexão");
        if (!Uri.TryCreate(EmissorNormalizado, UriKind.Absolute, out var emissor) || emissor.Scheme is not ("https" or "http"))
            problemas.Add("o emissor precisa ser a URL do realm OIDC (ex.: https://sso.exemplo.com/realms/interno)");
        if (string.IsNullOrWhiteSpace(ClientId)) problemas.Add("informe o client ID");
        if (string.IsNullOrWhiteSpace(Host)) problemas.Add("informe o host do gateway");
        if (PortaGateway is <= 0 or > 65535) problemas.Add("a porta do gateway vai de 1 a 65535");
        if (PortaLocal is <= 1024 or > 65535) problemas.Add("a porta local vai de 1025 a 65535");
        if (PortaLocal == Opcoes.PortaRetornoPadrao) problemas.Add($"a porta local {Opcoes.PortaRetornoPadrao} é a do retorno do login: use outra");
        return problemas;
    }
}
