using System.Security.Cryptography;
using System.Text;

namespace Fix.Bancos;

/// <summary>
/// Senha que o DBeaver informa para conectar em localhost, gerada a cada execução do fix-bancos e mostrada só na janela
/// dele: sem ela, outro programa ou usuário desta máquina não usa o login de quem abriu o fix-bancos. Não substitui o
/// login do Keycloak (a identidade da auditoria continua vindo do token); só protege a porta local.
/// </summary>
public sealed class SenhaLocal
{
    // Sem caracteres que se confundem (0/O, 1/I/L).
    private const string Alfabeto = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private static readonly TimeSpan EsperaAposErro = TimeSpan.FromSeconds(2);
    private readonly byte[] _esperada;
    private readonly SemaphoreSlim _uma = new(1, 1);

    public SenhaLocal(string? valor = null)
    {
        Valor = valor ?? Gerar();
        _esperada = Encoding.UTF8.GetBytes(Normalizar(Valor));
    }

    public string Valor { get; }

    /// <summary>Confere sem vazar tempo; depois de um erro, segura a próxima tentativa (tentativa em massa fica lenta).</summary>
    public async Task<bool> ConfereAsync(string informada, CancellationToken ct)
    {
        await _uma.WaitAsync(ct);
        try
        {
            var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Normalizar(informada)), _esperada);
            if (!ok) await Task.Delay(EsperaAposErro, ct);
            return ok;
        }
        finally
        {
            _uma.Release();
        }
    }

    /// <summary>12 caracteres (~59 bits) em três grupos: XXXX-XXXX-XXXX.</summary>
    private static string Gerar()
    {
        var chars = new char[12];
        for (var i = 0; i < chars.Length; i++) chars[i] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
        var s = new string(chars);
        return $"{s[..4]}-{s[4..8]}-{s[8..]}";
    }

    // Aceita com ou sem os hífens e em minúsculas.
    private static string Normalizar(string valor) => valor.Replace("-", "").Trim().ToUpperInvariant();
}
