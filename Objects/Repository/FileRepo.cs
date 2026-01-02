using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Utils;
using System.Text.RegularExpressions;


namespace NetworkMonitor.Objects.Repository
{
    public interface IFileRepo
    {
        string PrefixPath { get; }
        Task ShutdownAsync();

        void CheckFileExists(string filename, ILogger logger);
        void CheckFileExistsWithCreateObject<T>(string filename, T obj, ILogger logger) where T : class;
        void CheckFileExistsWithCreateJsonZObject<T>(string filename, T obj, ILogger logger) where T : class;
        void CheckFileExistsWithCreateStringJsonZObject<T>(string filename, T obj, ILogger logger) where T : class;

        bool IsFileExists(string filename);

        Task WriteFileStringAsync(string key, string jsonStr);
        Task WriteFileBytesAsync(string key, byte[] bytes);
        Task<string> ReadFileStringAsync(string key);
        Task<byte[]> ReadFileBytesAsync(string key);

        T? GetStateJson<T>(string key, string statestore) where T : class;
        T? GetStateJson<T>(string key) where T : class;
        T? GetStateJsonZ<T>(string key, string statestore) where T : class;
        T? GetStateJsonZ<T>(string key) where T : class;
        T? GetStateStringJsonZ<T>(string key, string statestore) where T : class;
        T? GetStateStringJsonZ<T>(string key) where T : class;

        Task<T?> GetStateJsonAsync<T>(string key, string statestore) where T : class;
        Task<T?> GetStateJsonAsync<T>(string key) where T : class;
        Task<T?> GetStateJsonZAsync<T>(string key, string statestore) where T : class;
        Task<T?> GetStateJsonZAsync<T>(string key) where T : class;
        Task<T?> GetStateStringJsonZAsync<T>(string key, string statestore) where T : class;
        Task<T?> GetStateStringJsonZAsync<T>(string key) where T : class;

        void SaveStateJson<T>(string key, T obj, string statestore) where T : class;
        void SaveStateJson<T>(string key, T obj) where T : class;
        void SaveStateString(string key, string obj, string statestore);
        void SaveStateString(string key, string obj);
        void SaveStateBytes(string key, byte[] bytes, string statestore);
        void SaveStateBytes(string key, byte[] bytes);
        byte[] SaveStateJsonZ<T>(string key, T obj, string statestore) where T : class;
        byte[] SaveStateJsonZ<T>(string key, T obj) where T : class;
        string SaveStateStringJsonZ<T>(string key, T obj, string statestore) where T : class;
        string SaveStateStringJsonZ<T>(string key, T obj) where T : class;

        Task SaveStateJsonAsync<T>(string key, T obj, string statestore) where T : class;
        Task SaveStateJsonAsync<T>(string key, T obj) where T : class;
        Task SaveStateStringAsync(string key, string obj, string statestore);
        Task SaveStateStringAsync(string key, string obj);
        Task SaveStateBytesAsync(string key, byte[] bytes, string statestore);
        Task SaveStateBytesAsync(string key, byte[] bytes);
        Task<byte[]> SaveStateJsonZAsync<T>(string key, T obj, string statestore) where T : class;
        Task<byte[]> SaveStateJsonZAsync<T>(string key, T obj) where T : class;
        Task<string> SaveStateStringJsonZAsync<T>(string key, T obj, string statestore) where T : class;
        Task<string> SaveStateStringJsonZAsync<T>(string key, T obj) where T : class;

        // ---------- Try* (sync) ----------
        bool TryGetStateJson<T>(string key, out T? value) where T : class;
        bool TryGetStateJson<T>(string key, string statestore, out T? value) where T : class;
        bool TryGetStateJsonZ<T>(string key, out T? value) where T : class;
        bool TryGetStateJsonZ<T>(string key, string statestore, out T? value) where T : class;
        bool TryGetStateStringJsonZ<T>(string key, out T? value) where T : class;
        bool TryGetStateStringJsonZ<T>(string key, string statestore, out T? value) where T : class;

        // ---------- Try* (async) ----------
        Task<(bool ok, T? value)> TryGetStateJsonAsync<T>(string key) where T : class;
        Task<(bool ok, T? value)> TryGetStateJsonAsync<T>(string key, string statestore) where T : class;
        Task<(bool ok, T? value)> TryGetStateJsonZAsync<T>(string key) where T : class;
        Task<(bool ok, T? value)> TryGetStateJsonZAsync<T>(string key, string statestore) where T : class;
        Task<(bool ok, T? value)> TryGetStateStringJsonZAsync<T>(string key) where T : class;
        Task<(bool ok, T? value)> TryGetStateStringJsonZAsync<T>(string key, string statestore) where T : class;
    }

