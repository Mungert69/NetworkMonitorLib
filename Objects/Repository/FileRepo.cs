
using System.Collections.Generic;
using NetworkMonitor.Utils;
using Microsoft.Extensions.Logging;
using System.IO;
using Nito.AsyncEx;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;

namespace NetworkMonitor.Objects.Repository
{
    public interface IFileRepo
    {
        string PrefixPath { get; }
        Task ShutdownAsync();
        void CheckFileExists(string filename, ILogger logger);
        void CheckFileExistsWithCreateObject<T>(string filename, T obj, ILogger logger) where T : class;
        void CheckFileExistsWithCreateJsonZObject<T>(string filename, T obj, ILogger logger) where T : class;
        
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
    }

    public class FileRepo : IFileRepo
    {
        private ConcurrentDictionary<string, SemaphoreSlim> fileLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        private bool _isRunningOnMauiAndroid = false;
        private string _prefixPath = "";
        public string PrefixPath { get => _prefixPath; }
        public FileRepo()
        {
            _isRunningOnMauiAndroid = false;
        }
        public FileRepo(bool isRunningOnMauiAndroid, string prefixPath = "")
        {
            _isRunningOnMauiAndroid = isRunningOnMauiAndroid;
            _prefixPath = prefixPath;
        }

        public async Task ShutdownAsync()
        {
            // Wait for all file operations to complete
            foreach (var fileLock in fileLocks.Values)
            {
                using (await fileLock.LockAsync())
                {
                    // This block will execute once the lock is available,
                    // indicating that the file operation is complete.
                }
            }
        }
        private string GetFilePath(string key)
        {
            if (_isRunningOnMauiAndroid)
            {
                return Path.Combine(_prefixPath, key);
            }
            return key;
        }

        public void CheckFileExists(string filename, ILogger logger)
        {
            filename = GetFilePath(filename);
            if (!File.Exists(filename))
            {
                File.Create(filename).Close();
                logger.LogWarning("Warning : Creating file " + filename + " this will have no data!");
            }
        }

        public void CheckFileExistsWithCreateObject<T>(string filename, T obj, ILogger logger)  where T : class
        {
            filename = GetFilePath(filename);
            if (!File.Exists(filename))
            {
                File.Create(filename).Close();
                WriteFileString(filename, JsonUtils.WriteJsonObjectToString<T>(obj));
                logger.LogWarning($"Warning : Creating file {filename} this will have an empty {typeof(T)} object");
            }
        }

         public void CheckFileExistsWithCreateJsonZObject<T>(string filename, T obj, ILogger logger)  where T : class
        {
            filename = GetFilePath(filename);
            if (!File.Exists(filename))
            {
                File.Create(filename).Close();
                  var json = JsonUtils.WriteJsonObjectToString<T>(obj);
            byte[] jsonZ = StringCompressor.CompressToBytes(json);
            WriteFileBytes(filename, jsonZ);
                logger.LogWarning($"Warning : Creating file {filename} this will have an empty {typeof(T)} object");
            }
        }

        public bool IsFileExists(string filename)
        {
            filename = GetFilePath(filename);
            return File.Exists(filename);
        }
        //private readonly string _statestore = "statestore";
        private void WriteFileString(string key, string jsonStr)
        {
            key = GetFilePath(key);
            using (StreamWriter writer = new StreamWriter(key))
            {
                writer.WriteLine(jsonStr);
                writer.Close();
            }
        }
        /*
        public async Task WriteFileStringAsync(string key, string jsonStr)
        {
            key = GetFilePath(key);
            var fileLock = fileLocks.GetOrAdd(key, new AsyncLock());

            using (await fileLock.LockAsync())
            {
                using (StreamWriter writer = new StreamWriter(key))
                {
                    await writer.WriteLineAsync(jsonStr);
                }
            }
        }*/

        public async Task WriteFileStringAsync(string key, string jsonStr)
        {
            key = GetFilePath(key);
            var fileLock = fileLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

            await fileLock.WaitAsync();
            try
            {
                using (StreamWriter writer = new StreamWriter(key))
                {
                    await writer.WriteLineAsync(jsonStr);
                }
            }
            finally
            {
                fileLock.Release();
            }
        }

        private void WriteFileBytes(string key, byte[] bytes)
        {
            key = GetFilePath(key);
            using (StreamWriter writer = new StreamWriter(key))
            {
                writer.BaseStream.Write(bytes, 0, bytes.Length);
                writer.Close();
            }
        }
        /*public async Task WriteFileBytesAsync(string key, byte[] bytes)
        {
            key = GetFilePath(key);
            var fileLock = fileLocks.GetOrAdd(key, new AsyncLock());

            using (await fileLock.LockAsync())
            {
                await File.WriteAllBytesAsync(key, bytes);
            }
        }*/
        public async Task WriteFileBytesAsync(string key, byte[] bytes)
        {
            key = GetFilePath(key);
            var fileLock = fileLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

            await fileLock.WaitAsync();
            try
            {
                await File.WriteAllBytesAsync(key, bytes);
            }
            finally
            {
                fileLock.Release();
            }
        }


