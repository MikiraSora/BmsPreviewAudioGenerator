using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ude;

namespace BmsPreviewAudioGenerator
{
    internal sealed class BmsResourceResolver
    {
        private static readonly ConcurrentDictionary<string, Encoding> CachedEncodings = new();

        private readonly string dirPath;
        private readonly HashSet<string> supportExtensionNames;

        public BmsResourceResolver(string dirPath, IEnumerable<string> supportExtensionNames)
        {
            this.dirPath = dirPath;
            this.supportExtensionNames = supportExtensionNames
                .Select(x => x.ToLowerInvariant())
                .ToHashSet();
        }

        public BmsEncodingDetectionResult DetectEncoding(string bmsFilePath)
        {
            var data = File.ReadAllBytes(bmsFilePath);
            var detectedCharset = DetectCharset(data);
            var detectedEncoding = TryGetEncoding(detectedCharset);
            var candidates = CreateEncodingCandidates(detectedEncoding).ToArray();

            var scores = candidates
                .Select((x, i) => ScoreEncoding(data, x, i))
                .ToArray();

            var detectedScore = detectedEncoding == null
                ? default
                : scores.FirstOrDefault(x => IsSameEncoding(x.Encoding, detectedEncoding));

            var bestScore = scores
                .OrderByDescending(x => x.MatchedAudioCount)
                .ThenByDescending(x => x.WavReferenceCount)
                .ThenBy(x => x.Order)
                .FirstOrDefault();

            var selectedScore = bestScore;
            if (detectedScore != null && detectedScore.MatchedAudioCount >= bestScore.MatchedAudioCount)
                selectedScore = detectedScore;

            if (selectedScore == null)
                selectedScore = new BmsEncodingCandidateScore(Encoding.UTF8, 0, 0, 0);

            return new BmsEncodingDetectionResult(
                selectedScore.Encoding,
                detectedCharset,
                selectedScore.MatchedAudioCount,
                selectedScore.WavReferenceCount,
                detectedScore != null && !IsSameEncoding(selectedScore.Encoding, detectedScore.Encoding));
        }

        public string ResolveAudioFile(string audioPathName, Action<string> log = null)
        {
            if (string.IsNullOrWhiteSpace(audioPathName))
                return default;

            var normalizedAudioPathName = audioPathName.Replace('\\', '/');
            var path = dirPath;
            var split = normalizedAudioPathName.Split('/');
            var fileName = normalizedAudioPathName;

            if (split.Length > 1)
            {
                fileName = split.LastOrDefault();
                for (int i = 0; i < split.Length - 1; i++)
                    path = Path.Combine(path, split[i]);
            }

            var exactPath = FindExactFile(path, fileName);
            if (exactPath != null)
                return exactPath;

            var filePath = FindFile(path, fileName);
            if (filePath != null)
                return filePath;

            if (Directory.Exists(path))
            {
                foreach (var subPath in EnumerateDirectoriesSafe(path))
                {
                    exactPath = FindExactFile(subPath, fileName);
                    if (exactPath != null)
                    {
                        log?.Invoke($".bms require audio file {fileName} is found but it is located in a sub folder: {Path.GetDirectoryName(exactPath)}");
                        return exactPath;
                    }

                    filePath = FindFile(subPath, fileName);
                    if (filePath != null)
                    {
                        log?.Invoke($".bms require audio file {fileName} is found but it is located in a sub folder: {Path.GetDirectoryName(filePath)}");
                        return filePath;
                    }
                }
            }

            var actualSearchPattern = $"{Path.GetFileNameWithoutExtension(fileName)}.*";
            log?.Invoke($".bms require audio file {fileName} is not found, ignored. search pattern: {Path.Combine(path, actualSearchPattern)}");
            return default;
        }

        private static string DetectCharset(byte[] data)
        {
            var detector = new CharsetDetector();
            detector.Feed(data, 0, data.Length);
            detector.DataEnd();

            return detector.Charset;
        }

        private static Encoding TryGetEncoding(string charset)
        {
            if (string.IsNullOrWhiteSpace(charset))
                return default;

            if (CachedEncodings.TryGetValue(charset, out var encoding))
                return encoding;

            try
            {
                encoding = Encoding.GetEncoding(charset);
                CachedEncodings[charset] = encoding;
                return encoding;
            }
            catch
            {
                return default;
            }
        }

        private static IEnumerable<Encoding> CreateEncodingCandidates(Encoding detectedEncoding)
        {
            var encodings = new List<Encoding>();

            Add(detectedEncoding);
            Add(Encoding.UTF8);
            Add(TryGetEncoding("shift_jis"));
            Add(TryGetEncoding("gb18030"));
            Add(TryGetEncoding("big5"));
            Add(TryGetEncoding("ks_c_5601-1987"));
            Add(TryGetEncoding("windows-1252"));

            return encodings;

            void Add(Encoding encoding)
            {
                if (encoding == null)
                    return;

                if (encodings.Any(x => IsSameEncoding(x, encoding)))
                    return;

                encodings.Add(encoding);
            }
        }