    public class FileRepo : IFileRepo
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
        private readonly bool _isRunningOnMauiAndroid;
        private readonly string _prefixPath = "";
        public string PrefixPath => _prefixPath;

        public FileRepo() : this(false, "") { }

        private static readonly Regex AtomicTmpPattern =
            new Regex(@"\.[0-9a-f]{32}\.tmp$", RegexOptions.IgnoreCase | RegexOptions.Compiled);


        public FileRepo(bool isRunningOnMauiAndroid, string prefixPath = "")
        {
            _isRunningOnMauiAndroid = isRunningOnMauiAndroid;
            _prefixPath = prefixPath ?? "";
        }

        // ---- helpers (atomic write core) -----------------------------------

        public static int CleanupTempFiles(string directory, TimeSpan? olderThan = null, bool recursive = false)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return 0;

            var cutoffUtc = DateTime.UtcNow - (olderThan ?? TimeSpan.FromHours(1));
            var search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var deleted = 0;

            foreach (var path in Directory.EnumerateFiles(directory, "*.tmp", search))
            {
                var name = Path.GetFileName(path);
                if (!AtomicTmpPattern.IsMatch(name))
                    continue;

                DateTime lastWriteUtc;
                try { lastWriteUtc = File.GetLastWriteTimeUtc(path); }
                catch { continue; }

                if (lastWriteUtc > cutoffUtc)
                    continue; // too fresh; could still be in use

                try { File.Delete(path); deleted++; }
                catch { /* best-effort: ignore in-use/permission errors */ }
            }

