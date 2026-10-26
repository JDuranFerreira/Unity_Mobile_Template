using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;
using Template.Core.Localization;
using Template.Core.Services;

namespace Template.Services.Localization
{
    /// <summary>
    /// Unity Localization package implementation of ILocalizationService.
    /// Wraps the package so Core stays package-agnostic.
    /// </summary>
    [AddComponentMenu("Template/Localization/Localization Service")]
    public sealed class LocalizationService : MonoBehaviour, ILocalizationService
    {
        public const string ServiceName = "Localization";

        [Header("Settings")]
        [SerializeField] bool initializeOnStart = true;
        [SerializeField] string fallbackLocale = "en";

        public string Name => ServiceName;

        public event Action<string> OnLocaleChanged;

        public string CurrentLocale => LocalizationSettings.SelectedLocale?.Identifier.Code ?? fallbackLocale;

        public IReadOnlyList<string> AvailableLocales
        {
            get
            {
                if (LocalizationSettings.AvailableLocales.Locales == null)
                    return Array.Empty<string>();

                return LocalizationSettings.AvailableLocales.Locales
                    .Select(l => l.Identifier.Code)
                    .ToList();
            }
        }

        public async Task InitializeAsync(CancellationToken token = default)
        {
            // Wait for Unity Localization to initialize
            await LocalizationSettings.InitializationOperation.Task;

            // On first launch, try to detect device language
            if (PlayerPrefs.GetInt("LocaleInitialized", 0) == 0)
            {
                string deviceLang = Application.systemLanguage.ToString().ToLowerInvariant();
                // Map Unity's system language to our locale codes
                string mapped = MapSystemLanguage(deviceLang);
                if (AvailableLocales.Contains(mapped))
                {
                    await SetLocaleAsync(mapped, token);
                }
                else if (AvailableLocales.Contains(fallbackLocale))
                {
                    await SetLocaleAsync(fallbackLocale, token);
                }
                PlayerPrefs.SetInt("LocaleInitialized", 1);
                PlayerPrefs.Save();
            }
            // On subsequent launches, use saved locale
            else
            {
                string saved = PlayerPrefs.GetString("SelectedLocale", fallbackLocale);
                if (AvailableLocales.Contains(saved))
                {
                    await SetLocaleAsync(saved, token);
                }
            }
        }

        public string Get(string key)
        {
            var table = GetStringTable();
            if (table == null) return key;

            var entry = table.GetEntry(key);
            if (entry == null || entry.IsEmpty) return key;

            return entry.GetLocalizedString();
        }

        public string Get(string key, params object[] args)
        {
            string format = Get(key);
            if (format == key) return key; // missing key

            try
            {
                return string.Format(format, args);
            }
            catch
            {
                return format; // formatting failed, return raw
            }
        }

        public string GetPlural(string key, int count, params object[] args)
        {
            // Unity Localization handles plurals via StringTable with plural categories
            // We append the plural category to the key (e.g., "items_{zero}", "items_{one}")
            var table = GetStringTable();
            if (table == null) return key;

            string pluralKey = GetPluralKey(key, count);
            var entry = table.GetEntry(pluralKey);
            if (entry == null || entry.IsEmpty)
            {
                // Fallback to base key
                entry = table.GetEntry(key);
                if (entry == null || entry.IsEmpty) return key;
            }

            string format = entry.GetLocalizedString();
            try
            {
                return string.Format(format, args.Prepend(count).ToArray());
            }
            catch
            {
                return format;
            }
        }

        public async Task<bool> SetLocaleAsync(string localeCode, CancellationToken token = default)
        {
            var locale = AvailableLocales.FirstOrDefault(l => l.Equals(localeCode, StringComparison.OrdinalIgnoreCase));
            if (locale == null) return false;

            var localeObj = LocalizationSettings.AvailableLocales.Locales
                .FirstOrDefault(l => l.Identifier.Code.Equals(locale, StringComparison.OrdinalIgnoreCase));

            if (localeObj == null) return false;

            await LocalizationSettings.SelectedLocale.SetLocale(localeObj, token);

            PlayerPrefs.SetString("SelectedLocale", locale);
            PlayerPrefs.Save();

            OnLocaleChanged?.Invoke(locale);
            return true;
        }

        StringTable GetStringTable()
        {
            // Returns the first StringTableCollection's table for current locale
            // In a real game, you might have multiple tables (UI, Dialog, etc.)
            var collection = LocalizationSettings.StringDatabase.GetTableAsync("UI")?.Result;
            return collection?.GetTable(LocalizationSettings.SelectedLocale.Identifier);
        }

        static string MapSystemLanguage(string systemLang)
        {
            // Map common Unity system languages to locale codes
            return systemLang switch
            {
                "english" => "en",
                "german" => "de",
                "french" => "fr",
                "spanish" => "es",
                "italian" => "it",
                "japanese" => "ja",
                "korean" => "ko",
                "chinese" => "zh",
                "chinesesimplified" => "zh",
                "chinesetraditional" => "zh-TW",
                "russian" => "ru",
                "portuguese" => "pt",
                "polish" => "pl",
                "turkish" => "tr",
                "arabic" => "ar",
                _ => "en"
            };
        }

        static string GetPluralKey(string key, int count)
        {
            // Unity Localization uses ICU plural categories: zero, one, two, few, many, other
            // This is a simplified mapping; real implementation would use ICU rules per locale
            if (count == 0) return $"{key}_{{zero}}";
            if (count == 1) return $"{key}_{{one}}";
            if (count == 2) return $"{key}_{{two}}";
            return $"{key}_{{other}}";
        }
    }

    static class EnumerableExtensions
    {
        public static IEnumerable<T> Prepend<T>(this IEnumerable<T> source, T value)
        {
            yield return value;
            foreach (var item in source) yield return item;
        }
    }
}