        private string ReadFileString(string key)
        {
            key = GetFilePath(key);
            return File.ReadAllText(key);
        }
        private byte[] ReadFileBytes(string key)
        {
            key = GetFilePath(key);
            return File.ReadAllBytes(key);
        }

       /* public async Task<string> ReadFileStringAsync(string key)
        {
            key = GetFilePath(key);
            var fileLock = fileLocks.GetOrAdd(key, new AsyncLock());

            using (await fileLock.LockAsync())
            {
                return await File.ReadAllTextAsync(key);
            }
        }*/

        public async Task<string> ReadFileStringAsync(string key)
        {
            key = GetFilePath(key);
            var fileLock = fileLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

            await fileLock.WaitAsync();
            try
            {
                return await File.ReadAllTextAsync(key);
            }
            finally
            {
                fileLock.Release();
            }
        }


       /* public async Task<byte[]> ReadFileBytesAsync(string key)
        {
            key = GetFilePath(key);
            var fileLock = fileLocks.GetOrAdd(key, new AsyncLock());

            using (await fileLock.LockAsync())
            {
                return await File.ReadAllBytesAsync(key);
            }
        }*/

        public async Task<byte[]> ReadFileBytesAsync(string key)
        {
            key = GetFilePath(key);
            var fileLock = fileLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

            await fileLock.WaitAsync();
            try
            {
                return await File.ReadAllBytesAsync(key);
            }
            finally
            {
                fileLock.Release();
            }
        }

        public T? GetStateJson<T>(string key, string statestore) where T : class
        {
            return (JsonUtils.GetJsonObjectFromString<T>(ReadFileString(statestore + "/" + key)));
        }
        public T? GetStateJson<T>(string key) where T : class
        {
            return (JsonUtils.GetJsonObjectFromString<T>(ReadFileString(key)));
        }
        public T? GetStateJsonZ<T>(string key, string statestore) where T : class
        {
            var bytes = ReadFileBytes(statestore + "/" + key);
            var json = StringCompressor.Decompress(bytes);
            T? obj = JsonUtils.GetJsonObjectFromString<T>(json);
            return (obj);
        }
        public T? GetStateJsonZ<T>(string key) where T : class
        {
            var bytes = ReadFileBytes(key);
            var json = StringCompressor.Decompress(bytes);
            T? obj = JsonUtils.GetJsonObjectFromString<T>(json);
            return (obj);
        }

        public T? GetStateStringJsonZ<T>(string key, string statestore) where T : class
        {
            var str = ReadFileString(statestore + "/" + key);
            var json = StringCompressor.Decompress(str);
            T? obj = JsonUtils.GetJsonObjectFromString<T>(json);
            return (obj);
        }
        public T? GetStateStringJsonZ<T>(string key) where T : class
        {
            var str = ReadFileString(key);
            var json = StringCompressor.Decompress(str);
            T? obj = JsonUtils.GetJsonObjectFromString<T>(json);
            return (obj);
        }

        public async Task<T?> GetStateJsonAsync<T>(string key, string statestore) where T : class
        {
            return JsonUtils.GetJsonObjectFromString<T>(await ReadFileStringAsync(statestore + "/" + key));
        }

        public async Task<T?> GetStateJsonAsync<T>(string key) where T : class
        {
            return JsonUtils.GetJsonObjectFromString<T>(await ReadFileStringAsync(key));
        }

        public async Task<T?> GetStateJsonZAsync<T>(string key, string statestore) where T : class
        {
            var bytes = await ReadFileBytesAsync(statestore + "/" + key);
            var json = StringCompressor.Decompress(bytes);
            return JsonUtils.GetJsonObjectFromString<T>(json);
        }

        public async Task<T?> GetStateJsonZAsync<T>(string key) where T : class
        {
            var bytes = await ReadFileBytesAsync(key);
            var json = StringCompressor.Decompress(bytes);
            return JsonUtils.GetJsonObjectFromString<T>(json);
        }

        public async Task<T?> GetStateStringJsonZAsync<T>(string key, string statestore) where T : class
        {
            var str = await ReadFileStringAsync(statestore + "/" + key);
            var json = StringCompressor.Decompress(str);
            return JsonUtils.GetJsonObjectFromString<T>(json);
        }

