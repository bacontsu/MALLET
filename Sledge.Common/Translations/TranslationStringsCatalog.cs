﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Sledge.Common.Shell.Hooks;

namespace Sledge.Common.Translations
{
    [Export]
    public class TranslationStringsCatalog
    {
        [ImportMany("AutoTranslate")] private IEnumerable<Lazy<object>> _autoTranslate;
        [ImportMany] private IEnumerable<Lazy<IManualTranslate>> _manualTranslate;

        private List<string> _loaded;
        public Dictionary<string, TranslationStringsCollection> Languages { get; set; }

        public TranslationStringsCatalog()
        {
            Languages = new Dictionary<string, TranslationStringsCollection>();
            _loaded = new List<string>();
        }
        
        public void Initialise(string language)
        {
            foreach (var at in _autoTranslate)
            {
                Inject(language, at.Value);
            }

            if (Languages.ContainsKey(language))
            {
                var strings = Languages[language];
                foreach (var mt in _manualTranslate)
                {
                    mt.Value.Translate(strings);
                }
            }
        }

        private void Inject(string language, object target)
        {
            if (target == null) return;
            var ty = target.GetType();
            Load(ty);

            if (!Languages.ContainsKey(language)) return;
            var strings = Languages[language];

            var props = ty.GetProperties().Where(x => x.CanWrite);
            foreach (var prop in props)
            {
                var path = ty.FullName + '.' + prop.Name;
                if (strings.Strings.ContainsKey(path))
                {
                    prop.SetValue(target, strings.Strings[path]);
                }
            }
        }

        public void Load(Type type)
        {
            var loc = type.Assembly.Location ?? "";
            if (_loaded.Contains(loc)) return;

            _loaded.Add(loc);
            if (!File.Exists(loc)) return;

            var dir = Path.Combine(Path.GetDirectoryName(loc) ?? "", "Translations");
            if (!Directory.Exists(dir)) return;

            foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
            {
                LoadFile(file);
            }
        }

        private void LoadFile(string file)
        {
            var text = File.ReadAllText(file, Encoding.UTF8);
            var obj = JObject.Parse(text);

            var meta = obj["@Meta"];
            if (meta == null) return;

            var lang = Convert.ToString(meta["Language"]);
            if (String.IsNullOrWhiteSpace(lang)) return;

            var basePath = Convert.ToString(meta["Base"]) ?? "";
            if (!String.IsNullOrWhiteSpace(basePath)) basePath += ".";
            
            TranslationStringsCollection collection;
            if (!Languages.ContainsKey(lang))
            {
                collection = new TranslationStringsCollection();
                Languages[lang] = collection;
            }
            collection = Languages[lang];

            var strings = obj.Descendants()
                .OfType<JProperty>()
                .Where(x => x.Path[0] != '@')
                .Where(x => x.Value.Type == JTokenType.String);
            foreach (var st in strings)
            {
                collection.Strings[basePath + st.Path] = st.Value?.ToString();
            }
        }
    }
}