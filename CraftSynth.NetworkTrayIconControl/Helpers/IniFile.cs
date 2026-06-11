using System;
using System.Collections.Generic;
using System.IO;

namespace CraftSynth.NetworkTrayIconControl.Helpers;

public static class IniFile
{
    public static void Write(string section, string key, string value, string filePath)
    {
        var lines = File.Exists(filePath) ? new List<string>(File.ReadAllLines(filePath)) : new List<string>();

        bool inTargetSection = false;
        bool keyWritten = false;

        for (int i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                if (inTargetSection && !keyWritten)
                {
                    lines.Insert(i, $"{key}={value}");
                    keyWritten = true;
                    break;
                }
                inTargetSection = string.Equals(trimmed[1..^1].Trim(), section, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inTargetSection && !trimmed.StartsWith(';') && !trimmed.StartsWith('#'))
            {
                var eq = trimmed.IndexOf('=');
                if (eq > 0 && string.Equals(trimmed[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"{key}={value}";
                    keyWritten = true;
                    break;
                }
            }
        }

        if (!keyWritten)
        {
            if (!inTargetSection)
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                    lines.Add(string.Empty);
                lines.Add($"[{section}]");
            }
            lines.Add($"{key}={value}");
        }

        File.WriteAllLines(filePath, lines);
    }

    public static string Read(string section, string key, string defaultValue, string filePath)
    {
        if (!File.Exists(filePath))
            return defaultValue;

        var currentSection = string.Empty;
        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (line.StartsWith(';') || line.StartsWith('#') || line.Length == 0)
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            if (string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(line[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                return line[(eq + 1)..].Trim();
            }
        }

        return defaultValue;
    }
}
