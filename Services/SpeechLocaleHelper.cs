using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
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

        // Protege la resolución para que dos SpeakAsync casi simultáneos (p.ej.
        // "Fase" + texto principal) no disparen dos llamadas concurrentes a
        // GetLocalesAsync() que se pisen entre sí.
        private static readonly SemaphoreSlim _lock = new(1, 1);

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

            if (_cachedForLanguage == idiomaActual && _cachedLocale != null)
                return _cachedLocale;

            await _lock.WaitAsync();
            try
            {
                // Puede que otra llamada ya haya resuelto el idioma mientras
                // esperábamos el lock.
                if (_cachedForLanguage == idiomaActual && _cachedLocale != null)
                    return _cachedLocale;

                string codigo = BaseLanguageCode(idiomaActual);
                Locale? encontrado = null;
                int ultimoConteoLocales = 0;

                // Justo al entrar a una página con audio, el motor de TTS del
                // dispositivo puede no haber terminado de inicializarse todavía,
                // así que GetLocalesAsync() puede devolver una lista vacía o
                // incompleta en el primer intento. Si aceptábamos ese resultado
                // tal cual y lo cacheábamos como definitivo, un solo fallo de
                // arranque dejaba Locale=null cacheado para el resto de la
                // sesión — y con Locale=null el motor usa el idioma por defecto
                // del sistema operativo del teléfono, no el de la app (el bug
                // reportado: en un teléfono con el SO en alemán, el audio
                // seguía sonando en alemán aunque la app estuviera en inglés).
                for (int intento = 0; intento < 2 && encontrado == null; intento++)
                {
                    if (intento > 0)
                        await Task.Delay(400);

                    try
                    {
                        var locales = (await TextToSpeech.Default.GetLocalesAsync()).ToList();
                        ultimoConteoLocales = locales.Count;
                        encontrado = locales.FirstOrDefault(l =>
                            !string.IsNullOrEmpty(l.Language) &&
                            l.Language.StartsWith(codigo, StringComparison.OrdinalIgnoreCase));
                    }
                    catch
                    {
                        encontrado = null;
                    }
                }

                // Solo cacheamos un resultado encontrado. Si tras los reintentos
                // el motor sigue sin reportar el idioma pedido, no lo marcamos
                // como "resuelto": la próxima llamada (siguiente paso hablado)
                // lo volverá a intentar en vez de quedarse atascada en null.
                if (encontrado != null)
                {
                    _cachedLocale = encontrado;
                    _cachedForLanguage = idiomaActual;
                }

#if ANDROID
                // Diagnóstico temporal: si el bug reaparece, "adb logcat -s SCA_TTS"
                // dirá si el motor realmente no reporta el idioma pedido (lista sin
                // "en") o si lo reporta pero algo más está descartando el resultado.
                Android.Util.Log.Info("SCA_TTS",
                    $"idioma_app={idiomaActual} codigo={codigo} encontrado={(encontrado != null ? encontrado.Language : "NINGUNO")} locales_disponibles={ultimoConteoLocales}");
#endif

                return encontrado;
            }
            finally
            {
                _lock.Release();
            }
        }

        private static readonly Regex RegexPuntero = new(">+", RegexOptions.Compiled);
        private static readonly Regex RegexEspacios = new(@"\s+", RegexOptions.Compiled);

        // El contenido del Excel usa ">>" como viñeta/punto de lista dentro del
        // texto de una misma celda (p.ej. ">> Comprobar presión >> Comprobar
        // luces"), no como palabra a pronunciar — el motor de TTS lo leía
        // literalmente ("mayor que mayor que"). Se quita solo del texto que se
        // envía a hablar; el texto en pantalla no se toca.
        public static string LimpiarParaVoz(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return texto ?? string.Empty;

            string limpio = RegexPuntero.Replace(texto, " ");
            limpio = RegexEspacios.Replace(limpio, " ").Trim();
            return limpio;
        }

        // Corta justo después de ".", "!" o "?" (haya o no espacio detrás) y
        // también por saltos de línea — el "\s*" (no "\s+") es a propósito:
        // si el Excel no tiene espacio tras el punto, con "\s+" no cortaba
        // nada y toda la frase se hablaba de un tirón sin pausa alguna.
        private static readonly Regex RegexFinFrase = new(@"(?<=[.!?])\s*|\n+", RegexOptions.Compiled);

        // Habla texto largo frase por frase, con una pausa entre cada una, en
        // vez de una sola llamada continua a SpeakAsync. El motor de TTS no
        // expone control de velocidad (SpeechOptions solo tiene Locale/Pitch/
        // Volume), así que la forma de que no suene atropellado es meter un
        // respiro real entre frases en vez de dejar que el motor las encadene.
        //
        // Nota: en algunos motores de TTS de Android el callback "OnDone" de
        // una frase puede llegar justo antes de que termine de sonar del
        // todo (aunque el audio ya haya terminado de encolarse). El delay se
        // añade siempre DESPUÉS de que SpeakAsync haya devuelto el control,
        // nunca en paralelo, para dejarle a la reproducción todo el margen
        // posible antes de encolar la siguiente frase.
        public static async Task HablarConPausasAsync(string? texto, Locale? locale, CancellationToken token, int pausaEntreFrasesMs = 500)
        {
            string limpio = LimpiarParaVoz(texto);
            if (string.IsNullOrWhiteSpace(limpio)) return;

            var frases = RegexFinFrase.Split(limpio)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .ToList();

            if (frases.Count == 0) return;

            for (int i = 0; i < frases.Count; i++)
            {
                await TextToSpeech.Default.SpeakAsync(frases[i], new SpeechOptions { Locale = locale }, token);

                if (i < frases.Count - 1)
                    await Task.Delay(pausaEntreFrasesMs, token);
            }
        }
    }
}