        private BmsEncodingCandidateScore ScoreEncoding(byte[] data, Encoding encoding, int order)
        {
            var content = encoding.GetString(data);
            var wavReferences = ExtractWavReferences(content).ToArray();
            var matchedAudioCount = wavReferences.Count(x => ResolveAudioFile(x) != null);

            return new BmsEncodingCandidateScore(encoding, matchedAudioCount, wavReferences.Length, order);
        }

        private static IEnumerable<string> ExtractWavReferences(string content)
        {
            using var reader = new StringReader(content);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length < 8 || line[0] != '#')
                    continue;

                if (!MatchesReserveWord(line, "WAV"))
                    continue;

                if (!IsBase36(line[4]) || !IsBase36(line[5]))
                    continue;

                var fileName = line.Substring(7).Trim().Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(fileName))
                    yield return fileName;
            }
        }

        private static bool MatchesReserveWord(string line, string word)
        {
            if (line.Length <= word.Length)
                return false;

            for (int i = 0; i < word.Length; i++)
            {
                var actual = line[i + 1];
                var expected = word[i];
                if (actual != expected && actual != expected + 32)
                    return false;
            }

            return true;
        }

        private static bool IsBase36(char value)
        {
            return value >= '0' && value <= '9'
                || value >= 'a' && value <= 'z'
                || value >= 'A' && value <= 'Z';
        }

        private string FindFile(string path, string audioPathName)
        {
            if (!Directory.Exists(path))
                return default;

            var actualSearchPattern = $"{Path.GetFileNameWithoutExtension(audioPathName)}.*";
            return EnumerateFilesSafe(path, actualSearchPattern)
                .Where(CheckSupportedExtension)
                .FirstOrDefault();
        }

        private static string FindExactFile(string path, string fileName)
        {
            try
            {
                var exactPath = Path.Combine(path, fileName);
                return File.Exists(exactPath) ? exactPath : default;
            }
            catch (Exception e) when (IsPathError(e))
            {
                return default;
            }
        }

        private static IEnumerable<string> EnumerateFilesSafe(string path, string searchPattern)
        {
            try
            {
                return Directory.EnumerateFiles(path, searchPattern);
            }
            catch (Exception e) when (IsPathError(e))
            {
                return Enumerable.Empty<string>();
            }
        }

        private static IEnumerable<string> EnumerateDirectoriesSafe(string path)
        {
            try
            {
                return Directory.EnumerateDirectories(path);
            }
            catch (Exception e) when (IsPathError(e))
            {
                return Enumerable.Empty<string>();
            }
        }

        private static bool IsPathError(Exception e)
        {
            return e is ArgumentException
                || e is IOException
                || e is NotSupportedException
                || e is PathTooLongException
                || e is UnauthorizedAccessException;
        }

        private bool CheckSupportedExtension(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return supportExtensionNames.Contains(ext.ToLowerInvariant());
        }

        private static bool IsSameEncoding(Encoding left, Encoding right)
        {
            return left != null && right != null && left.CodePage == right.CodePage;
        }
    }

    internal sealed class BmsEncodingDetectionResult
    {
        public BmsEncodingDetectionResult(
            Encoding encoding,
            string detectedCharset,
            int matchedAudioCount,
            int wavReferenceCount,
            bool selectedByAudioMatch)
        {
            Encoding = encoding;
            DetectedCharset = detectedCharset;
            MatchedAudioCount = matchedAudioCount;
            WavReferenceCount = wavReferenceCount;
            SelectedByAudioMatch = selectedByAudioMatch;
        }

        public Encoding Encoding { get; }

        public string DetectedCharset { get; }

        public int MatchedAudioCount { get; }

        public int WavReferenceCount { get; }

        public bool SelectedByAudioMatch { get; }

        public string ToLogMessage()
        {
            var detected = string.IsNullOrWhiteSpace(DetectedCharset) ? "unknown" : DetectedCharset;
            var reason = SelectedByAudioMatch ? "selected by audio match" : "selected by detector";
            return $"selected charset:{Encoding.WebName} (Ude detected:{detected}, audio match:{MatchedAudioCount}/{WavReferenceCount}, {reason})";
        }
    }

    internal sealed class BmsEncodingCandidateScore
    {
        public BmsEncodingCandidateScore(Encoding encoding, int matchedAudioCount, int wavReferenceCount, int order)
        {
            Encoding = encoding;
            MatchedAudioCount = matchedAudioCount;
            WavReferenceCount = wavReferenceCount;
            Order = order;
        }

        public Encoding Encoding { get; }

        public int MatchedAudioCount { get; }

        public int WavReferenceCount { get; }

        public int Order { get; }
    }
}