            return deleted;
        }

        private string GetFilePath(string key)
            => Path.Combine(_prefixPath, key);

        private static void EnsureDirectoryFor(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
        }

        private string NormalizePathForLock(string key)
            => Path.GetFullPath(GetFilePath(key));

        private SemaphoreSlim GetLock(string normalizedPath)
            => _fileLocks.GetOrAdd(normalizedPath, _ => new SemaphoreSlim(1, 1));

        private static void AtomicWriteBytes(string path, byte[] data)
        {
            EnsureDirectoryFor(path);
            var dir = Path.GetDirectoryName(path)!;
            var tmp = Path.Combine(dir, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

            try
            {
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.Write(data, 0, data.Length);
                    fs.Flush(true);
                }

                try { File.Replace(tmp, path, null); }
                catch (PlatformNotSupportedException) { File.Move(tmp, path, overwrite: true); }
                catch (FileNotFoundException) { File.Move(tmp, path, overwrite: true); }
            }
            finally
            {
                // If Replace/Move succeeded, tmp no longer exists; if it failed, try to clean up.
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
            }
        }

        private static void AtomicWriteText(string path, string text)
        {
            EnsureDirectoryFor(path);
            var dir = Path.GetDirectoryName(path)!;
            var tmp = Path.Combine(dir, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

            try
            {
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var sw = new StreamWriter(fs))
                {
                    sw.Write(text);
                    sw.Flush();
                    fs.Flush(true);
                }

                try { File.Replace(tmp, path, null); }
                catch (PlatformNotSupportedException) { File.Move(tmp, path, overwrite: true); }
                catch (FileNotFoundException) { File.Move(tmp, path, overwrite: true); }
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
            }
        }

        private static async Task AtomicWriteBytesAsync(string path, byte[] data)
        {
            EnsureDirectoryFor(path);
            var dir = Path.GetDirectoryName(path)!;
            var tmp = Path.Combine(dir, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

            try
            {
                await using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await fs.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
                    await fs.FlushAsync().ConfigureAwait(false);
                }

                try { File.Replace(tmp, path, null); }
                catch (PlatformNotSupportedException) { File.Move(tmp, path, overwrite: true); }
                catch (FileNotFoundException) { File.Move(tmp, path, overwrite: true); }
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
            }
        }

        private static async Task AtomicWriteTextAsync(string path, string text)
        {
            EnsureDirectoryFor(path);
            var dir = Path.GetDirectoryName(path)!;
            var tmp = Path.Combine(dir, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

            try
            {
                await using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await using (var sw = new StreamWriter(fs))
                {
                    await sw.WriteAsync(text).ConfigureAwait(false);
                    await sw.FlushAsync().ConfigureAwait(false);
                    await fs.FlushAsync().ConfigureAwait(false);
                }

                try { File.Replace(tmp, path, null); }
                catch (PlatformNotSupportedException) { File.Move(tmp, path, overwrite: true); }
                catch (FileNotFoundException) { File.Move(tmp, path, overwrite: true); }
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
            }
        }

        // ---- lifecycle ------------------------------------------------------

        public async Task ShutdownAsync()
        {
            foreach (var sem in _fileLocks.Values)
            {
                await sem.WaitAsync().ConfigureAwait(false);
                sem.Release();
            }
        }

        // ---- existence / create-if-missing ---------------------------------

        public void CheckFileExists(string filename, ILogger logger)
        {
            filename = GetFilePath(filename);
            try
            {
                EnsureDirectoryFor(filename);
                using var fs = new FileStream(filename, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                if (fs.Length == 0)
                    logger?.LogWarning("Warning: File {File} was created empty", filename);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error checking/creating file {File}", filename);
            }
        }

        public void CheckFileExistsWithCreateObject<T>(string filename, T obj, ILogger logger) where T : class
        {
            filename = GetFilePath(filename);
            EnsureDirectoryFor(filename);
            var json = JsonUtils.WriteJsonObjectToString(obj);

            try
            {
                using var fs = new FileStream(
                    filename,
                    FileMode.CreateNew,        // only succeeds if file didn't exist
                    FileAccess.Write,
                    FileShare.None             // nobody else can open while we write
                );
                using var sw = new StreamWriter(fs);
                sw.Write(json);
                sw.Flush();
                fs.Flush(true);
                logger?.LogWarning("Created file {File} with initial {Type}", filename, typeof(T));
            }
            catch (IOException)
            {
                // Someone else created it first — do nothing.
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error creating file {File}", filename);
            }
        }

        public void CheckFileExistsWithCreateJsonZObject<T>(string filename, T obj, ILogger logger) where T : class
        {
            filename = GetFilePath(filename);
            EnsureDirectoryFor(filename);
            var jsonZ = StringCompressor.CompressToBytes(JsonUtils.WriteJsonObjectToString(obj));

            try
            {
                using var fs = new FileStream(filename, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                fs.Write(jsonZ, 0, jsonZ.Length);
                fs.Flush(true);
                logger?.LogWarning("Created file {File} (compressed) with initial {Type}", filename, typeof(T));
            }
            catch (IOException)
            {
                // Lost the race; leave existing content alone.
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error creating compressed file {File}", filename);
            }
        }

        public void CheckFileExistsWithCreateStringJsonZObject<T>(string filename, T obj, ILogger logger) where T : class
        {
            filename = GetFilePath(filename);
            EnsureDirectoryFor(filename);
            var jsonZ = StringCompressor.Compress(JsonUtils.WriteJsonObjectToString(obj));

            try
            {
                using var fs = new FileStream(filename, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var sw = new StreamWriter(fs);
                sw.Write(jsonZ);
                sw.Flush();
                fs.Flush(true);
                logger?.LogWarning("Created file {File} (compressed string) with initial {Type}", filename, typeof(T));
            }
            catch (IOException)
            {
                // Lost the race; leave existing content alone.
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error creating compressed-string file {File}", filename);
            }
        }
        public async Task CheckFileExistsWithCreateObjectAsync<T>(string filename, T obj, ILogger logger) where T : class
        {
            filename = GetFilePath(filename);
            EnsureDirectoryFor(filename);
            var json = JsonUtils.WriteJsonObjectToString(obj);

            try
            {
                await using var fs = new FileStream(
                    filename,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    useAsync: true
                );
                using var sw = new StreamWriter(fs);
                await sw.WriteAsync(json).ConfigureAwait(false);
                await sw.FlushAsync().ConfigureAwait(false);
                await fs.FlushAsync().ConfigureAwait(false);
                logger?.LogWarning("Created file {File} with initial {Type}", filename, typeof(T));
            }
            catch (IOException)
            {
                // Someone else won; noop.
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error creating file {File}", filename);
            }
        }

        public async Task CheckFileExistsWithCreateJsonZObjectAsync<T>(string filename, T obj, ILogger logger) where T : class
        {
            filename = GetFilePath(filename);
            EnsureDirectoryFor(filename);
            var jsonZ = StringCompressor.CompressToBytes(JsonUtils.WriteJsonObjectToString(obj));

            try
            {
                await using var fs = new FileStream(filename, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
                await fs.WriteAsync(jsonZ, 0, jsonZ.Length).ConfigureAwait(false);
                await fs.FlushAsync().ConfigureAwait(false);
                logger?.LogWarning("Created file {File} (compressed) with initial {Type}", filename, typeof(T));
            }
            catch (IOException) { }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error creating compressed file {File}", filename);
            }
        }

        public async Task CheckFileExistsWithCreateStringJsonZObjectAsync<T>(string filename, T obj, ILogger logger) where T : class
        {
            filename = GetFilePath(filename);
            EnsureDirectoryFor(filename);
            var jsonZ = StringCompressor.Compress(JsonUtils.WriteJsonObjectToString(obj));

            try
            {
                await using var fs = new FileStream(filename, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
                using var sw = new StreamWriter(fs);
                await sw.WriteAsync(jsonZ).ConfigureAwait(false);
                await sw.FlushAsync().ConfigureAwait(false);
                await fs.FlushAsync().ConfigureAwait(false);
                logger?.LogWarning("Created file {File} (compressed string) with initial {Type}", filename, typeof(T));
            }
            catch (IOException) { }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error creating compressed-string file {File}", filename);
            }
        }


        // ---- exists ---------------------------------------------------------

        public bool IsFileExists(string filename)
            => File.Exists(GetFilePath(filename));

        // ---- low-level IO (string/bytes) with atomic writers ----------------

        private void WriteFileString(string key, string text)
        {
            var path = GetFilePath(key);
            // keep prior newline behavior for SaveStateString/Json
            AtomicWriteText(path, text.EndsWith(Environment.NewLine) ? text : text + Environment.NewLine);
        }

        private void WriteFileBytes(string key, byte[] bytes)
        {
            var path = GetFilePath(key);
            AtomicWriteBytes(path, bytes);
        }

        private string ReadFileString(string key)
            => File.ReadAllText(GetFilePath(key));

        private byte[] ReadFileBytes(string key)
            => File.ReadAllBytes(GetFilePath(key));

        public async Task WriteFileStringAsync(string key, string text)
        {
            var norm = NormalizePathForLock(key);
            var sem = GetLock(norm);
            await sem.WaitAsync().ConfigureAwait(false);
            try
            {
                var path = GetFilePath(key);
                var toWrite = text.EndsWith(Environment.NewLine) ? text : text + Environment.NewLine;
                await AtomicWriteTextAsync(path, toWrite).ConfigureAwait(false);
            }
            finally
            {
                sem.Release();
            }
        }

        public async Task WriteFileBytesAsync(string key, byte[] bytes)
        {
            var norm = NormalizePathForLock(key);
            var sem = GetLock(norm);
            await sem.WaitAsync().ConfigureAwait(false);
            try
            {
                var path = GetFilePath(key);
                await AtomicWriteBytesAsync(path, bytes).ConfigureAwait(false);
            }
            finally
            {
                sem.Release();
            }
        }

        public async Task<string> ReadFileStringAsync(string key)
        {
            var norm = NormalizePathForLock(key);
            var sem = GetLock(norm);
            await sem.WaitAsync().ConfigureAwait(false);
            try
            {
                var path = GetFilePath(key);
                return await File.ReadAllTextAsync(path).ConfigureAwait(false);
            }
            finally
            {
                sem.Release();
            }
        }

        public async Task<byte[]> ReadFileBytesAsync(string key)
        {
            var norm = NormalizePathForLock(key);
            var sem = GetLock(norm);
            await sem.WaitAsync().ConfigureAwait(false);
            try
            {
                var path = GetFilePath(key);
                return await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            }
            finally
            {
                sem.Release();
            }
        }

        // ---- state helpers (JSON/plain/compressed) --------------------------

        public T? GetStateJson<T>(string key, string statestore) where T : class
            => JsonUtils.GetJsonObjectFromString<T>(ReadFileString(Path.Combine(statestore, key)));

        public T? GetStateJson<T>(string key) where T : class
            => JsonUtils.GetJsonObjectFromString<T>(ReadFileString(key));

        public T? GetStateJsonZ<T>(string key, string statestore) where T : class
        {
            var bytes = ReadFileBytes(Path.Combine(statestore, key));
            var json = StringCompressor.Decompress(bytes);
            return JsonUtils.GetJsonObjectFromString<T>(json);
        }

        public T? GetStateJsonZ<T>(string key) where T : class
        {
            var bytes = ReadFileBytes(key);
            var json = StringCompressor.Decompress(bytes);
            return JsonUtils.GetJsonObjectFromString<T>(json);
        }

        public T? GetStateStringJsonZ<T>(string key, string statestore) where T : class
        {
            var str = ReadFileString(Path.Combine(statestore, key));
            var json = StringCompressor.Decompress(str);
            return JsonUtils.GetJsonObjectFromString<T>(json);
        }

        public T? GetStateStringJsonZ<T>(string key) where T : class
        {
            var str = ReadFileString(key);
            var json = StringCompressor.Decompress(str);
            return JsonUtils.GetJsonObjectFromString<T>(json);
        }

        public async Task<T?> GetStateJsonAsync<T>(string key, string statestore) where T : class
            => JsonUtils.GetJsonObjectFromString<T>(await ReadFileStringAsync(Path.Combine(statestore, key)).ConfigureAwait(false));

        public async Task<T?> GetStateJsonAsync<T>(string key) where T : class
            => JsonUtils.GetJsonObjectFromString<T>(await ReadFileStringAsync(key).ConfigureAwait(false));

        public async Task<T?> GetStateJsonZAsync<T>(string key, string statestore) where T : class
        {
            var bytes = await ReadFileBytesAsync(Path.Combine(statestore, key)).ConfigureAwait(false);
            var json = StringCompressor.Decompress(bytes);
            return JsonUtils.GetJsonObjectFromString<T>(json);
        }

        public async Task<T?> GetStateJsonZAsync<T>(string key) where T : class
        {
            var bytes = await ReadFileBytesAsync(key).ConfigureAwait(false);
            var json = StringCompressor.Decompress(bytes);
            return JsonUtils.GetJsonObjectFromString<T>(json);
        }

        public async Task<T?> GetStateStringJsonZAsync<T>(string key, string statestore) where T : class
        {
            var str = await ReadFileStringAsync(Path.Combine(statestore, key)).ConfigureAwait(false);
            var json = StringCompressor.Decompress(str);
            return JsonUtils.GetJsonObjectFromString<T>(json);
        }

        public async Task<T?> GetStateStringJsonZAsync<T>(string key) where T : class
        {
            var str = await ReadFileStringAsync(key).ConfigureAwait(false);
            var json = StringCompressor.Decompress(str);
            return JsonUtils.GetJsonObjectFromString<T>(json);
        }

        public void SaveStateJson<T>(string key, T obj, string statestore) where T : class
            => WriteFileString(Path.Combine(statestore, key), JsonUtils.WriteJsonObjectToString(obj));

        public void SaveStateJson<T>(string key, T obj) where T : class
            => WriteFileString(key, JsonUtils.WriteJsonObjectToString(obj));

        public void SaveStateString(string key, string obj, string statestore)
            => WriteFileString(Path.Combine(statestore, key), obj);

        public void SaveStateString(string key, string obj)
            => WriteFileString(key, obj);

        public void SaveStateBytes(string key, byte[] bytes, string statestore)
            => WriteFileBytes(Path.Combine(statestore, key), bytes);

        public void SaveStateBytes(string key, byte[] bytes)
            => WriteFileBytes(key, bytes);

        public byte[] SaveStateJsonZ<T>(string key, T obj, string statestore) where T : class
        {
            var json = JsonUtils.WriteJsonObjectToString(obj);
            var jsonZ = StringCompressor.CompressToBytes(json);
            WriteFileBytes(Path.Combine(statestore, key), jsonZ);
            return jsonZ;
        }

        public byte[] SaveStateJsonZ<T>(string key, T obj) where T : class
        {
            var json = JsonUtils.WriteJsonObjectToString(obj);
            var jsonZ = StringCompressor.CompressToBytes(json);
            WriteFileBytes(key, jsonZ);
            return jsonZ;
        }

        public string SaveStateStringJsonZ<T>(string key, T obj, string statestore) where T : class
        {
            var json = JsonUtils.WriteJsonObjectToString(obj);
            var jsonZ = StringCompressor.Compress(json);
            WriteFileString(Path.Combine(statestore, key), jsonZ);
            return jsonZ;
        }

        public string SaveStateStringJsonZ<T>(string key, T obj) where T : class
        {
            var json = JsonUtils.WriteJsonObjectToString(obj);
            var jsonZ = StringCompressor.Compress(json);
            WriteFileString(key, jsonZ);
            return jsonZ;
        }

        public async Task SaveStateJsonAsync<T>(string key, T obj, string statestore) where T : class
            => await WriteFileStringAsync(Path.Combine(statestore, key), JsonUtils.WriteJsonObjectToString(obj)).ConfigureAwait(false);

        public async Task SaveStateJsonAsync<T>(string key, T obj) where T : class
            => await WriteFileStringAsync(key, JsonUtils.WriteJsonObjectToString(obj)).ConfigureAwait(false);

        public async Task SaveStateStringAsync(string key, string obj, string statestore)
            => await WriteFileStringAsync(Path.Combine(statestore, key), obj).ConfigureAwait(false);

        public async Task SaveStateStringAsync(string key, string obj)
            => await WriteFileStringAsync(key, obj).ConfigureAwait(false);

        public async Task SaveStateBytesAsync(string key, byte[] bytes, string statestore)
            => await WriteFileBytesAsync(Path.Combine(statestore, key), bytes).ConfigureAwait(false);

        public async Task SaveStateBytesAsync(string key, byte[] bytes)
            => await WriteFileBytesAsync(key, bytes).ConfigureAwait(false);

        public async Task<byte[]> SaveStateJsonZAsync<T>(string key, T obj, string statestore) where T : class
        {
            var json = JsonUtils.WriteJsonObjectToString(obj);
            var jsonZ = StringCompressor.CompressToBytes(json);
            await WriteFileBytesAsync(Path.Combine(statestore, key), jsonZ).ConfigureAwait(false);
            return jsonZ;
        }

        public async Task<byte[]> SaveStateJsonZAsync<T>(string key, T obj) where T : class
        {
            var json = JsonUtils.WriteJsonObjectToString(obj);
            var jsonZ = StringCompressor.CompressToBytes(json);
            await WriteFileBytesAsync(key, jsonZ).ConfigureAwait(false);
            return jsonZ;
        }

        public async Task<string> SaveStateStringJsonZAsync<T>(string key, T obj, string statestore) where T : class
        {
            var json = JsonUtils.WriteJsonObjectToString(obj);
            var jsonZ = StringCompressor.Compress(json);
            await WriteFileStringAsync(Path.Combine(statestore, key), jsonZ).ConfigureAwait(false);
            return jsonZ;
        }

        public async Task<string> SaveStateStringJsonZAsync<T>(string key, T obj) where T : class
        {
            var json = JsonUtils.WriteJsonObjectToString(obj);
            var jsonZ = StringCompressor.Compress(json);
            await WriteFileStringAsync(key, jsonZ).ConfigureAwait(false);
            return jsonZ;
        }

        // ---------- Try* (sync) ----------
        public bool TryGetStateJson<T>(string key, out T? value) where T : class
        {
            try { value = GetStateJson<T>(key); return value is not null; }
            catch (FileNotFoundException) { value = null; return false; }
            catch (DirectoryNotFoundException) { value = null; return false; }
            catch (IOException) { value = null; return false; }
            catch (UnauthorizedAccessException) { value = null; return false; }
        }

        public bool TryGetStateJson<T>(string key, string statestore, out T? value) where T : class
        {
            try { value = GetStateJson<T>(key, statestore); return value is not null; }
            catch (FileNotFoundException) { value = null; return false; }
            catch (DirectoryNotFoundException) { value = null; return false; }
            catch (IOException) { value = null; return false; }
            catch (UnauthorizedAccessException) { value = null; return false; }
        }

        public bool TryGetStateJsonZ<T>(string key, out T? value) where T : class
        {
            try { value = GetStateJsonZ<T>(key); return value is not null; }
            catch (FileNotFoundException) { value = null; return false; }
            catch (DirectoryNotFoundException) { value = null; return false; }
            catch (IOException) { value = null; return false; }
            catch (UnauthorizedAccessException) { value = null; return false; }
            catch (Exception) { value = null; return false; } // decompression/parse errors
        }

        public bool TryGetStateJsonZ<T>(string key, string statestore, out T? value) where T : class
        {
            try { value = GetStateJsonZ<T>(key, statestore); return value is not null; }
            catch (FileNotFoundException) { value = null; return false; }
            catch (DirectoryNotFoundException) { value = null; return false; }
            catch (IOException) { value = null; return false; }
            catch (UnauthorizedAccessException) { value = null; return false; }
            catch (Exception) { value = null; return false; }
        }

        public bool TryGetStateStringJsonZ<T>(string key, out T? value) where T : class
        {
            try { value = GetStateStringJsonZ<T>(key); return value is not null; }
            catch (FileNotFoundException) { value = null; return false; }
            catch (DirectoryNotFoundException) { value = null; return false; }
            catch (IOException) { value = null; return false; }
            catch (UnauthorizedAccessException) { value = null; return false; }
            catch (Exception) { value = null; return false; }
        }

        public bool TryGetStateStringJsonZ<T>(string key, string statestore, out T? value) where T : class
        {
            try { value = GetStateStringJsonZ<T>(key, statestore); return value is not null; }
            catch (FileNotFoundException) { value = null; return false; }
            catch (DirectoryNotFoundException) { value = null; return false; }
            catch (IOException) { value = null; return false; }
            catch (UnauthorizedAccessException) { value = null; return false; }
            catch (Exception) { value = null; return false; }
        }
        // ---------- Try* (async) ----------
        public async Task<(bool ok, T? value)> TryGetStateJsonAsync<T>(string key) where T : class
        {
            try { var v = await GetStateJsonAsync<T>(key).ConfigureAwait(false); return (v is not null, v); }
            catch (FileNotFoundException) { return (false, null); }
            catch (DirectoryNotFoundException) { return (false, null); }
            catch (IOException) { return (false, null); }
            catch (UnauthorizedAccessException) { return (false, null); }
        }

        public async Task<(bool ok, T? value)> TryGetStateJsonAsync<T>(string key, string statestore) where T : class
        {
            try { var v = await GetStateJsonAsync<T>(key, statestore).ConfigureAwait(false); return (v is not null, v); }
            catch (FileNotFoundException) { return (false, null); }
            catch (DirectoryNotFoundException) { return (false, null); }
            catch (IOException) { return (false, null); }
            catch (UnauthorizedAccessException) { return (false, null); }
        }

        public async Task<(bool ok, T? value)> TryGetStateJsonZAsync<T>(string key) where T : class
        {
            try { var v = await GetStateJsonZAsync<T>(key).ConfigureAwait(false); return (v is not null, v); }
            catch (FileNotFoundException) { return (false, null); }
            catch (DirectoryNotFoundException) { return (false, null); }
            catch (IOException) { return (false, null); }
            catch (UnauthorizedAccessException) { return (false, null); }
            catch (Exception) { return (false, null); }
        }

        public async Task<(bool ok, T? value)> TryGetStateJsonZAsync<T>(string key, string statestore) where T : class
        {
            try { var v = await GetStateJsonZAsync<T>(key, statestore).ConfigureAwait(false); return (v is not null, v); }
            catch (FileNotFoundException) { return (false, null); }
            catch (DirectoryNotFoundException) { return (false, null); }
            catch (IOException) { return (false, null); }
            catch (UnauthorizedAccessException) { return (false, null); }
            catch (Exception) { return (false, null); }
        }

        public async Task<(bool ok, T? value)> TryGetStateStringJsonZAsync<T>(string key) where T : class
        {
            try { var v = await GetStateStringJsonZAsync<T>(key).ConfigureAwait(false); return (v is not null, v); }
            catch (FileNotFoundException) { return (false, null); }
            catch (DirectoryNotFoundException) { return (false, null); }
            catch (IOException) { return (false, null); }
            catch (UnauthorizedAccessException) { return (false, null); }
            catch (Exception) { return (false, null); }
        }

        public async Task<(bool ok, T? value)> TryGetStateStringJsonZAsync<T>(string key, string statestore) where T : class
        {
            try { var v = await GetStateStringJsonZAsync<T>(key, statestore).ConfigureAwait(false); return (v is not null, v); }
            catch (FileNotFoundException) { return (false, null); }
            catch (DirectoryNotFoundException) { return (false, null); }
            catch (IOException) { return (false, null); }
            catch (UnauthorizedAccessException) { return (false, null); }
            catch (Exception) { return (false, null); }
        }

    }
}
