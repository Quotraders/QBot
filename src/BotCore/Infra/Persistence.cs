#nullable enable
using System;
using System.IO;
using System.Text.Json;

namespace BotCore.Infra
{
    public static class Persistence
    {
        static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "state");
        
        // Cached JsonSerializerOptions for performance (CA1869 compliance)
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        public static void Save<T>(string name, T obj)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var path = Path.Combine(Dir, $"{name}.json");
                var json = JsonSerializer.Serialize(obj, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch (IOException)
            {
                // Silently fail on IO errors (disk full, access issues)
            }
            catch (UnauthorizedAccessException)
            {
                // Silently fail on permission issues
            }
            catch (JsonException)
            {
                // Silently fail on serialization errors
            }
        }

        public static T? Load<T>(string name)
        {
            try
            {
                var path = Path.Combine(Dir, $"{name}.json");
                if (!File.Exists(path)) return default;
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (IOException)
            {
                // Return default on IO errors (file not found, access issues)
                return default;
            }
            catch (UnauthorizedAccessException)
            {
                // Return default on permission issues
                return default;
            }
            catch (JsonException)
            {
                // Return default on deserialization errors (corrupt file)
                return default;
            }
        }
    }
}
