using System;
using System.Collections.Generic;
using System.IO;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    public class CsvParserTests : IDisposable
    {
        private readonly List<string> _tempFiles = new();

        private string CreateTempFile(params string[] lines)
        {
            string path = Path.Combine(Path.GetTempPath(), $"csvtest_{Guid.NewGuid():N}.csv");
            File.WriteAllLines(path, lines);
            _tempFiles.Add(path);
            return path;
        }

        public void Dispose()
        {
            foreach (var file in _tempFiles)
            {
                try
                {
                    if (File.Exists(file)) File.Delete(file);
                }
                catch
                {
                    // best effort cleanup
                }
            }
        }

        [Fact]
        public void HexStringConverter_RoundTrips()
        {
            var input = " 0x1A ";
            int value = HexStringToIntConverter.ConvertFromString(input);
            Assert.Equal(0x1A, value);
            Assert.Equal("0x1a", HexStringToIntConverter.ConvertToString(value));
        }

        [Theory]
        [InlineData("yes", true)]
        [InlineData("YES", true)]
        [InlineData("no", false)]
        [InlineData("anything", false)]
        public void YesNoConverter_ConvertsBothWays(string input, bool expected)
        {
            bool result = YesNoToBoolConverter.ConvertFromString(input);
            Assert.Equal(expected, result);
            Assert.Equal(result ? "yes" : "no", YesNoToBoolConverter.ConvertToString(result));
        }

        [Fact]
        public void ParseAlgorithmInfoCsv_ReadsRecords()
        {
            string path = CreateTempFile(
                "AlgorithmName,DefaultID,Enabled,EnvironmentVariable,AddEnv",
                "AlgoA,0x1A,yes,ENV_A,No",
                "AlgoB,0xFF,no,ENV_B,yes");

            var records = CsvParser.ParseAlgorithmInfoCsv(path);

            Assert.Equal(2, records.Count);
            Assert.Equal("AlgoA", records[0].AlgorithmName);
            Assert.Equal(0x1A, records[0].DefaultID);
            Assert.True(records[0].Enabled);
            Assert.False(records[0].AddEnv);
            Assert.Equal("AlgoB", records[1].AlgorithmName);
            Assert.Equal(0xFF, records[1].DefaultID);
            Assert.False(records[1].Enabled);
            Assert.True(records[1].AddEnv);
        }

        [Fact]
        public void WriteAlgorithmInfoCsv_WritesExpectedFormat()
        {
            var path = Path.Combine(Path.GetTempPath(), $"csvtest_{Guid.NewGuid():N}.csv");
            _tempFiles.Add(path);

            var records = new List<AlgorithmInfo>
            {
                new AlgorithmInfo { AlgorithmName = "AlgoX", DefaultID = 0x2B, Enabled = true, EnvironmentVariable = "ENV_X", AddEnv = false }
            };

            CsvParser.WriteAlgorithmInfoCsv(path, records);

            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.Equal("AlgorithmName,DefaultID,Enabled,EnvironmentVariable,AddEnv", lines[0]);
            Assert.Equal("AlgoX,0x2b,yes,ENV_X,no", lines[1]);
        }

        private class SampleRecord
        {
            public string Name { get; set; } = string.Empty;
            public int Value { get; set; }
        }

        [Fact]
        public void ParseCsv_GenericParsesRecords()
        {
            string path = CreateTempFile("Alice,42", "Bob,7");

            var records = CsvParser.ParseCsv<SampleRecord>(path);

            Assert.Collection(records,
                r => { Assert.Equal("Alice", r.Name); Assert.Equal(42, r.Value); },
                r => { Assert.Equal("Bob", r.Name); Assert.Equal(7, r.Value); });
        }

        [Fact]
        public void WriteCsv_GenericWritesRecords()
        {
            var data = new List<SampleRecord>
            {
                new SampleRecord { Name = "Carol", Value = 5 }
            };
            string path = Path.Combine(Path.GetTempPath(), $"csvtest_{Guid.NewGuid():N}.csv");
            _tempFiles.Add(path);

            CsvParser.WriteCsv(path, data);

            var lines = File.ReadAllLines(path);
            Assert.Single(lines);
            Assert.Equal("Carol,5", lines[0]);
        }
    }
}
