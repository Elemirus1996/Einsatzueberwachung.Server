using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Interfaces;

namespace Einsatzueberwachung.Domain.Services
{
    public class HomeNotesService : IHomeNotesService
    {
        private readonly string _filePath;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

        private List<HomeNoteEntry>? _cache;

        public HomeNotesService()
        {
            _filePath = Path.Combine(AppPathResolver.GetDataDirectory(), "home-notes.json");
        }

        public async Task<List<HomeNoteEntry>> GetNotesAsync()
        {
            if (_cache != null)
                return _cache;

            if (!File.Exists(_filePath))
            {
                _cache = [];
                return _cache;
            }

            try
            {
                var json = await File.ReadAllTextAsync(_filePath);
                _cache = JsonSerializer.Deserialize<List<HomeNoteEntry>>(json) ?? [];
            }
            catch
            {
                _cache = [];
            }

            return _cache;
        }

        public async Task AddNoteAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            var notes = await GetNotesAsync();
            var entry = new HomeNoteEntry(Guid.NewGuid().ToString(), text.Trim(), Ortszeit.Jetzt);

            await _lock.WaitAsync();
            try
            {
                notes.Insert(0, entry);
                await PersistAsync(notes);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task DeleteNoteAsync(string id)
        {
            var notes = await GetNotesAsync();

            await _lock.WaitAsync();
            try
            {
                notes.RemoveAll(n => n.Id == id);
                await PersistAsync(notes);
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task PersistAsync(List<HomeNoteEntry> notes)
        {
            await File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(notes, _jsonOpts));
        }
    }
}
