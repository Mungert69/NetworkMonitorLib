using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace NetworkMonitor.Security.Tests
{
    public class EnvFileStoreTests
    {
        [Fact]
        public async Task SetAsync_SetsOwnerOnlyPermissionsOnLinux()
        {
            if (!OperatingSystem.IsLinux())
            {
                return;
            }

            var directory = Path.Combine(Path.GetTempPath(), $"env-store-{Guid.NewGuid():N}");
            var envPath = Path.Combine(directory, ".env");

            try
            {
                var store = new EnvFileStore(envPath);
                await store.SetAsync("TestKey", "TestValue");

                const UnixFileMode accessPermissions =
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead |
                    UnixFileMode.GroupWrite |
                    UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead |
                    UnixFileMode.OtherWrite |
                    UnixFileMode.OtherExecute;

                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(envPath) & accessPermissions);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }
    }
}
