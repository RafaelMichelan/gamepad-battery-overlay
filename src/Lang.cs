// Idioma da interface: português (Brasil) ou inglês. Cada texto do programa é escrito nos dois idiomas
// no próprio lugar onde é usado:  Lang.T("texto em português", "text in English").
// A escolha fica no menu da bandeja ("Idioma / Language") e é salva no registro (valor "Language").
using System.Globalization;

public static class Lang
{
    /// <summary>true = inglês, false = português.</summary>
    public static bool En;

    public static string T(string pt, string en) { return En ? en : pt; }

    public static string Code { get { return En ? "en" : "pt"; } }

    /// <summary>Idioma inicial quando o usuário ainda não escolheu: português se o Windows estiver em português, senão inglês.</summary>
    public static string SystemDefault()
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" ? "pt" : "en";
    }

    /// <summary>Aplica um código salvo ("pt" ou "en"); qualquer outro valor usa o idioma do Windows.</summary>
    public static void Apply(string code)
    {
        if (code != "pt" && code != "en") code = SystemDefault();
        En = code == "en";
    }
}