        public async Task<T?> GetStateStringJsonZAsync<T>(string key) where T : class
        {
            var str = await ReadFileStringAsync(key);
            var json = StringCompressor.Decompress(str);
            return JsonUtils.GetJsonObjectFromString<T>(json);
        }

        public void SaveStateJson<T>(string key, T obj, string statestore) where T : class
        {
            WriteFileString(statestore + "/" + key, JsonUtils.WriteJsonObjectToString<T>(obj));
        }
        public void SaveStateJson<T>(string key, T obj) where T : class
        {
            WriteFileString(key, JsonUtils.WriteJsonObjectToString<T>(obj));
        }
        public void SaveStateString(string key, string obj, string statestore)
        {
            WriteFileString(statestore + "/" + key, obj);
        }
        public void SaveStateString(string key, string obj)
        {
            WriteFileString(key, obj);
        }
        public void SaveStateBytes(string key, byte[] bytes, string statestore)
        {
            WriteFileBytes(statestore + "/" + key, bytes);
        }
        public void SaveStateBytes(string key, byte[] bytes)
        {
            WriteFileBytes(key, bytes);
        }
        public byte[] SaveStateJsonZ<T>(string key, T obj, string statestore) where T : class
        {
            var json = JsonUtils.WriteJsonObjectToString<T>(obj);
            byte[] jsonZ = StringCompressor.CompressToBytes(json);
            WriteFileBytes(statestore + "/" + key, jsonZ);
            return jsonZ;
        }
        public byte[] SaveStateJsonZ<T>(string key, T obj) where T : class
        {
            var json = JsonUtils.WriteJsonObjectToString<T>(obj);
            byte[] jsonZ = StringCompressor.CompressToBytes(json);
            WriteFileBytes(key, jsonZ);
            return jsonZ;
        }

        public string SaveStateStringJsonZ<T>(string key, T obj, string statestore) where T : class
        {
            var json = JsonUtils.WriteJsonObjectToString<T>(obj);
            string jsonZ = StringCompressor.Compress(json);
            WriteFileString(statestore + "/" + key, jsonZ);
            return jsonZ;
        }
        public string SaveStateStringJsonZ<T>(string key, T obj) where T : class
        {
            var json = JsonUtils.WriteJsonObjectToString<T>(obj);
            string jsonZ = StringCompressor.Compress(json);
            WriteFileString(key, jsonZ);
            return jsonZ;
        }

        public async Task SaveStateJsonAsync<T>(string key, T obj, string statestore) where T : class
        {
            await WriteFileStringAsync(statestore + "/" + key, JsonUtils.WriteJsonObjectToString<T>(obj));
        }

        public async Task SaveStateJsonAsync<T>(string key, T obj) where T : class
        {
            await WriteFileStringAsync(key, JsonUtils.WriteJsonObjectToString<T>(obj));
        }

        public async Task SaveStateStringAsync(string key, string obj, string statestore)
        {
            await WriteFileStringAsync(statestore + "/" + key, obj);
        }

        public async Task SaveStateStringAsync(string key, string obj)
        {
            await WriteFileStringAsync(key, obj);
        }

        public async Task SaveStateBytesAsync(string key, byte[] bytes, string statestore)
        {
            await WriteFileBytesAsync(statestore + "/" + key, bytes);
        }

        public async Task SaveStateBytesAsync(string key, byte[] bytes)
        {
            await WriteFileBytesAsync(key, bytes);
        }

        public async Task<byte[]> SaveStateJsonZAsync<T>(string key, T obj, string statestore) where T : class
        {
            var json = JsonUtils.WriteJsonObjectToString<T>(obj);
            byte[] jsonZ = StringCompressor.CompressToBytes(json);
            await WriteFileBytesAsync(statestore + "/" + key, jsonZ);
            return jsonZ;
        }

        public async Task<byte[]> SaveStateJsonZAsync<T>(string key, T obj) where T : class
        {
            var json = JsonUtils.WriteJsonObjectToString<T>(obj);
            byte[] jsonZ = StringCompressor.CompressToBytes(json);
            await WriteFileBytesAsync(key, jsonZ);
            return jsonZ;
        }


        public async Task<string> SaveStateStringJsonZAsync<T>(string key, T obj, string statestore) where T : class
        {
            var json = JsonUtils.WriteJsonObjectToString<T>(obj);
            string jsonZ = StringCompressor.Compress(json);
            await WriteFileStringAsync(statestore + "/" + key, jsonZ);
            return jsonZ;
        }
        public async Task<string> SaveStateStringJsonZAsync<T>(string key, T obj) where T : class
        {
            var json = JsonUtils.WriteJsonObjectToString<T>(obj);
            string jsonZ = StringCompressor.Compress(json);
            await WriteFileStringAsync(key, jsonZ);
            return jsonZ;
        }

    }
}