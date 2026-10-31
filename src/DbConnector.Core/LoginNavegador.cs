using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace DbConnector;

/// <summary>Tokens de um login; <see cref="SessaoAte"/> é até quando o Keycloak renova sem abrir o navegador.</summary>
public sealed record Tokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiraEm, DateTimeOffset? SessaoAte = null)
{
    public string Email { get; init; } = "";
}

/// <summary>
/// Login no Keycloak pelo navegador (authorization code + PKCE, RFC 8252): o retorno vem para 127.0.0.1 nesta máquina,
/// então o login pode passar pela Microsoft (ou o que o Keycloak usar) com MFA. O client é público: não há segredo aqui.
/// Os tokens ficam só na memória deste processo.
/// </summary>
public sealed class LoginNavegador(Opcoes opcoes, HttpClient http, Registrar registrar)
{
    private static readonly SemaphoreSlim Um = new(1, 1); // a porta de retorno é uma só
    private static readonly TimeSpan Limite = TimeSpan.FromMinutes(5);

    public async Task<Tokens> EntrarAsync(Ambiente ambiente, CancellationToken ct)
    {
        await Um.WaitAsync(ct);
        try
        {
            var verificador = Base64Url(RandomNumberGenerator.GetBytes(32));
            var desafio = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verificador)));
            var estado = Base64Url(RandomNumberGenerator.GetBytes(16));
            var retorno = $"http://127.0.0.1:{opcoes.PortaRetorno}/callback";

            var url = $"{opcoes.Emissor}/protocol/openid-connect/auth?response_type=code&client_id={Uri.EscapeDataString(ambiente.ClientId)}" +
                      $"&redirect_uri={Uri.EscapeDataString(retorno)}&scope=openid%20email%20profile&state={estado}" +
                      $"&code_challenge={desafio}&code_challenge_method=S256" +
                      (opcoes.IdpHint is { } idp ? $"&kc_idp_hint={Uri.EscapeDataString(idp)}" : "");

            using var escuta = new TcpListener(IPAddress.Loopback, opcoes.PortaRetorno);
            escuta.Start();
            registrar(ambiente, "abrindo o navegador para o login…");
            AbrirNavegador(ambiente, url);
            registrar(ambiente, $"se o navegador não abrir, copie: {url}");

            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(Limite);
            var codigo = await EsperarRetornoAsync(escuta, estado, limite.Token);
            return await TrocarAsync(ambiente, new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ambiente.ClientId,
                ["code"] = codigo,
                ["redirect_uri"] = retorno,
                ["code_verifier"] = verificador,
            }, ct);
        }
        finally
        {
            Um.Release();
        }
    }

    /// <summary>Token novo pelo refresh token (sem navegador); null se a sessão do Keycloak acabou.</summary>
    public async Task<Tokens?> RenovarAsync(Ambiente ambiente, string refreshToken, CancellationToken ct)
    {
        try
        {
            return await TrocarAsync(ambiente, new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = ambiente.ClientId,
                ["refresh_token"] = refreshToken,
            }, ct);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<Tokens> TrocarAsync(Ambiente ambiente, Dictionary<string, string> formulario, CancellationToken ct)
    {
        using var resposta = await http.PostAsync($"{opcoes.Emissor}/protocol/openid-connect/token", new FormUrlEncodedContent(formulario), ct);
        var json = await resposta.Content.ReadAsStringAsync(ct);
        if (!resposta.IsSuccessStatusCode) throw new InvalidOperationException($"Keycloak recusou ({(int)resposta.StatusCode}): {json}");
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        var access = raiz.GetProperty("access_token").GetString()!;
        var refresh = raiz.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        var agora = DateTimeOffset.UtcNow;
        var expira = agora.AddSeconds(raiz.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 60);
        DateTimeOffset? sessao = raiz.TryGetProperty("refresh_expires_in", out var s) && s.GetInt32() > 0 ? agora.AddSeconds(s.GetInt32()) : null;
        return new Tokens(access, refresh, expira, sessao) { Email = EmailDo(access) };
    }

    private async Task<string> EsperarRetornoAsync(TcpListener escuta, string estado, CancellationToken ct)
    {
        while (true)
        {
            using var cliente = await escuta.AcceptTcpClientAsync(ct);
            var stream = cliente.GetStream();
            var leitor = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var linha = await leitor.ReadLineAsync(ct) ?? "";
            var caminho = linha.Split(' ') is [_, var p, ..] ? p : "";
            if (!caminho.StartsWith("/callback", StringComparison.Ordinal))
            {
                await ResponderAsync(stream, 404, "não encontrado", ct);
                continue;
            }
            var query = HttpUtility.ParseQueryString(new Uri("http://127.0.0.1" + caminho).Query);
            if (query["state"] != estado)
            {
                await ResponderAsync(stream, 400, $"Login de outra tentativa: volte ao {opcoes.NomeDoAplicativo} e tente de novo.", ct);
                continue;
            }
            if (query["error"] is { } erro)
            {
                await ResponderAsync(stream, 400, $"O Keycloak recusou o login: {query["error_description"] ?? erro}", ct);
                throw new InvalidOperationException($"login recusado: {query["error_description"] ?? erro}");
            }
            await ResponderAsync(stream, 200, "Login feito. Pode fechar esta aba e voltar ao DBeaver.", ct);
            return query["code"] ?? throw new InvalidOperationException("retorno sem código");
        }
    }

    private async Task ResponderAsync(Stream stream, int status, string texto, CancellationToken ct)
    {
        var corpo = Encoding.UTF8.GetBytes(
            $"<!doctype html><meta charset=utf-8><title>{WebUtility.HtmlEncode(opcoes.NomeDoAplicativo)}</title><body style=\"font-family:system-ui;margin:3rem\"><h2>{WebUtility.HtmlEncode(opcoes.NomeDoAplicativo)}</h2><p>{WebUtility.HtmlEncode(texto)}</p>");
        var cabecalho = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {corpo.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(cabecalho, ct);
        await stream.WriteAsync(corpo, ct);
    }

    private void AbrirNavegador(Ambiente ambiente, string url)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS()) Process.Start("open", url);
            else Process.Start("xdg-open", url);
        }
        catch (Exception e)
        {
            registrar(ambiente, $"não consegui abrir o navegador ({e.Message})");
        }
    }

    /// <summary>E-mail do token, só para mostrar quem está conectado (quem confere a assinatura é o gateway).</summary>
    internal static string EmailDo(string token)
    {
        try
        {
            var partes = token.Split('.');
            var payload = partes[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
