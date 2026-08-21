using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Media;

namespace Aplicacion_SCA.Services
{
    // Resuelve el Locale de TTS a partir del idioma elegido EN LA APP
    // (LocalizationService.CurrentLanguage) — nunca del idioma del sistema
    // operativo del dispositivo. Sin esto, TextToSpeech.SpeakAsync(texto, null, token)
    // deja que el motor de voz del teléfono elija el idioma por defecto del
    // dispositivo, así que un texto en inglés se lee con voz alemana (u otra)
    // si ese es el idioma del sistema, aunque la app esté en inglés.
    public static class SpeechLocaleHelper
    {
        private static Locale? _cachedLocale;
        private static LocalizationService.Language? _cachedForLanguage;

        private static string BaseLanguageCode(LocalizationService.Language language) => language switch
        {
            LocalizationService.Language.English => "en",
            LocalizationService.Language.French => "fr",
            LocalizationService.Language.German => "de",
            _ => "es"
        };

        public static async Task<Locale?> GetLocaleAsync()
        {
            var idiomaActual = LocalizationService.CurrentLanguage;

            if (_cachedForLanguage == idiomaActual)
                return _cachedLocale;

            string codigo = BaseLanguageCode(idiomaActual);

            try
            {
                var locales = await TextToSpeech.Default.GetLocalesAsync();
                _cachedLocale = locales.FirstOrDefault(l =>
                    !string.IsNullOrEmpty(l.Language) &&
                    l.Language.StartsWith(codigo, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                _cachedLocale = null;
            }

            _cachedForLanguage = idiomaActual;
            return _cachedLocale;
        }
    }
}
