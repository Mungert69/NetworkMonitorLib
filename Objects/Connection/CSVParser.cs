using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Reflection;
using NetworkMonitor.Objects;
using System.Diagnostics.CodeAnalysis;
namespace NetworkMonitor.Connection;
public class HexStringToIntConverter
{
    public static int ConvertFromString(string text)
    {
        text = text.Trim();
        text = text.Replace("0x", "");
        return int.TryParse(text, NumberStyles.HexNumber | NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int result) ? result : 0;
    }

    public static string ConvertToString(int value)
    {
        return $"0x{value:x}";
    }
}

public class YesNoToBoolConverter
{
    public static bool ConvertFromString(string text)
    {
        return string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase);
    }

    public static string ConvertToString(bool value)
    {
        return value ? "yes" : "no";
    }
}

public class CsvParser
{

    public static List<AlgorithmInfo> ParseAlgorithmInfoCsv(string filePath)
    {
        var records = new List<AlgorithmInfo>();

        using (var reader = new StreamReader(filePath))
        {
            string? line;
            bool isFirstLine = true;

            while ((line = reader.ReadLine()) != null)
            {
                var fields = line.Split(',');

                if (isFirstLine)
                {
                    isFirstLine = false;
                    continue; // Skip the header line
                }

                var record = new AlgorithmInfo
                {
                    AlgorithmName = fields[0],
                    DefaultID = HexStringToIntConverter.ConvertFromString(fields[1]),
                    Enabled = YesNoToBoolConverter.ConvertFromString(fields[2]),
                    EnvironmentVariable = fields[3],
                    AddEnv = YesNoToBoolConverter.ConvertFromString(fields[4])
                };
                records.Add(record);
            }
        }

        return records;
    }

    public static void WriteAlgorithmInfoCsv(string filePath, List<AlgorithmInfo> algoTable)
    {
        using (var writer = new StreamWriter(filePath))
        {
            // Write headers
            writer.WriteLine("AlgorithmName,DefaultID,Enabled,EnvironmentVariable,AddEnv");

            foreach (var record in algoTable)
            {
                writer.Write($"{record.AlgorithmName},");
                writer.Write($"{HexStringToIntConverter.ConvertToString(record.DefaultID)},");
                writer.Write($"{YesNoToBoolConverter.ConvertToString(record.Enabled)},");
                writer.Write($"{record.EnvironmentVariable},");
                writer.Write($"{YesNoToBoolConverter.ConvertToString(record.AddEnv)}");
                writer.WriteLine();
            }
        }
    }
     public static List<T> ParseCsv<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(string filePath) 
        where T : new()
    {
        var records = new List<T>();

        using (var reader = new StreamReader(filePath))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var fields = line.Split(',');

                T record = new T();
                PropertyInfo[] properties = typeof(T).GetProperties();

                for (int i = 0; i < fields.Length; i++)
                {
                    PropertyInfo property = properties[i];
                    object value = Convert.ChangeType(fields[i], property.PropertyType);
                    property.SetValue(record, value);
                }

                records.Add(record);
            }
        }

        return records;
    }

    public static void WriteCsv<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(string filePath, List<T> objects)
    {
        using (var writer = new StreamWriter(filePath))
        {
            foreach (var obj in objects)
            {
                PropertyInfo[] properties = typeof(T).GetProperties();
                List<string> fields = new List<string>();

                foreach (var property in properties)
                {
                    object? value = property.GetValue(obj);
                    fields.Add(value?.ToString() ?? "");

                }

                writer.WriteLine(string.Join(",", fields));
            }
        }
    }

}
