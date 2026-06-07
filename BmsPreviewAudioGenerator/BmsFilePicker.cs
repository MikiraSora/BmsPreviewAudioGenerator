using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;

namespace BmsPreviewAudioGenerator
{
    internal static class BmsFilePicker
    {
        private const long OneKb = 1024;
        private const long OneMb = 1024 * 1024;
        private const long TooSmallFileThresholdBytes = OneKb;
        private const long TooLargeFileThresholdBytes = 4 * OneMb;
        private const long PreferredMinFileSizeBytes = 50 * OneKb;
        private const long PreferredMaxFileSizeBytes = 200 * OneKb;
        private const int MaxLineLengthBytes = 65335;
        private const int ScoringFailureScore = -100;

        public static string Pick(IEnumerable<string> bmsFilePaths)
        {
            if (bmsFilePaths is null)
                throw new ArgumentNullException(nameof(bmsFilePaths));

            var candidates = new List<(string Path, string SortPath, int Score, bool ScoringSucceeded)>();

            foreach (var bmsFilePath in bmsFilePaths)
            {
                var score = 0;
                var sortPath = bmsFilePath ?? string.Empty;
                var scoringSucceeded = false;

                if (string.IsNullOrWhiteSpace(bmsFilePath))
                {
                    candidates.Add((bmsFilePath, sortPath, ScoringFailureScore, scoringSucceeded));
                    continue;
                }

                try
                {
                    var file = new FileInfo(bmsFilePath);
                    sortPath = file.FullName;

                    if (file.Length > TooLargeFileThresholdBytes || file.Length < TooSmallFileThresholdBytes)
                        score -= 1;

                    if (file.Length >= PreferredMinFileSizeBytes && file.Length <= PreferredMaxFileSizeBytes)
                        score += 1;

                    var baseName = Path.GetFileNameWithoutExtension(file.Name);
                    if (baseName?.IndexOf("bug", StringComparison.OrdinalIgnoreCase) >= 0)
                        score -= 2;

                    if (ShouldCheckLongLine(file.Extension) && HasLineLongerThan(file.FullName, MaxLineLengthBytes))
                        score -= 2;

                    scoringSucceeded = true;
                }
                catch (Exception ex) when (IsScoringException(ex))
                {
                    score = ScoringFailureScore;
                    sortPath = GetBestEffortFullPath(bmsFilePath);
                }

                candidates.Add((bmsFilePath, sortPath, score, scoringSucceeded));
            }

            if (candidates.Count == 0)
                throw new InvalidOperationException("No BMS file paths were provided.");

            if (!candidates.Any(x => x.ScoringSucceeded))
                throw new InvalidOperationException("No BMS file paths could be scored.");

            return candidates
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.SortPath, StringComparer.OrdinalIgnoreCase)
                .First()
                .Path;
        }

        private static bool ShouldCheckLongLine(string extension)
        {
            return string.Equals(extension, ".bms", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".bme", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".bml", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".pms", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasLineLongerThan(string filePath, int maxLineLengthBytes)
        {
            var buffer = new byte[8192];
            var lineLength = 0;

            using var stream = File.OpenRead(filePath);

            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                    return false;

                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] == '\r' || buffer[i] == '\n')
                    {
                        lineLength = 0;
                        continue;
                    }

                    lineLength++;
                    if (lineLength > maxLineLengthBytes)
                        return true;
                }
            }
        }

        private static string GetBestEffortFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception ex) when (IsScoringException(ex))
            {
                return path ?? string.Empty;
            }
        }

        private static bool IsScoringException(Exception ex)
        {
            return ex is IOException
                || ex is UnauthorizedAccessException
                || ex is ArgumentException
                || ex is NotSupportedException
                || ex is SecurityException;
        }
    }
}
